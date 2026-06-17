using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileExplorer.Controls;
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer;

public partial class MainWindow
{
    private readonly ContentSearchService _contentSearchService = new();

    private ObservableCollection<ContentSearchMatch>? _contentSearchResults;
    private CancellationTokenSource? _previewCancellation;
    private DirectoryPaneState? _contentSearchPane;

    private void InitializeExplorerFeatures()
    {
        _bookmarkService.LoadFromSettings();
        BookmarksListBox.ItemsSource = _bookmarkService.Items;

        FilePreviewPanelControl.CloseRequested += (_, _) => SetPreviewPanelVisible(false);
        ApplyPreviewPanelVisibility(SettingsManager.Instance.Current.ShowPreviewPanel);

        ContentSearchBox.Text = ContentSearchService.QueryPrefix;
    }

    private void WirePaneExplorerFeatures(DirectoryPaneState pane)
    {
        pane.EnsureDefaultTab();
        pane.Control.RefreshTabBar();

        pane.Control.FileListSelectionChanged += (_, _) => OnPaneFileListSelectionChanged(pane);
        pane.Control.TabSelected += (_, e) => SwitchPaneTab(pane, e.Tab);
        pane.Control.TabCloseRequested += (_, e) => ClosePaneTab(pane, e.Tab);
        pane.Control.TabMoveRequested += (_, e) => HandleTabMove(pane, e);
        pane.Control.NewTabRequested += (_, _) => OpenNewPaneTab(pane);
        pane.Control.OpenInOtherPaneRequested += (_, _) => OpenSelectionInOtherPane(pane);
        pane.Control.CompareFoldersRequested += (_, _) => CompareWithOtherPane(pane);
        pane.Control.AddBookmarkRequested += (_, _) => AddBookmarkFromPane(pane);
        pane.Control.RemoveBookmarkRequested += (_, _) => RemoveBookmarkFromPane(pane);
    }

    private void OnPaneFileListSelectionChanged(DirectoryPaneState pane)
    {
        if (pane != _activePane || !SettingsManager.Instance.Current.ShowPreviewPanel)
            return;

        if (pane.Control.FileListView.SelectedItems.Count != 1)
        {
            FilePreviewPanelControl.ClearPreview();
            return;
        }

        if (pane.Control.FileListView.SelectedItem is not FileSystemEntry entry)
            return;

        _ = UpdateFilePreviewAsync(entry);
    }

    private async Task UpdateFilePreviewAsync(FileSystemEntry entry)
    {
        _previewCancellation?.Cancel();
        _previewCancellation?.Dispose();
        _previewCancellation = new CancellationTokenSource();
        var token = _previewCancellation.Token;

        try
        {
            await FilePreviewPanelControl.ShowPathAsync(entry.FullPath, entry.IsDirectory).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Superseded.
        }
    }

    private void SetPreviewPanelVisible(bool isVisible)
    {
        SettingsManager.Instance.Current.ShowPreviewPanel = isVisible;
        SettingsManager.Instance.Save();
        ApplyPreviewPanelVisibility(isVisible);

        if (!isVisible)
            FilePreviewPanelControl.ClearPreview();
        else if (_activePane is not null)
            OnPaneFileListSelectionChanged(_activePane);
    }

    private void ApplyPreviewPanelVisibility(bool isVisible)
    {
        PreviewPanelColumn.Width = isVisible
            ? new GridLength(SettingsManager.Instance.Current.PreviewPanelWidth, GridUnitType.Pixel)
            : new GridLength(0);

        PreviewPanelSplitter.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        FilePreviewPanelControl.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        PreviewToggleButton.Opacity = isVisible ? 1.0 : 0.55;
    }

    private void PreviewToggleButton_Click(object sender, RoutedEventArgs e) =>
        SetPreviewPanelVisible(!SettingsManager.Instance.Current.ShowPreviewPanel);

    private void SyncActiveTabPath(DirectoryPaneState pane)
    {
        pane.EnsureDefaultTab();
        if (pane.ActiveTab is not null)
            pane.ActiveTab.Path = pane.CurrentPath;

        pane.Control.RefreshTabBar();
    }

