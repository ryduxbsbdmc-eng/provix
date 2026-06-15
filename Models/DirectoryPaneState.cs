using System.Collections.ObjectModel;
using FileExplorer.Controls;

namespace FileExplorer.Models;

public sealed class DirectoryPaneState
{
    public required DirectoryPaneControl Control { get; init; }
    public string? CurrentPath { get; set; }
    public Stack<string> BackHistory { get; } = new();
    public Stack<string> ForwardHistory { get; } = new();
    public CancellationTokenSource? SearchCancellation { get; set; }
    public CancellationTokenSource? DebounceCancellation { get; set; }
    public ObservableCollection<FileSystemEntry>? LiveSearchResults { get; set; }
    public bool SuppressPathSearchBoxUpdate { get; set; }
    public int ListingRefreshVersion { get; set; }
}
