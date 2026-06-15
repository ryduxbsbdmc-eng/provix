using System.Collections.ObjectModel;
using FileExplorer.Controls;

namespace FileExplorer.Models;

public sealed class DirectoryPaneState
{
    public required DirectoryPaneControl Control { get; init; }

    public ObservableCollection<PaneTab> Tabs { get; } = [];

    public PaneTab? ActiveTab { get; set; }

    public string? CurrentPath { get; set; }

    public Stack<string> BackHistory => ActiveTab?.BackHistory ?? _fallbackBackHistory;

    public Stack<string> ForwardHistory => ActiveTab?.ForwardHistory ?? _fallbackForwardHistory;

    private readonly Stack<string> _fallbackBackHistory = new();

    private readonly Stack<string> _fallbackForwardHistory = new();

    public CancellationTokenSource? SearchCancellation { get; set; }

    public CancellationTokenSource? ContentSearchCancellation { get; set; }

    public CancellationTokenSource? DebounceCancellation { get; set; }

    public CancellationTokenSource? ListingRefreshCancellation { get; set; }

    public ObservableCollection<FileSystemEntry>? LiveSearchResults { get; set; }

    public ObservableCollection<ContentSearchMatch>? LiveContentSearchResults { get; set; }

    public bool IsContentSearchMode { get; set; }

    public bool SuppressPathSearchBoxUpdate { get; set; }

    public int ListingRefreshVersion { get; set; }

    public int PendingTreeSyncVersion { get; set; }

    public DateTime LastListingShownUtc { get; set; }

    public void EnsureDefaultTab()
    {
        if (Tabs.Count > 0)
            return;

        var tab = new PaneTab();
        Tabs.Add(tab);
        ActiveTab = tab;
    }
}
