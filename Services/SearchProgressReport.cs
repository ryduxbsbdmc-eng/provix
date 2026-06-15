using FileExplorer.Models;

namespace FileExplorer.Services;

public sealed class SearchProgressReport
{
    public IReadOnlyList<FileSystemEntry> NewItems { get; init; } = [];
    public int TotalCount { get; init; }
    public int ScannedDirectoryCount { get; init; }
    public bool IsComplete { get; init; }
    public bool IsTruncated { get; init; }
}
