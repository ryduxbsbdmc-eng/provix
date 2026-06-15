using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using FileExplorer.Models;

namespace FileExplorer.Services;

public sealed class FileSystemService
{
    private const int MaxSearchResults = 2_500;
    private const int MaxSearchDepth = 32;
    private const int SearchStatusIntervalMs = 250;

    private static readonly HashSet<string> SkippedSearchDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".idea",
        "node_modules",
        "__pycache__",
        "bin",
        "obj",
        "packages",
        "vendor",
        "$Recycle.Bin",
        "System Volume Information",
        "WindowsApps"
    };

    private static readonly EnumerationOptions SearchEnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
    };

    private static readonly EnumerationOptions BrowseEnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false
    };

    private readonly FileIconService _iconService;

    public FileSystemService(FileIconService iconService) =>
        _iconService = iconService;

    public IReadOnlyList<DirectoryTreeNode> GetRootNodes()
    {
        var nodes = new List<DirectoryTreeNode>();

        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!string.IsNullOrWhiteSpace(desktopPath) && Directory.Exists(desktopPath))
            nodes.Add(CreateDirectoryNode(desktopPath, "Desktop"));

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady)
                continue;

            var rootPath = drive.RootDirectory.FullName;
            nodes.Add(CreateDirectoryNode(rootPath, drive.Name.TrimEnd('\\'), isDrive: true));
        }

        return nodes;
    }

    public IReadOnlyList<DirectoryTreeNode> GetDriveNodes() => GetRootNodes();

    public void LoadChildDirectories(DirectoryTreeNode parent)
    {
        if (!parent.HasDummyChild && parent.Children.Count > 0 && parent.Children[0].FullPath != string.Empty)
            return;

        parent.IsLoading = true;

        try
        {
            parent.Children.Clear();

            foreach (var child in GetChildDirectoryNodes(parent.FullPath))
                parent.Children.Add(child);
        }
        finally
        {
            parent.IsLoading = false;
        }
    }

    public Task<IReadOnlyList<FileSystemEntry>> GetDirectoryContentsAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => GetDirectoryContents(path, cancellationToken), cancellationToken);

    public IReadOnlyList<FileSystemEntry> GetDirectoryContents(string path, CancellationToken cancellationToken = default)
    {
        var entries = new List<FileSystemEntry>(256);

        foreach (var directory in Directory.EnumerateDirectories(path, "*", BrowseEnumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(CreateDirectoryEntryFromPath(directory, fastIcons: true));
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", BrowseEnumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();
            entries.Add(CreateFileEntryFromPath(file, fastIcons: true));
        }

        return SortEntries(entries);
    }

    public IReadOnlyList<DirectoryTreeNode> GetChildDirectoryNodes(string parentPath)
    {
        var nodes = new List<DirectoryTreeNode>();

        foreach (var directory in Directory.EnumerateDirectories(parentPath, "*", BrowseEnumerationOptions))
            nodes.Add(CreateDirectoryNode(directory, Path.GetFileName(directory)));

        return nodes;
    }

    public async Task<IReadOnlyList<FileSystemEntry>> SearchAsync(
        string rootPath,
        string query,
        IProgress<SearchProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        var trimmedQuery = query.Trim();
        if (trimmedQuery.Length == 0)
            return [];

        return await Task.Run(
            () => SearchCore(rootPath, trimmedQuery, progress, cancellationToken),
            cancellationToken);
    }

    private List<FileSystemEntry> SearchCore(
        string rootPath,
        string query,
        IProgress<SearchProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        var results = new List<FileSystemEntry>(Math.Min(MaxSearchResults, 256));
        var pendingDirectories = new Queue<(string Path, int Depth)>();
        pendingDirectories.Enqueue((rootPath, 0));

        var stopwatch = Stopwatch.StartNew();
        var lastStatusReportMs = 0L;
        var scannedDirectories = 0;
        var isTruncated = false;

        void ReportStatus(bool complete)
        {
            progress?.Report(new SearchProgressReport
            {
                TotalCount = results.Count,
                ScannedDirectoryCount = scannedDirectories,
                IsComplete = complete,
                IsTruncated = isTruncated
            });
            lastStatusReportMs = stopwatch.ElapsedMilliseconds;
        }

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (results.Count >= MaxSearchResults)
            {
                isTruncated = true;
                break;
            }

            var (directory, depth) = pendingDirectories.Dequeue();
            scannedDirectories++;

            try
            {
                var directoryName = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (directoryName.Length > 0 &&
                    directoryName.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(CreateSearchDirectoryEntry(directory, directoryName));
                    if (results.Count >= MaxSearchResults)
                    {
                        isTruncated = true;
                        break;
                    }
                }

                foreach (var filePath in Directory.EnumerateFiles(directory, "*", SearchEnumerationOptions))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var fileName = Path.GetFileName(filePath);
                    if (fileName.Length == 0 ||
                        !fileName.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    results.Add(CreateSearchFileEntry(filePath, fileName));
                    if (results.Count >= MaxSearchResults)
                    {
                        isTruncated = true;
                        break;
                    }
                }

                if (isTruncated)
                    break;

                if (depth >= MaxSearchDepth)
                    continue;

                foreach (var subdirectory in Directory.EnumerateDirectories(directory, "*", SearchEnumerationOptions))
                {
                    var subdirectoryName = Path.GetFileName(subdirectory);
                    if (ShouldSkipSearchDirectory(subdirectoryName))
                        continue;

                    pendingDirectories.Enqueue((subdirectory, depth + 1));
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip folders we cannot read.
            }
            catch (DirectoryNotFoundException)
            {
                // Folder disappeared during search.
            }
            catch (IOException)
            {
                // Transient IO issues while scanning.
            }

            if (stopwatch.ElapsedMilliseconds - lastStatusReportMs >= SearchStatusIntervalMs)
                ReportStatus(complete: false);
        }

        ReportStatus(complete: true);
        return SortEntries(results);
    }

    private static bool ShouldSkipSearchDirectory(string directoryName) =>
        SkippedSearchDirectoryNames.Contains(directoryName);

    private FileSystemEntry CreateSearchDirectoryEntry(string fullPath, string name) =>
        new()
        {
            Name = name,
            FullPath = fullPath,
            IsDirectory = true,
            DateModified = default,
            Type = "File folder",
            Icon = _iconService.GetFolderIcon(fullPath)
        };

    private FileSystemEntry CreateSearchFileEntry(string fullPath, string name)
    {
        long size = 0;
        DateTime modified = default;
        string extension = string.Empty;

        try
        {
            var info = new FileInfo(fullPath);
            size = info.Length;
            modified = info.LastWriteTime;
            extension = info.Extension;
        }
        catch
        {
            extension = Path.GetExtension(fullPath);
        }

        return new FileSystemEntry
        {
            Name = name,
            FullPath = fullPath,
            IsDirectory = false,
            DateModified = modified,
            Type = extension.Length > 0
                ? extension.TrimStart('.').ToUpperInvariant() + " File"
                : "File",
            Size = size,
            Icon = _iconService.GetFileIcon(fullPath)
        };
    }

    public static bool TryResolveDirectoryPath(string input, out string fullPath)
    {
        fullPath = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        try
        {
            fullPath = Path.GetFullPath(input.Trim());

            if (!Path.IsPathRooted(fullPath))
                return false;

            return Directory.Exists(fullPath);
        }
        catch
        {
            return false;
        }
    }

    private FileSystemEntry CreateDirectoryEntryFromPath(string fullPath, bool fastIcons = false)
    {
        var name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        DateTime modified = default;

        try
        {
            modified = Directory.GetLastWriteTime(fullPath);
        }
        catch
        {
            // Leave default timestamp when metadata is unavailable.
        }

        return new FileSystemEntry
        {
            Name = name,
            FullPath = fullPath,
            IsDirectory = true,
            DateModified = modified,
            Type = "File folder",
            Size = -1,
            Icon = fastIcons
                ? _iconService.GetFolderIcon()
                : _iconService.GetFolderIcon(fullPath)
        };
    }

    private FileSystemEntry CreateFileEntryFromPath(string fullPath, bool fastIcons = false)
    {
        var name = Path.GetFileName(fullPath);
        var extension = Path.GetExtension(fullPath);
        long size = 0;
        DateTime modified = default;

        try
        {
            var info = new FileInfo(fullPath);
            size = info.Length;
            modified = info.LastWriteTime;
            extension = info.Extension;
        }
        catch
        {
            // Fall back to path-based metadata.
        }

        return new FileSystemEntry
        {
            Name = name,
            FullPath = fullPath,
            IsDirectory = false,
            DateModified = modified,
            Type = extension.Length > 0
                ? extension.TrimStart('.').ToUpperInvariant() + " File"
                : "File",
            Size = size,
            Icon = fastIcons
                ? _iconService.GetFileIconByExtension(extension)
                : _iconService.GetFileIcon(fullPath)
        };
    }

    private DirectoryTreeNode CreateDirectoryNode(string fullPath, string displayName, bool isDrive = false)
    {
        var node = new DirectoryTreeNode
        {
            Name = displayName,
            FullPath = fullPath,
            Icon = isDrive
                ? _iconService.GetDriveIcon(fullPath)
                : _iconService.GetFolderIcon(fullPath)
        };

        node.Children.Add(CreateDummyChild());
        return node;
    }

    private static DirectoryTreeNode CreateDummyChild() =>
        new() { Name = string.Empty, FullPath = string.Empty };

    private static List<FileSystemEntry> SortEntries(List<FileSystemEntry> entries) =>
        entries
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
