using FileExplorer.Models;

namespace FileExplorer.Services;

public sealed class SearchProgressReport
{
    public required IReadOnlyList<FileSystemEntry> NewItems { get; init; }
    public int TotalCount { get; init; }
    public bool IsComplete { get; init; }
}