    private void SwitchPaneTab(DirectoryPaneState pane, PaneTab tab)
    {
        if (ReferenceEquals(pane.ActiveTab, tab))
            return;

        if (pane.ActiveTab is not null)
            pane.ActiveTab.Path = pane.CurrentPath;

        pane.ActiveTab = tab;
        pane.Control.RefreshTabBar();

        if (!string.IsNullOrWhiteSpace(tab.Path) && Directory.Exists(tab.Path))
        {
            NavigateToDirectory(pane, tab.Path, syncTree: pane == _activePane, recordHistory: false);
            return;
        }

        pane.CurrentPath = tab.Path;
        if (!string.IsNullOrWhiteSpace(tab.Path))
            SetPathSearchBoxText(pane, tab.Path);

        pane.Control.RefreshTabBar();
        if (pane == _activePane)
            UpdateNavigationButtons(pane);
    }

    private void OpenNewPaneTab(DirectoryPaneState pane)
    {
        var tab = new PaneTab { Path = pane.CurrentPath };
        pane.Tabs.Add(tab);
        SwitchPaneTab(pane, tab);
    }

    private void ClosePaneTab(DirectoryPaneState pane, PaneTab tab)
    {
        if (pane.Tabs.Count <= 1)
            return;

        var index = FindPaneTabIndex(pane, tab);
        if (index < 0)
            return;

        var closingActiveTab = ReferenceEquals(pane.ActiveTab, tab);
        if (closingActiveTab)
            pane.ActiveTab = null;

        pane.Tabs.RemoveAt(index);

        if (closingActiveTab)
        {
            var nextIndex = Math.Min(index, pane.Tabs.Count - 1);
            SwitchPaneTab(pane, pane.Tabs[nextIndex]);
        }
        else
        {
            pane.Control.RefreshTabBar();
        }
    }

    private static int FindPaneTabIndex(DirectoryPaneState pane, PaneTab tab)
    {
        for (var i = 0; i < pane.Tabs.Count; i++)
        {
            if (ReferenceEquals(pane.Tabs[i], tab) || pane.Tabs[i].Id == tab.Id)
                return i;
        }

        return -1;
    }

    private static PaneTab? ResolvePaneTab(DirectoryPaneState pane, PaneTab tab)
    {
        var index = FindPaneTabIndex(pane, tab);
        return index >= 0 ? pane.Tabs[index] : null;
    }

    private void HandleTabMove(DirectoryPaneState targetPane, PaneTabMoveEventArgs e)
    {
        if (PaneTabDragSession.MoveCompleted)
            return;

        var sourcePane = e.SourceControl.PaneState;
        if (sourcePane is null)
            return;

        var tab = ResolvePaneTab(sourcePane, e.Tab);
        if (tab is null)
            return;

        var sourceIndex = FindPaneTabIndex(sourcePane, tab);
        if (sourceIndex < 0)
            return;

        var movingActiveTab = ReferenceEquals(sourcePane.ActiveTab, tab);
        if (movingActiveTab)
        {
            tab.Path = sourcePane.CurrentPath ?? tab.Path;
            sourcePane.ActiveTab = null;
        }

        var targetIsSource = ReferenceEquals(sourcePane, targetPane);
        var insertIndex = Math.Clamp(e.InsertIndex, 0, targetIsSource ? sourcePane.Tabs.Count : targetPane.Tabs.Count);

        if (targetIsSource)
        {
            if (insertIndex == sourceIndex || insertIndex == sourceIndex + 1)
                return;

            sourcePane.Tabs.RemoveAt(sourceIndex);
            if (insertIndex > sourceIndex)
                insertIndex--;

            insertIndex = Math.Clamp(insertIndex, 0, sourcePane.Tabs.Count);
            sourcePane.Tabs.Insert(insertIndex, tab);
            sourcePane.Control.RefreshTabBar();
            PaneTabDragSession.MarkMoveCompleted();
            return;
        }

        sourcePane.Tabs.RemoveAt(sourceIndex);

        if (sourcePane.Tabs.Count == 0)
        {
            sourcePane.EnsureDefaultTab();
            sourcePane.Control.RefreshTabBar();
        }
        else if (movingActiveTab)
        {
            var nextIndex = Math.Min(sourceIndex, sourcePane.Tabs.Count - 1);
            SwitchPaneTab(sourcePane, sourcePane.Tabs[nextIndex]);
        }
        else
        {
            sourcePane.Control.RefreshTabBar();
        }

        insertIndex = Math.Clamp(insertIndex, 0, targetPane.Tabs.Count);
        targetPane.Tabs.Insert(insertIndex, tab);
        SwitchPaneTab(targetPane, tab);
        SetActivePane(targetPane);
        sourcePane.Control.RefreshTabBar();
        targetPane.Control.RefreshTabBar();

        PaneTabDragSession.MarkMoveCompleted();
        StatusText.Text = LocalizationManager.Instance["UI_TabMoved"];
    }

