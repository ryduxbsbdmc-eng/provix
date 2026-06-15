using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using FileExplorer.Models;

namespace FileExplorer.Services;

public sealed class FileSystemService
{
    private const int SearchBatchItemThreshold = 100;
    private const int SearchBatchMsThreshold = 50;

    private static readonly EnumerationOptions SearchEnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint
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
        var results = new ConcurrentBag<FileSystemEntry>();
        var pendingDirectories = new ConcurrentQueue<string>();
        pendingDirectories.Enqueue(rootPath);

        var batchBuffer = new List<FileSystemEntry>();
        var batchLock = new object();
        var stopwatch = Stopwatch.StartNew();
        var lastFlushMs = 0L;

        void FlushBatch(bool forceComplete = false)
        {
            List<FileSystemEntry> snapshot;
            int total;

            lock (batchLock)
            {
                if (!forceComplete && batchBuffer.Count == 0)
                    return;

                snapshot = batchBuffer.ToList();
                batchBuffer.Clear();
                total = results.Count;
                lastFlushMs = stopwatch.ElapsedMilliseconds;
            }

            if (snapshot.Count == 0 && !forceComplete)
                return;

            progress?.Report(new SearchProgressReport
            {
                NewItems = snapshot,
                TotalCount = total,
                IsComplete = forceComplete
            });
        }

        void AddMatch(FileSystemEntry entry)
        {
            results.Add(entry);

            var shouldFlush = false;
            lock (batchLock)
            {
                batchBuffer.Add(entry);
                shouldFlush =
                    batchBuffer.Count >= SearchBatchItemThreshold ||
                    stopwatch.ElapsedMilliseconds - lastFlushMs >= SearchBatchMsThreshold;
            }

            if (shouldFlush)
                FlushBatch();
        }

        await Task.Run(() =>
        {
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            };

            while (!pendingDirectories.IsEmpty)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var currentBatch = new List<string>();
                while (currentBatch.Count < Math.Max(Environment.ProcessorCount * 4, 16) &&
                       pendingDirectories.TryDequeue(out var directory))
                {
                    currentBatch.Add(directory);
                }

                if (currentBatch.Count == 0)
                    break;

                Parallel.ForEach(currentBatch, parallelOptions, directory =>
                {
                    parallelOptions.CancellationToken.ThrowIfCancellationRequested();

                    var directoryName = Path.GetFileName(directory);
                    if (directoryName.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase))
                        AddMatch(CreateDirectoryEntry(new DirectoryInfo(directory)));

                    foreach (var file in Directory.EnumerateFiles(directory, "*", SearchEnumerationOptions))
                    {
                        parallelOptions.CancellationToken.ThrowIfCancellationRequested();

                        var fileName = Path.GetFileName(file);
                        if (fileName.Contains(trimmedQuery, StringComparison.OrdinalIgnoreCase))
                            AddMatch(CreateFileEntry(new FileInfo(file)));
                    }

                    foreach (var subdirectory in Directory.EnumerateDirectories(directory, "*", SearchEnumerationOptions))
                        pendingDirectories.Enqueue(subdirectory);
                });
            }
        }, cancellationToken);

        FlushBatch(forceComplete: true);

        return SortEntries(results.ToList());
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
