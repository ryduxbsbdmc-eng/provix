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

            foreach (var directory in Directory.EnumerateDirectories(parent.FullPath, "*", BrowseEnumerationOptions))
                parent.Children.Add(CreateDirectoryNode(directory, Path.GetFileName(directory)));
        }
        finally
        {
            parent.IsLoading = false;
        }
    }

    public IReadOnlyList<FileSystemEntry> GetDirectoryContents(string path)
    {
        var entries = new List<FileSystemEntry>();

        foreach (var directory in Directory.EnumerateDirectories(path, "*", BrowseEnumerationOptions))
            entries.Add(CreateDirectoryEntry(new DirectoryInfo(directory)));

        foreach (var file in Directory.EnumerateFiles(path, "*", BrowseEnumerationOptions))
            entries.Add(CreateFileEntry(new FileInfo(file)));

        return SortEntries(entries);
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

        var sharedFolderIcon = _iconService.GetSharedFolderIcon();
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
                    results.Add(CreateSearchDirectoryEntry(directory, directoryName, sharedFolderIcon));
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

    private FileSystemEntry CreateSearchDirectoryEntry(string fullPath, string name, ImageSource folderIcon) =>
        new()
        {
            Name = name,
            FullPath = fullPath,
            IsDirectory = true,
            DateModified = default,
            Type = "File folder",
            Icon = folderIcon
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

    private FileSystemEntry CreateDirectoryEntry(DirectoryInfo info) =>
        new()
        {
            Name = info.Name,
            FullPath = info.FullName,
            IsDirectory = true,
            DateModified = info.LastWriteTime,
            Type = "File folder",
            Icon = _iconService.GetFolderIcon(info.FullName)
        };

    private FileSystemEntry CreateFileEntry(FileInfo info) =>
        new()
        {
            Name = info.Name,
            FullPath = info.FullName,
            IsDirectory = false,
            DateModified = info.LastWriteTime,
            Type = info.Extension.Length > 0
                ? info.Extension.TrimStart('.').ToUpperInvariant() + " File"
                : "File",
            Size = info.Length,
            Icon = _iconService.GetFileIcon(info.FullName)
        };

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

        if (MayHaveSubdirectories(fullPath))
            node.Children.Add(CreateDummyChild());

        return node;
    }

    private static DirectoryTreeNode CreateDummyChild() =>
        new() { Name = string.Empty, FullPath = string.Empty };

    private static bool MayHaveSubdirectories(string path) =>
        Directory.EnumerateDirectories(path, "*", BrowseEnumerationOptions).Any();

    private static List<FileSystemEntry> SortEntries(List<FileSystemEntry> entries) =>
        entries
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