    private void BookmarksNavigationButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateBookmarksEmptyState();
        BookmarksPopup.IsOpen = !BookmarksPopup.IsOpen;
    }

    private void UpdateBookmarksEmptyState()
    {
        var hasItems = _bookmarkService.Items.Count > 0;
        BookmarksListBox.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        BookmarksEmptyText.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        BookmarksClearButton.IsEnabled = hasItems;
        BookmarkAddCurrentButton.IsEnabled = !string.IsNullOrWhiteSpace(ActivePane.CurrentPath);
    }

    private async void BookmarksListBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (BookmarksListBox.SelectedItem is not BookmarkItem item)
            return;

        BookmarksPopup.IsOpen = false;

        if (!Directory.Exists(item.Path))
        {
            StatusText.Text = LocalizationManager.Instance["UI_BookmarkMissing"];
            return;
        }

        CancelPaneSearch(ActivePane);
        NavigateToDirectory(ActivePane, item.Path, syncTree: true);
        await Task.CompletedTask;
    }

    private void BookmarkAddCurrentButton_Click(object sender, RoutedEventArgs e)
    {
        var path = ActivePane.CurrentPath;
        if (string.IsNullOrWhiteSpace(path))
            return;

        _bookmarkService.Add(path);
        UpdateBookmarksEmptyState();
        UpdatePaneFileListContextMenuState(ActivePane);
        StatusText.Text = LocalizationManager.Instance["UI_BookmarkAdded"];
    }

    private void BookmarksClearButton_Click(object sender, RoutedEventArgs e)
    {
        _bookmarkService.Clear();
        UpdateBookmarksEmptyState();
        BookmarksPopup.IsOpen = false;
        foreach (var pane in _panes)
            UpdatePaneFileListContextMenuState(pane);
    }

    private void AddBookmarkFromPane(DirectoryPaneState pane)
    {
        var path = ResolveBookmarkPath(pane);
        if (string.IsNullOrWhiteSpace(path))
            return;

        _bookmarkService.Add(path);
        UpdateBookmarksEmptyState();
        UpdatePaneFileListContextMenuState(pane);
        StatusText.Text = LocalizationManager.Instance["UI_BookmarkAdded"];
    }

    private void RemoveBookmarkFromPane(DirectoryPaneState pane)
    {
        var path = ResolveBookmarkPath(pane);
        if (string.IsNullOrWhiteSpace(path))
            return;

        _bookmarkService.Remove(path);
        UpdateBookmarksEmptyState();
        UpdatePaneFileListContextMenuState(pane);
        StatusText.Text = LocalizationManager.Instance["UI_BookmarkRemoved"];
    }

    private static string? ResolveBookmarkPath(DirectoryPaneState pane)
    {
        if (pane.Control.FileListView.SelectedItems.Count == 1 &&
            pane.Control.FileListView.SelectedItem is FileSystemEntry entry &&
            entry.IsDirectory)
        {
            return entry.FullPath;
        }

        return pane.CurrentPath;
    }

    private void OpenSelectionInOtherPane(DirectoryPaneState sourcePane)
    {
        if (_panes.Count < 2)
        {
            var newPane = CreatePane();
            AppendPaneToHost(newPane.Control);
            NavigateToDirectory(newPane, sourcePane.CurrentPath ?? string.Empty, syncTree: false, recordHistory: false);
        }

        var targetPane = _panes.FirstOrDefault(p => !ReferenceEquals(p, sourcePane)) ?? _panes.Last();
        var path = ResolveOpenInOtherPanePath(sourcePane);
        if (string.IsNullOrWhiteSpace(path))
            return;

        SetActivePane(targetPane);
        if (Directory.Exists(path))
            NavigateToDirectory(targetPane, path, syncTree: true);
        else if (File.Exists(path))
        {
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent))
                NavigateToDirectory(targetPane, parent, syncTree: true, recordHistory: false);
        }
    }

    private static string? ResolveOpenInOtherPanePath(DirectoryPaneState pane)
    {
        if (pane.Control.FileListView.SelectedItems.Count == 1 &&
            pane.Control.FileListView.SelectedItem is FileSystemEntry entry)
        {
            return entry.IsDirectory ? entry.FullPath : entry.FullPath;
        }

        return pane.CurrentPath;
    }

    private void CompareWithOtherPane(DirectoryPaneState sourcePane)
    {
        if (_panes.Count < 2)
        {
            StatusText.Text = LocalizationManager.Instance["UI_CompareNeedsTwoPanes"];
            return;
        }

        var otherPane = _panes.FirstOrDefault(p => !ReferenceEquals(p, sourcePane));
        if (otherPane is null)
            return;

        var leftPath = sourcePane.CurrentPath;
        var rightPath = otherPane.CurrentPath;

        if (sourcePane.Control.FileListView.SelectedItems.Count == 1 &&
            sourcePane.Control.FileListView.SelectedItem is FileSystemEntry entry &&
            entry.IsDirectory)
        {
            leftPath = entry.FullPath;
        }

        if (string.IsNullOrWhiteSpace(leftPath) || string.IsNullOrWhiteSpace(rightPath))
            return;

        ShowFolderCompare(leftPath, rightPath);
    }

    private void ShowFolderCompare(string leftPath, string rightPath)
    {
        var loc = LocalizationManager.Instance;
        FolderCompareTitleText.Text = loc["UI_CompareFolders"];
        FolderComparePathsText.Text = $"{leftPath}\n{rightPath}";
        FolderCompareNameColumn.Header = loc["UI_ColumnName"];
        FolderCompareLeftColumn.Header = loc["UI_CompareLeft"];
        FolderCompareRightColumn.Header = loc["UI_CompareRight"];
        FolderCompareStatusColumn.Header = loc["UI_CompareStatus"];
        FolderCompareCloseButton.Content = loc["UI_Close"];

        var entries = FolderCompareService.Compare(leftPath, rightPath)
            .Select(FolderCompareRow.FromEntry)
            .ToList();

        FolderCompareList.ItemsSource = entries;
        PushChromeDimOverlay();
        FolderCompareOverlay.Visibility = Visibility.Visible;
        StatusText.Text = string.Format(loc["UI_CompareSummary"], entries.Count);
    }

    private void FolderCompareCloseButton_Click(object sender, RoutedEventArgs e)
    {
        FolderCompareOverlay.Visibility = Visibility.Collapsed;
        PopChromeDimOverlay();
    }

    private void ContentSearchButton_Click(object sender, RoutedEventArgs e)
    {
        ContentSearchPanel.Visibility = Visibility.Visible;
        ContentSearchBox.Focus();
        ContentSearchBox.SelectAll();
    }

    private void ContentSearchCloseButton_Click(object sender, RoutedEventArgs e) =>
        HideContentSearchPanel(restoreListing: true);

    private void HideContentSearchPanel(bool restoreListing)
    {
        ContentSearchPanel.Visibility = Visibility.Collapsed;
        ContentSearchBox.Text = ContentSearchService.QueryPrefix;

        if (_contentSearchPane is not null)
        {
            CancelPaneContentSearch(_contentSearchPane);
            if (restoreListing && !string.IsNullOrWhiteSpace(_contentSearchPane.CurrentPath))
                RefreshDirectoryListing(_contentSearchPane);
        }

        _contentSearchPane = null;
        _contentSearchResults = null;
        ContentSearchResultsList.ItemsSource = null;
    }

    private async void ContentSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        await RunContentSearchAsync(ActivePane, ContentSearchBox.Text.Trim());
    }

    private async Task HandleContentSearchInputAsync(DirectoryPaneState pane, string input, CancellationToken token)
    {
        if (!ContentSearchService.TryParseQuery(input, out var query))
            return;

        if (token.IsCancellationRequested)
            return;

        await RunContentSearchAsync(pane, query, token);
    }

    private async Task RunContentSearchAsync(
        DirectoryPaneState pane,
        string query,
        CancellationToken debounceToken = default)
    {
        if (string.IsNullOrWhiteSpace(pane.CurrentPath))
        {
            StatusText.Text = LocalizationManager.Instance["UI_ContentSearchNoFolder"];
            return;
        }

        if (!ContentSearchService.TryParseQuery(query, out var parsedQuery))
            parsedQuery = query.Trim();

        if (parsedQuery.Length == 0)
            return;

        CancelPaneSearch(pane);
        CancelPaneContentSearch(pane);
        pane.ContentSearchCancellation = debounceToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(debounceToken)
            : new CancellationTokenSource();
        var token = pane.ContentSearchCancellation.Token;

        _contentSearchPane = pane;
        _contentSearchResults = [];
        pane.LiveContentSearchResults = _contentSearchResults;
        pane.IsContentSearchMode = true;
        ContentSearchResultsList.ItemsSource = _contentSearchResults;
        ContentSearchPanel.Visibility = Visibility.Visible;

        SetSearchUiState(isSearching: true);

        var progress = new Progress<SearchProgressReport>(report =>
        {
            if (token.IsCancellationRequested || pane != _activePane)
                return;

            StatusText.Text = report.IsComplete
                ? string.Format(LocalizationManager.Instance["UI_ContentSearchDone"], report.TotalCount)
                : string.Format(LocalizationManager.Instance["UI_ContentSearchProgress"], report.TotalCount, report.ScannedDirectoryCount);
        });

        try
        {
            var matches = await _contentSearchService.SearchAsync(
                pane.CurrentPath,
                parsedQuery,
                progress,
                token);

            if (token.IsCancellationRequested || _contentSearchResults is null)
                return;

            foreach (var match in matches)
                _contentSearchResults.Add(match);
        }
        catch (OperationCanceledException)
        {
            // Superseded.
        }
        catch (Exception ex)
        {
            if (pane == _activePane)
                StatusText.Text = string.Format(LocalizationManager.Instance["UI_ContentSearchError"], ex.Message);
        }
        finally
        {
            if (pane.ContentSearchCancellation?.Token == token)
            {
                pane.ContentSearchCancellation.Dispose();
                pane.ContentSearchCancellation = null;
                SetSearchUiState(isSearching: false);
            }
        }
    }

    private async void ContentSearchResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ContentSearchResultsList.SelectedItem is not ContentSearchMatch match)
            return;

        var parent = Path.GetDirectoryName(match.FilePath);
        if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
            NavigateToDirectory(ActivePane, parent, syncTree: true, recordHistory: false);

        await OpenFileFromPathAsync(match.FilePath);
    }

    private void CancelPaneContentSearch(DirectoryPaneState pane)
    {
        pane.ContentSearchCancellation?.Cancel();
        pane.ContentSearchCancellation?.Dispose();
        pane.ContentSearchCancellation = null;
        pane.IsContentSearchMode = false;
        pane.LiveContentSearchResults = null;
    }

    private void UpdateExplorerContextMenuState(DirectoryPaneState pane)
    {
        var hasOtherPane = _panes.Count > 1;
        var bookmarkPath = ResolveBookmarkPath(pane);
        var isBookmarked = !string.IsNullOrWhiteSpace(bookmarkPath) && _bookmarkService.Contains(bookmarkPath);

        pane.Control.OpenInOtherPaneMenuItemControl.IsEnabled = !string.IsNullOrWhiteSpace(pane.CurrentPath)
                                                                  || pane.Control.FileListView.SelectedItems.Count > 0;
        pane.Control.CompareFoldersMenuItemControl.IsEnabled = hasOtherPane && !string.IsNullOrWhiteSpace(pane.CurrentPath);
        pane.Control.AddBookmarkMenuItemControl.Visibility = isBookmarked ? Visibility.Collapsed : Visibility.Visible;
        pane.Control.RemoveBookmarkMenuItemControl.Visibility = isBookmarked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyExplorerFeatureLocalization(LocalizationManager loc)
    {
        BookmarksNavigationButton.ToolTip = loc["UI_Bookmarks"];
        BookmarksTitleText.Text = loc["UI_BookmarksTitle"];
        BookmarksEmptyText.Text = loc["UI_BookmarksEmpty"];
        BookmarkAddCurrentButton.Content = loc["UI_BookmarkAddCurrent"];
        BookmarksClearButton.Content = loc["UI_BookmarksClear"];
        ContentSearchButton.ToolTip = loc["UI_ContentSearch"];
        ContentSearchCloseButton.Content = loc["UI_Close"];
        PreviewToggleButton.ToolTip = loc["UI_PreviewToggle"];
        FilePreviewPanelControl.ApplyLocalization();
    }
}
