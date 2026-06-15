using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FileExplorer.Controls;
using FileExplorer.Helpers;
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer;

public partial class MainWindow : Window
{
    private const int SearchDebounceMs = 600;

    private enum NamePromptMode
    {
        NewFolder,
        NewTextFile,
        GitCommit,
        GitAmend,
        GitBranch
    }

    private readonly FileIconService _iconService = new();
    private readonly FileSystemService _fileSystemService;
    private readonly RecycleBinService _recycleBinService = new();
    private readonly ArchiveService _archiveService = new();
    private readonly FileMoveService _fileMoveService = new();
    private readonly AiService _aiService = new();
    private readonly AiCommandExecutor _aiCommandExecutor = new();
    private readonly ObservableCollection<DirectoryTreeNode> _driveNodes = [];
    private readonly List<DirectoryPaneState> _panes = [];

    private DirectoryPaneState? _activePane;
    private bool _suppressTreeSelectionChange;
    private bool _suppressHistoryRecording;

    private string? _editorFilePath;
    private bool _editorIsDirty;
    private bool _suppressEditorTextChanged;
    private string _editorOriginalContent = string.Empty;

    private NamePromptMode _namePromptMode;
    private DirectoryPaneState? _namePromptTargetPane;
    private string? _namePromptGitTargetDirectory;
    private bool _isGitCommitInProgress;
    private bool _isGitHistoryInProgress;
    private bool _commitHistoryRestorePending;
    private DirectoryPaneState? _commitHistoryTargetPane;
    private string? _commitHistoryTargetDirectory;
    private bool _isEditorOpen;
    private int _chromeDimDepth;
    private List<FileSystemEntry> _pendingDeleteEntries = [];
    private DirectoryPaneState? _deleteTargetPane;

    private string? _openArchivePath;
    private bool _isArchiveViewerOpen;
    private bool _isExtracting;
    private bool _paneRemoveInProgress;
    private bool _suppressSettingsUiChange;
    private bool _suppressAiApiKeyChange;
    private bool _isAiInProgress;
    private DirectoryPaneState? _aiTargetPane;
    private List<AiCommand> _aiPendingCommands = [];
    private bool _isTerminalVisible;

    public MainWindow()
    {
        _fileSystemService = new FileSystemService(_iconService);
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
        ContentRendered += MainWindow_ContentRendered;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e) =>
        PowerShellTerminal.Stop();

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5)
        {
            RefreshActivePane();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Oem3 && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ToggleTerminalPanel();
            e.Handled = true;
        }
    }

    private void RefreshActivePane()
    {
        if (_activePane != null)
        {
            RefreshDirectoryListing(_activePane);
        }
    }

    private void MainWindow_ContentRendered(object? sender, EventArgs e)
    {
        ContentRendered -= MainWindow_ContentRendered;
        EnsureInitialPane();
    }

    private void EnsureInitialPane()
    {
        if (_panes.Count > 0)
            return;

        var pane = CreatePane();
        RebuildPaneHost();

        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!string.IsNullOrWhiteSpace(desktopPath) && Directory.Exists(desktopPath))
            NavigateToDirectory(pane, desktopPath, syncTree: false);
        else if (_driveNodes.Count > 0)
            NavigateToDirectory(pane, _driveNodes[0].FullPath, syncTree: false);

        SetActivePane(pane);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        SettingsManager.Instance.SettingChanged += OnSettingChanged;
        LocalizationManager.Instance.PropertyChanged += (_, _) => ApplyLocalizedStrings();
        ApplyLocalizedStrings();
        PopulateSettingsControls();
        LoadDriveTree();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        var loc = LocalizationManager.Instance;
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized
            ? loc["UI_Restore"]
            : loc["UI_Maximize"];
    }

    private void LoadDriveTree()
    {
        _driveNodes.Clear();

        foreach (var node in _fileSystemService.GetRootNodes())
            _driveNodes.Add(node);

        DirectoryTree.ItemsSource = _driveNodes;

        if (_driveNodes.Count == 0)
            return;

        StatusText.Text = LocalizationManager.Instance["UI_Ready"];
        EnsureInitialPane();
    }

    private DirectoryPaneState CreatePane()
    {
        var control = new DirectoryPaneControl();
        var pane = new DirectoryPaneState { Control = control };
        control.PaneState = pane;

        control.Activated += (_, _) => SetActivePane(pane);
        control.CloseRequested += (_, _) => RemovePane(pane);
        control.PathSearchTextChanged += (_, e) => PanePathSearchBox_TextChanged(pane, e);
        control.PathSearchKeyDown += (_, e) => PanePathSearchBox_KeyDown(pane, e);
        control.FileListMouseDoubleClick += (_, e) => PaneFileList_MouseDoubleClick(pane, e);
        control.FileListContextMenuOpening += (_, e) => PaneFileList_ContextMenuOpening(pane, e);
        if (control.FileListView.ContextMenu is ContextMenu fileContextMenu)
            fileContextMenu.Opened += (_, _) => UpdatePaneFileListContextMenuState(pane);
        control.NewFolderRequested += (_, _) => ShowNamePrompt(NamePromptMode.NewFolder, pane);
        control.NewTextFileRequested += (_, _) => ShowNamePrompt(NamePromptMode.NewTextFile, pane);
        control.DeleteRequested += (_, _) => DeleteSelectedItems(pane);
        control.ExtractArchiveRequested += async (_, _) => await ExtractSelectedArchivesFromPaneAsync(pane);
        control.FileDropRequested += (_, e) => HandleFileDrop(pane, e.SourcePaths, e.TargetDirectory);
        control.GitInitRequested += (_, e) => HandleGitInit(pane, e.TargetDirectory);
        control.GitCommitRequested += (_, _) => HandleGitCommitRequest(pane);
        control.GitAmendRequested += (_, _) => HandleGitAmendRequest(pane);
        control.GitHistoryRequested += (_, _) => HandleGitHistoryRequest(pane);
        control.GitBranchRequested += (_, e) => HandleGitBranchRequest(pane, e.TargetDirectory);
        control.AiExecuteQueryRequested += (_, _) => HandleAiExecuteQueryRequest(pane);

        _panes.Add(pane);
        UpdatePaneCloseButtonsVisibility();
        pane.Control.ApplyLocalization();
        return pane;
    }

    private void AddPaneButton_Click(object sender, RoutedEventArgs e)
    {
        var initialPath = ActivePane.CurrentPath;
        var pane = CreatePane();
        RebuildPaneHost(entrancePane: pane.Control);

        if (!string.IsNullOrEmpty(initialPath))
            NavigateToDirectory(pane, initialPath, syncTree: false, recordHistory: false);

        SetActivePane(pane);
    }

    private void RemovePane(DirectoryPaneState pane)
    {
        if (_panes.Count <= 1 || _paneRemoveInProgress)
            return;

        var wrapper = DirectoryPaneHost.GetPaneWrapper(pane.Control);
        if (wrapper is null)
        {
            FinishRemovePane(pane);
            return;
        }

        _paneRemoveInProgress = true;
        UiAnimationHelper.AnimatePaneExit(wrapper, () =>
        {
            _paneRemoveInProgress = false;
            FinishRemovePane(pane);
        });
    }

    private void FinishRemovePane(DirectoryPaneState pane)
    {
        CancelPaneSearch(pane);
        pane.DebounceCancellation?.Cancel();
        pane.DebounceCancellation?.Dispose();
        pane.DebounceCancellation = null;

        _panes.Remove(pane);

        if (_activePane == pane)
            SetActivePane(_panes[0]);

        if (_deleteTargetPane == pane)
            HideDeleteConfirmation(animate: false);

        if (_namePromptTargetPane == pane)
            HideNamePrompt();

        if (_commitHistoryTargetPane == pane)
            HideCommitHistoryOverlay(animate: false);

        if (_aiTargetPane == pane)
            HideAiWorkflow(animate: false);

        RebuildPaneHost();
        UpdatePaneCloseButtonsVisibility();
    }

    private void RebuildPaneHost(DirectoryPaneControl? entrancePane = null)
    {
        DirectoryPaneHost.ClearPanes();

        if (_panes.Count == 0)
            return;

        FrameworkElement? entranceWrapper = null;

        for (var i = 0; i < _panes.Count; i++)
        {
            var control = _panes[i].Control;
            var prepareEntrance = ReferenceEquals(control, entrancePane);
            var wrapper = DirectoryPaneHost.AddPaneColumn(
                control,
                addSplitterAfter: i < _panes.Count - 1,
                prepareEntrance: prepareEntrance);

            if (prepareEntrance)
                entranceWrapper = wrapper;
        }

        if (entranceWrapper is not null)
            UiAnimationHelper.AnimatePaneEntrance(entranceWrapper);
    }

    private void UpdatePaneCloseButtonsVisibility()
    {
        var canClose = _panes.Count > 1;
        foreach (var pane in _panes)
            pane.Control.SetCloseButtonVisible(canClose);
    }

    private DirectoryPaneState ActivePane =>
        _activePane ?? _panes[0];

    private void SetActivePane(DirectoryPaneState pane)
    {
        if (!_panes.Contains(pane))
            return;

        _activePane = pane;

        foreach (var candidate in _panes)
            candidate.Control.SetIsActive(candidate == pane);

        UpdateNavigationButtons(pane);

        if (!string.IsNullOrEmpty(pane.CurrentPath))
            SyncTreeToPath(pane.CurrentPath);

        if (_isTerminalVisible && !string.IsNullOrEmpty(pane.CurrentPath))
            PowerShellTerminal.SyncWorkingDirectory(pane.CurrentPath);
    }

    private async void PanePathSearchBox_TextChanged(DirectoryPaneState pane, TextChangedEventArgs e)
    {
        if (pane.SuppressPathSearchBoxUpdate)
            return;

        pane.DebounceCancellation?.Cancel();
        pane.DebounceCancellation?.Dispose();
        pane.DebounceCancellation = new CancellationTokenSource();
        var token = pane.DebounceCancellation.Token;

        try
        {
            await Task.Delay(SearchDebounceMs, token);
            await HandlePathSearchInputAsync(pane, pane.Control.PathSearchTextBox.Text.Trim(), token);
        }
        catch (OperationCanceledException)
        {
            // User typed again before debounce elapsed.
        }
    }

    private async void PanePathSearchBox_KeyDown(DirectoryPaneState pane, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;

        pane.DebounceCancellation?.Cancel();
        pane.DebounceCancellation?.Dispose();
        pane.DebounceCancellation = null;

        await HandlePathSearchInputAsync(pane, pane.Control.PathSearchTextBox.Text.Trim(), CancellationToken.None);
    }

    private async Task HandlePathSearchInputAsync(DirectoryPaneState pane, string input, CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return;

        if (string.IsNullOrEmpty(input))
        {
            CancelPaneSearch(pane);

            if (!string.IsNullOrEmpty(pane.CurrentPath))
                RefreshDirectoryListing(pane);

            return;
        }

        if (FileSystemService.TryResolveDirectoryPath(input, out var directoryPath))
        {
            CancelPaneSearch(pane);

            if (pane.CurrentPath is null || !PathsEqual(directoryPath, pane.CurrentPath))
                NavigateToDirectory(pane, directoryPath, syncTree: pane == _activePane);

            return;
        }

        if (string.IsNullOrEmpty(pane.CurrentPath))
        {
            StatusText.Text = "Select a folder before searching.";
            return;
        }

        await RunSearchAsync(pane, input, token);
    }

    private void DirectoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_suppressTreeSelectionChange)
            return;

        if (e.NewValue is not DirectoryTreeNode node || string.IsNullOrEmpty(node.FullPath))
            return;

        CancelActivePaneSearch();
        CloseEditor();
        CloseArchiveViewer();
        NavigateToDirectory(ActivePane, node.FullPath, syncTree: false);
    }

    private async void DirectoryTreeItem_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TreeViewItem { DataContext: DirectoryTreeNode node })
            return;

        if (!node.HasDummyChild)
            return;

        node.IsLoading = true;

        try
        {
            var childNodes = await Task.Run(() => _fileSystemService.GetChildDirectoryNodes(node.FullPath));
            node.Children.Clear();
            foreach (var child in childNodes)
                node.Children.Add(child);
        }
        finally
        {
            node.IsLoading = false;
        }
    }

    private void DirectoryTree_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] sourcePaths || sourcePaths.Length == 0)
            return;

        var targetDirectory = ResolveTreeViewDropTarget(e, sourcePaths);
        e.Effects = string.IsNullOrEmpty(targetDirectory)
            ? DragDropEffects.None
            : DragDropEffects.Move;
        e.Handled = true;
    }

    private void DirectoryTree_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] sourcePaths || sourcePaths.Length == 0)
            return;

        var targetDirectory = ResolveTreeViewDropTarget(e, sourcePaths);
        if (string.IsNullOrEmpty(targetDirectory))
            return;

        HandleFileDrop(ActivePane, sourcePaths, targetDirectory);
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private string? ResolveTreeViewDropTarget(DragEventArgs e, IReadOnlyList<string> sourcePaths)
    {
        var dropPoint = e.GetPosition(DirectoryTree);
        var hitResult = VisualTreeHelper.HitTest(DirectoryTree, dropPoint);

        for (var source = hitResult?.VisualHit as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
        {
            if (source is TreeViewItem { DataContext: DirectoryTreeNode node } &&
                !string.IsNullOrEmpty(node.FullPath))
            {
                var folderPath = Path.GetFullPath(node.FullPath);

                if (!IsPathInDragSource(folderPath, sourcePaths))
                    return folderPath;
            }
        }

        for (var source = e.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
        {
            if (source is TreeViewItem { DataContext: DirectoryTreeNode node } &&
                !string.IsNullOrEmpty(node.FullPath))
            {
                var folderPath = Path.GetFullPath(node.FullPath);

                if (!IsPathInDragSource(folderPath, sourcePaths))
                    return folderPath;
            }
        }

        return ActivePane.CurrentPath;
    }

    private static bool IsPathInDragSource(string path, IReadOnlyList<string> sourcePaths)
    {
        var normalized = Path.GetFullPath(path).TrimEnd('\\');

        foreach (var sourcePath in sourcePaths)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                continue;

            if (string.Equals(
                    Path.GetFullPath(sourcePath).TrimEnd('\\'),
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void HandleFileDrop(DirectoryPaneState contextPane, IReadOnlyList<string> sourcePaths, string targetDirectory)
    {
        var result = _fileMoveService.MoveItems(sourcePaths, targetDirectory);

        if (result.Success)
        {
            RefreshAfterFileMove(contextPane);
            StatusText.Text = result.MovedCount == 1
                ? "Moved 1 item."
                : $"Moved {result.MovedCount} items.";
        }
        else
        {
            StatusText.Text = result.ErrorMessage ?? "Unable to move items.";
        }

        if (result.Success && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            StatusText.Text = result.ErrorMessage;
    }

    private void RefreshAfterFileMove(DirectoryPaneState contextPane)
    {
        foreach (var pane in _panes)
        {
            if (string.IsNullOrEmpty(pane.CurrentPath))
                continue;

            RefreshDirectoryListing(pane);
        }

        _suppressTreeSelectionChange = true;
        try
        {
            ReloadLoadedTreeBranches();

            if (!string.IsNullOrEmpty(contextPane.CurrentPath))
                SyncTreeToPath(contextPane.CurrentPath);
        }
        finally
        {
            _suppressTreeSelectionChange = false;
        }
    }

    private void ReloadLoadedTreeBranches()
    {
        foreach (var root in _driveNodes)
            ReloadTreeBranch(root);
    }

    private void ReloadTreeBranch(DirectoryTreeNode node)
    {
        if (string.IsNullOrEmpty(node.FullPath) || node.HasDummyChild)
            return;

        if (node.Children.Count > 0)
        {
            node.Children.Clear();
            _fileSystemService.LoadChildDirectories(node);
        }

        foreach (var child in node.Children.ToList())
            ReloadTreeBranch(child);
    }

    private async void PaneFileList_MouseDoubleClick(DirectoryPaneState pane, MouseButtonEventArgs e)
    {
        SetActivePane(pane);

        if (pane.Control.FileListView.SelectedItem is not FileSystemEntry entry)
            return;

        if (entry.IsDirectory)
        {
            CancelPaneSearch(pane);
            CloseEditor();
            CloseArchiveViewer();
            NavigateToDirectory(pane, entry.FullPath, syncTree: true);
            return;
        }

        if (ArchiveHelper.IsArchiveFile(entry.FullPath))
        {
            await OpenArchiveViewerAsync(entry.FullPath);
            return;
        }

        if (TextFileHelper.IsEditableTextFile(entry.FullPath))
        {
            if (!TextFileHelper.IsWithinEditorSizeLimit(entry.FullPath, out var fileSize))
            {
                OpenFileWithDefaultApplication(entry.FullPath);
                StatusText.Text = $"Opened with default app ({TextFileHelper.FormatByteSize(fileSize)} exceeds built-in editor limit).";
                return;
            }

            await OpenFileInEditorAsync(entry.FullPath);
            return;
        }

        OpenFileWithDefaultApplication(entry.FullPath);
    }

    private void NavigateToDirectory(
        DirectoryPaneState pane,
        string path,
        bool syncTree,
        bool recordHistory = true)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
            {
                StatusText.Text = "Folder not found.";
                return;
            }

            if (recordHistory &&
                !_suppressHistoryRecording &&
                pane.CurrentPath is not null &&
                !PathsEqual(pane.CurrentPath, fullPath))
            {
                pane.BackHistory.Push(pane.CurrentPath);
                pane.ForwardHistory.Clear();
            }

            CloseEditor();
            CloseArchiveViewer();
            pane.CurrentPath = fullPath;
            SetPathSearchBoxText(pane, fullPath);

            if (syncTree && pane == _activePane)
                SyncTreeToPath(fullPath);

            RefreshDirectoryListing(pane);

            if (pane == _activePane)
                UpdateNavigationButtons(pane);
        }
        catch (UnauthorizedAccessException)
        {
            StatusText.Text = "Access denied to this folder.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private void BackNavigationButton_Click(object sender, RoutedEventArgs e)
    {
        var pane = ActivePane;
        if (pane.BackHistory.Count == 0 || pane.CurrentPath is null)
            return;

        var target = pane.BackHistory.Pop();
        pane.ForwardHistory.Push(pane.CurrentPath);

        NavigatePaneFromHistory(pane, target);
    }

    private void ForwardNavigationButton_Click(object sender, RoutedEventArgs e)
    {
        var pane = ActivePane;
        if (pane.ForwardHistory.Count == 0 || pane.CurrentPath is null)
            return;

        var target = pane.ForwardHistory.Pop();
        pane.BackHistory.Push(pane.CurrentPath);

        NavigatePaneFromHistory(pane, target);
    }

    private void NavigatePaneFromHistory(DirectoryPaneState pane, string path)
    {
        _suppressHistoryRecording = true;
        try
        {
            CancelPaneSearch(pane);
            CloseEditor();
            NavigateToDirectory(pane, path, syncTree: pane == _activePane, recordHistory: false);
        }
        finally
        {
            _suppressHistoryRecording = false;
            if (pane == _activePane)
                UpdateNavigationButtons(pane);
        }
    }

    private void UpdateNavigationButtons(DirectoryPaneState pane)
    {
        BackNavigationButton.IsEnabled = pane.BackHistory.Count > 0;
        ForwardNavigationButton.IsEnabled = pane.ForwardHistory.Count > 0;
    }

    private async void RefreshDirectoryListing(DirectoryPaneState pane)
    {
        if (string.IsNullOrEmpty(pane.CurrentPath))
            return;

        var path = pane.CurrentPath;
        pane.ListingRefreshVersion++;
        var refreshVersion = pane.ListingRefreshVersion;

        ClearPaneFileListSelection(pane);

        IReadOnlyList<FileSystemEntry> contents;
        try
        {
            contents = await _fileSystemService.GetDirectoryContentsAsync(path);
        }
        catch (Exception ex)
        {
            if (pane == _activePane)
                StatusText.Text = $"Error: {ex.Message}";
            return;
        }

        if (refreshVersion != pane.ListingRefreshVersion ||
            !string.Equals(pane.CurrentPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        pane.Control.FileListView.ItemsSource = contents;

        if (pane == _activePane)
            StatusText.Text = $"{contents.Count} item(s)";

        await ApplyGitMetadataAsync(pane, contents, refreshVersion);
    }

    private async Task ApplyGitMetadataAsync(
        DirectoryPaneState pane,
        IReadOnlyList<FileSystemEntry> entries,
        int refreshVersion)
    {
        if (string.IsNullOrEmpty(pane.CurrentPath))
        {
            pane.Control.UpdateGitStatus(false, string.Empty, 0);
            return;
        }

        var repoRoot = GitRepositoryHelper.GetGitRepositoryRoot(pane.CurrentPath);
        if (repoRoot is null)
        {
            pane.Control.UpdateGitStatus(false, string.Empty, 0);
            return;
        }

        var status = await GitService.GetWorkingTreeStatusAsync(repoRoot);
        if (refreshVersion != pane.ListingRefreshVersion)
            return;

        if (!status.Success)
        {
            pane.Control.UpdateGitStatus(false, string.Empty, 0);
            return;
        }

        pane.Control.UpdateGitStatus(true, status.BranchName, status.ChangedFileCount);

        if (entries.Count == 0 || status.Changes.Count == 0)
            return;

        var repoRootNormalized = Path.GetFullPath(repoRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var fileChanges = new Dictionary<string, GitFileStatusType>(StringComparer.OrdinalIgnoreCase);
        var modifiedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var change in status.Changes)
        {
            fileChanges[change.FilePath] = change.Status;

            var parts = change.FilePath.Split('/');
            var currentPath = string.Empty;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                currentPath = i == 0 ? parts[i] : $"{currentPath}/{parts[i]}";
                modifiedDirectories.Add(currentPath);
            }
        }

        foreach (var entry in entries)
        {
            var relativePath = Path.GetRelativePath(repoRootNormalized, entry.FullPath).Replace('\\', '/');

            if (entry.IsDirectory)
            {
                if (modifiedDirectories.Contains(relativePath))
                    entry.GitStatus = GitFileStatusType.Modified;
            }
            else if (fileChanges.TryGetValue(relativePath, out var fileStatus))
            {
                entry.GitStatus = fileStatus;
            }
        }

        if (refreshVersion == pane.ListingRefreshVersion)
            pane.Control.FileListView.Items.Refresh();
    }

    private static void ClearPaneFileListSelection(DirectoryPaneState pane)
    {
        var listView = pane.Control.FileListView;
        listView.SelectedItems.Clear();
        listView.UnselectAll();
    }

    private async Task RunSearchAsync(DirectoryPaneState pane, string query, CancellationToken debounceToken)
    {
        if (string.IsNullOrEmpty(pane.CurrentPath))
            return;

        CancelPaneSearch(pane);
        pane.SearchCancellation = CancellationTokenSource.CreateLinkedTokenSource(debounceToken);
        var token = pane.SearchCancellation.Token;
        var searchRoot = pane.CurrentPath;

        pane.LiveSearchResults = [];
        ClearPaneFileListSelection(pane);
        pane.Control.FileListView.ItemsSource = pane.LiveSearchResults;

        SetSearchUiState(isSearching: true);

        var progress = new Progress<SearchProgressReport>(report =>
        {
            if (token.IsCancellationRequested || pane != _activePane)
                return;

            var status = report.IsComplete
                ? report.IsTruncated
                    ? $"Found {report.TotalCount}+ matches for \"{query}\" (limit reached)"
                    : $"Found {report.TotalCount} match(es) for \"{query}\" in {searchRoot}"
                : $"Searching... {report.TotalCount} match(es), {report.ScannedDirectoryCount} folder(s) scanned";

            StatusText.Text = status;
        });

        try
        {
            var finalResults = await _fileSystemService.SearchAsync(
                searchRoot,
                query,
                progress,
                token);

            if (token.IsCancellationRequested || pane.LiveSearchResults is null)
                return;

            pane.LiveSearchResults = new ObservableCollection<FileSystemEntry>(finalResults);
            pane.Control.FileListView.ItemsSource = pane.LiveSearchResults;

            if (pane == _activePane)
            {
                var truncated = finalResults.Count >= 2_500;
                StatusText.Text = truncated
                    ? $"Found {finalResults.Count}+ matches for \"{query}\" (limit reached)"
                    : $"Found {finalResults.Count} match(es) for \"{query}\" in {searchRoot}";
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by newer input; keep status unchanged.
        }
        catch (Exception ex)
        {
            if (pane == _activePane)
                StatusText.Text = $"Search error: {ex.Message}";
        }
        finally
        {
            if (pane.SearchCancellation?.Token == token)
            {
                pane.SearchCancellation.Dispose();
                pane.SearchCancellation = null;

                if (_panes.All(candidate => candidate.SearchCancellation is null))
                    SetSearchUiState(isSearching: false);
            }
        }
    }

    private void CancelPaneSearch(DirectoryPaneState pane)
    {
        if (pane.SearchCancellation is null)
            return;

        pane.SearchCancellation.Cancel();
        pane.SearchCancellation.Dispose();
        pane.SearchCancellation = null;

        if (_panes.All(candidate => candidate.SearchCancellation is null))
            SetSearchUiState(isSearching: false);
    }

    private void CancelActivePaneSearch()
    {
        if (_activePane is not null)
            CancelPaneSearch(_activePane);
    }

    private void SetSearchUiState(bool isSearching)
    {
        SearchingIndicator.Visibility = isSearching ? Visibility.Visible : Visibility.Collapsed;
        Cursor = isSearching ? Cursors.Wait : Cursors.Arrow;
    }

    private void SetPathSearchBoxText(DirectoryPaneState pane, string text)
    {
        pane.SuppressPathSearchBoxUpdate = true;
        pane.Control.PathSearchTextBox.Text = text;
        pane.SuppressPathSearchBoxUpdate = false;
    }

    private async Task OpenFileInEditorAsync(string filePath)
    {
        try
        {
            if (!TextFileHelper.IsWithinEditorSizeLimit(filePath, out var fileSize))
            {
                OpenFileWithDefaultApplication(filePath);
                StatusText.Text = $"Opened with default app ({TextFileHelper.FormatByteSize(fileSize)} exceeds built-in editor limit).";
                return;
            }

            PushChromeDimOverlay();
            UiAnimationHelper.ShowOverlay(EditorOverlay);
            EditorTextBox.IsEnabled = false;
            EditorSaveButton.IsEnabled = false;
            EditorFileNameText.Text = Path.GetFileName(filePath);
            StatusText.Text = $"Loading {Path.GetFileName(filePath)}...";

            var content = await TextFileHelper.ReadTextForEditorAsync(filePath);

            _editorFilePath = filePath;
            _editorOriginalContent = content;
            _editorIsDirty = false;
            _isEditorOpen = true;

            _suppressEditorTextChanged = true;
            EditorTextBox.Text = content;
            _suppressEditorTextChanged = false;

            EditorDirtyIndicator.Visibility = Visibility.Collapsed;
            EditorTextBox.IsEnabled = true;
            EditorSaveButton.IsEnabled = true;
            EditorTextBox.Focus();
            EditorTextBox.CaretIndex = 0;

            StatusText.Text = $"Editing {Path.GetFileName(filePath)}";
        }
        catch (UnauthorizedAccessException)
        {
            CloseEditor();
            StatusText.Text = "Access denied while opening file.";
        }
        catch (Exception ex)
        {
            CloseEditor();
            MessageBox.Show(
                $"Unable to open file:\n{ex.Message}",
                "Open File",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void EditorSaveButton_Click(object sender, RoutedEventArgs e) =>
        await SaveEditorFileAsync();

    private async void EditorTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.S || Keyboard.Modifiers != ModifierKeys.Control)
            return;

        e.Handled = true;
        await SaveEditorFileAsync();
    }

    private async Task SaveEditorFileAsync()
    {
        if (string.IsNullOrEmpty(_editorFilePath))
            return;

        try
        {
            EditorSaveButton.IsEnabled = false;
            StatusText.Text = "Saving...";

            await File.WriteAllTextAsync(_editorFilePath, EditorTextBox.Text);

            _editorOriginalContent = EditorTextBox.Text;
            _editorIsDirty = false;
            EditorDirtyIndicator.Visibility = Visibility.Collapsed;
            StatusText.Text = $"Saved {Path.GetFileName(_editorFilePath)}";
        }
        catch (UnauthorizedAccessException)
        {
            StatusText.Text = "Access denied while saving file.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to save file:\n{ex.Message}",
                "Save File",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            EditorSaveButton.IsEnabled = true;
        }
    }

    private void EditorCloseButton_Click(object sender, RoutedEventArgs e) =>
        CloseEditor();

    private void EditorTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEditorTextChanged || string.IsNullOrEmpty(_editorFilePath))
            return;

        _editorIsDirty = !string.Equals(EditorTextBox.Text, _editorOriginalContent, StringComparison.Ordinal);
        EditorDirtyIndicator.Visibility = _editorIsDirty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseEditor(bool animate = true)
    {
        if (!_isEditorOpen && EditorOverlay.Visibility != Visibility.Visible)
            return;

        _editorFilePath = null;
        _editorOriginalContent = string.Empty;
        _editorIsDirty = false;
        _isEditorOpen = false;

        _suppressEditorTextChanged = true;
        EditorTextBox.Clear();
        _suppressEditorTextChanged = false;

        EditorDirtyIndicator.Visibility = Visibility.Collapsed;

        if (!animate)
        {
            EditorOverlay.Visibility = Visibility.Collapsed;
            PopChromeDimOverlay();
            return;
        }

        UiAnimationHelper.HideOverlay(EditorOverlay, PopChromeDimOverlay);
    }

    private void PushChromeDimOverlay()
    {
        if (_chromeDimDepth == 0)
        {
            ChromeDimOverlay.Visibility = Visibility.Visible;
            ChromeDimOverlay.IsHitTestVisible = true;
            ChromeDimOverlay.BeginAnimation(UIElement.OpacityProperty, UiAnimationHelper.CreateDimFadeAnimation(0, 1, fadeIn: true));
        }

        _chromeDimDepth++;
    }

    private void PopChromeDimOverlay()
    {
        if (_chromeDimDepth <= 0)
            return;

        _chromeDimDepth--;

        if (_chromeDimDepth > 0)
            return;

        var animation = UiAnimationHelper.CreateDimFadeAnimation(ChromeDimOverlay.Opacity, 0, fadeIn: false);
        animation.Completed += (_, _) =>
        {
            if (_chromeDimDepth > 0)
                return;

            ChromeDimOverlay.Visibility = Visibility.Collapsed;
            ChromeDimOverlay.IsHitTestVisible = false;
            ChromeDimOverlay.Opacity = 0;
        };
        ChromeDimOverlay.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    private void PaneFileList_ContextMenuOpening(DirectoryPaneState pane, ContextMenuEventArgs e) =>
        UpdatePaneFileListContextMenuState(pane);

    private void UpdatePaneFileListContextMenuState(DirectoryPaneState pane)
    {
        SetActivePane(pane);

        if (pane.Control.FileListView.ContextMenu is not ContextMenu menu)
            return;

        var selectedEntries = GetSelectedFileEntries(pane.Control.FileListView);
        var hasSelection = selectedEntries.Count > 0;
        var archiveEntries = selectedEntries
            .Where(entry => !entry.IsDirectory && ArchiveHelper.IsArchiveFile(entry.FullPath))
            .ToList();
        var isArchiveSelection = archiveEntries.Count > 0;

        pane.Control.ExtractArchiveMenuItemControl.Visibility =
            isArchiveSelection ? Visibility.Visible : Visibility.Collapsed;
        pane.Control.ExtractArchiveSeparatorControl.Visibility =
            isArchiveSelection ? Visibility.Visible : Visibility.Collapsed;

        var hasResolvedPath = TryGetGitContextDirectory(pane, out var resolvedDirectory);
        var repositoryRoot = hasResolvedPath
            ? GitRepositoryHelper.GetGitRepositoryRoot(resolvedDirectory)
            : null;
        var isGitRepo = repositoryRoot is not null;

        pane.Control.GitInitMenuItemControl.IsEnabled = hasResolvedPath && !isGitRepo;
        pane.Control.GitCommitMenuItemControl.IsEnabled = isGitRepo && !_isGitCommitInProgress;
        pane.Control.GitAmendMenuItemControl.IsEnabled = isGitRepo && !_isGitCommitInProgress;
        pane.Control.GitHistoryMenuItemControl.IsEnabled = isGitRepo && !_isGitHistoryInProgress;
        pane.Control.AiExecuteQueryMenuItemControl.IsEnabled = hasResolvedPath && !_isAiInProgress;

        foreach (var item in menu.Items)
        {
            if (item is not MenuItem menuItem)
                continue;

            var tag = (string?)menuItem.Tag;
            if (tag is "GitInit" or "GitCommit" or "GitAmend" or "GitHistory" or "AiExecuteQuery")
                continue;

            menuItem.IsEnabled = tag switch
            {
                "Delete" => hasSelection,
                "ExtractArchive" => isArchiveSelection && hasResolvedPath && !_isExtracting,
                "ShowWindowsMenu" => hasSelection || hasResolvedPath,
                _ => hasResolvedPath
            };
        }
    }

    private static bool TryGetGitContextDirectory(DirectoryPaneState pane, out string contextDirectory)
    {
        if (pane.Control.TryResolveGitContextDirectory(out contextDirectory))
            return true;

        if (string.IsNullOrWhiteSpace(pane.CurrentPath))
        {
            contextDirectory = string.Empty;
            return false;
        }

        contextDirectory = Path.GetFullPath(pane.CurrentPath);
        return true;
    }

    private static bool TryResolveGitRepositoryRoot(DirectoryPaneState pane, out string repositoryRoot)
    {
        repositoryRoot = string.Empty;

        if (!TryGetGitContextDirectory(pane, out var contextDirectory))
            return false;

        repositoryRoot = GitRepositoryHelper.GetGitRepositoryRoot(contextDirectory) ?? string.Empty;
        return !string.IsNullOrEmpty(repositoryRoot);
    }

    private static string? ResolveGitWorkingPath(DirectoryPaneState pane)
    {
        if (!TryGetGitContextDirectory(pane, out var contextDirectory))
            return null;

        return GitRepositoryHelper.GetGitRepositoryRoot(contextDirectory) ?? contextDirectory;
    }

    private bool EnsureGitRepositoryOrNotify(DirectoryPaneState pane, out string repositoryRoot)
    {
        var loc = LocalizationManager.Instance;
        repositoryRoot = string.Empty;

        if (!TryResolveGitRepositoryRoot(pane, out repositoryRoot))
        {
            StatusText.Text = loc["UI_GitNotRepositoryPrompt"];
            return false;
        }

        return true;
    }

    private bool EnsureGitRepositoryOrNotify(string? targetPath)
    {
        var loc = LocalizationManager.Instance;

        if (string.IsNullOrWhiteSpace(targetPath))
        {
            StatusText.Text = loc["UI_GitNoDirectory"];
            return false;
        }

        if (GitRepositoryHelper.GetGitRepositoryRoot(targetPath) is null)
        {
            StatusText.Text = loc["UI_GitNotRepositoryPrompt"];
            return false;
        }

        return true;
    }

    private void HandleGitCommitRequest(DirectoryPaneState pane) =>
        BeginGitCommitPromptWorkflow(pane, NamePromptMode.GitCommit, requireChanges: false);

    private void HandleGitAmendRequest(DirectoryPaneState pane) =>
        BeginGitCommitPromptWorkflow(pane, NamePromptMode.GitAmend, requireChanges: true);

    private void BeginGitCommitPromptWorkflow(
        DirectoryPaneState pane,
        NamePromptMode mode,
        bool requireChanges)
    {
        SetActivePane(pane);
        var loc = LocalizationManager.Instance;

        if (!TryResolveGitRepositoryRoot(pane, out var repositoryRoot))
        {
            StatusText.Text = loc["UI_GitNotRepositoryPrompt"];
            return;
        }

        Dispatcher.BeginInvoke(
            () => _ = requireChanges
                ? RunGitCommitPreflightAndPromptAsync(pane, repositoryRoot, mode)
                : ShowGitCommitPromptAsync(pane, repositoryRoot, mode),
            DispatcherPriority.Input);
    }

    private async Task ShowGitCommitPromptAsync(
        DirectoryPaneState pane,
        string repositoryRoot,
        NamePromptMode mode)
    {
        var loc = LocalizationManager.Instance;

        if (!await TryFlushOpenEditorBeforeGitAsync(repositoryRoot))
        {
            StatusText.Text = loc["UI_GitEditorSaveFailed"];
            return;
        }

        var status = await GitService.GetWorkingTreeStatusAsync(repositoryRoot);
        ShowNamePrompt(mode, pane, repositoryRoot, status.Success ? status.ChangedFileCount : null, status.Success ? status.Changes : null);
        StatusText.Text = loc["UI_Ready"];
    }

    private async Task RunGitCommitPreflightAndPromptAsync(
        DirectoryPaneState pane,
        string repositoryRoot,
        NamePromptMode mode)
    {
        var loc = LocalizationManager.Instance;

        StatusText.Text = loc["UI_GitCheckingChanges"];

        if (!await TryFlushOpenEditorBeforeGitAsync(repositoryRoot))
        {
            StatusText.Text = loc["UI_GitEditorSaveFailed"];
            return;
        }

        var status = await GitService.GetWorkingTreeStatusAsync(repositoryRoot).ConfigureAwait(true);

        if (!status.Success)
        {
            StatusText.Text = status.Message;
            MessageBox.Show(status.Message, loc["UI_GitCommitErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (status.IsClean)
        {
            StatusText.Text = loc["UI_GitNoChangesDetected"];
            MessageBox.Show(
                loc["UI_GitNoChangesDetected"],
                loc["UI_GitCommitErrorTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ShowNamePrompt(mode, pane, repositoryRoot, status.ChangedFileCount, status.Changes);
        StatusText.Text = loc["UI_Ready"];
    }

    private async void HandleGitBranchRequest(DirectoryPaneState pane, string targetDirectory)
    {
        var loc = LocalizationManager.Instance;
        if (string.IsNullOrWhiteSpace(targetDirectory)) return;

        var branches = await GitService.GetBranchesAsync(targetDirectory);
        if (branches.Count == 0) return;

        var menu = new ContextMenu();
        
        var titleItem = new MenuItem { Header = loc["UI_GitBranchList"], IsEnabled = false, FontWeight = FontWeights.Bold };
        menu.Items.Add(titleItem);
        menu.Items.Add(new Separator());

        foreach (var branch in branches)
        {
            var item = new MenuItem { Header = branch.Name, IsCheckable = true, IsChecked = branch.IsCurrent };
            item.Click += async (_, _) =>
            {
                if (branch.IsCurrent) return;
                var result = await GitService.SwitchBranchAsync(targetDirectory, branch.Name);
                if (result.Success)
                {
                    StatusText.Text = string.Format(loc["UI_GitBranchSwitched"], branch.Name);
                    RefreshDirectoryListing(pane);
                }
                else
                {
                    MessageBox.Show(result.Message, loc["UI_GitBranchSwitchError"], MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var createItem = new MenuItem { Header = loc["UI_GitCreateBranch"] };
        createItem.Click += (_, _) => ShowNamePrompt(NamePromptMode.GitBranch, pane, targetDirectory);
        menu.Items.Add(createItem);

        menu.PlacementTarget = pane.Control.GitBranchButton;
        menu.Placement = PlacementMode.Top;
        menu.IsOpen = true;
    }

    private void HandleGitHistoryRequest(DirectoryPaneState pane)
    {
        if (!EnsureGitRepositoryOrNotify(pane, out var repositoryRoot))
            return;

        _ = ShowCommitHistoryAsync(pane, repositoryRoot);
    }

    private void HandleAiExecuteQueryRequest(DirectoryPaneState pane)
    {
        SetActivePane(pane);

        if (string.IsNullOrWhiteSpace(pane.CurrentPath) || !Directory.Exists(pane.CurrentPath))
        {
            StatusText.Text = LocalizationManager.Instance["UI_GitNoDirectory"];
            return;
        }

        if (string.IsNullOrWhiteSpace(SettingsManager.Instance.Current.OpenRouterApiKey))
        {
            StatusText.Text = LocalizationManager.Instance["UI_AiOpenRouterApiKey"];
            ShowSettingsOverlay();
            return;
        }

        ShowAiPromptOverlay(pane);
    }

    private void ShowAiPromptOverlay(DirectoryPaneState pane)
    {
        _aiTargetPane = pane;
        _aiPendingCommands = [];

        AiPromptTextBox.Text = string.Empty;
        HideAiPromptError();
        PushChromeDimOverlay();
        AiPromptOverlay.Visibility = Visibility.Visible;
        UiAnimationHelper.ShowOverlay(AiPromptPanel);

        Dispatcher.BeginInvoke(() =>
        {
            AiPromptTextBox.Focus();
            Keyboard.Focus(AiPromptTextBox);
        }, DispatcherPriority.Input);
    }

    private void HideAiPromptOverlay()
    {
        if (AiPromptOverlay.Visibility != Visibility.Visible)
            return;

        AiPromptOverlay.Visibility = Visibility.Collapsed;
        AiPromptTextBox.Clear();
        HideAiPromptError();
        PopChromeDimOverlay();
    }

    private void HideAiWorkflow(bool animate = true)
    {
        HideAiPreviewOverlay(animate);
        HideAiPromptOverlay();
        _aiTargetPane = null;
        _aiPendingCommands = [];
    }

    private void ShowAiPromptError(string message)
    {
        AiPromptErrorText.Text = message;
        AiPromptErrorText.Visibility = Visibility.Visible;
    }

    private void HideAiPromptError()
    {
        AiPromptErrorText.Text = string.Empty;
        AiPromptErrorText.Visibility = Visibility.Collapsed;
    }

    private void AiPromptCancelButton_Click(object sender, RoutedEventArgs e)
    {
        HideAiWorkflow();
        _aiTargetPane = null;
    }

    private void AiPromptSubmitButton_Click(object sender, RoutedEventArgs e) =>
        _ = SubmitAiPromptAsync();

    private void AiPromptTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            _ = SubmitAiPromptAsync();
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideAiWorkflow();
            _aiTargetPane = null;
        }
    }

    private async Task SubmitAiPromptAsync()
    {
        if (_aiTargetPane is null || string.IsNullOrWhiteSpace(_aiTargetPane.CurrentPath))
        {
            ShowAiPromptError(LocalizationManager.Instance["UI_GitNoDirectory"]);
            return;
        }

        var query = AiPromptTextBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            ShowAiPromptError(LocalizationManager.Instance["UI_AiPromptHint"]);
            return;
        }

        var pane = _aiTargetPane;
        var directory = pane.CurrentPath;
        var entries = pane.Control.FileListView.ItemsSource as IReadOnlyList<FileSystemEntry>
            ?? pane.Control.FileListView.Items.Cast<FileSystemEntry>().ToList();

        _isAiInProgress = true;
        AiPromptSubmitButton.IsEnabled = false;
        AiPromptCancelButton.IsEnabled = false;
        HideAiPromptError();
        StatusText.Text = LocalizationManager.Instance["UI_AiThinking"];

        try
        {
            var result = await _aiService.GenerateCommandsAsync(directory, entries, query);

            if (!result.Success)
            {
                ShowAiPromptError(result.ErrorMessage);
                StatusText.Text = result.ErrorMessage;
                return;
            }

            if (result.Commands.Count == 0)
            {
                ShowAiPromptError(LocalizationManager.Instance["UI_AiNoOperations"]);
                StatusText.Text = LocalizationManager.Instance["UI_AiNoOperations"];
                return;
            }

            _aiPendingCommands = result.Commands.ToList();
            HideAiPromptOverlay();
            ShowAiPreviewOverlay(result.Commands);
            StatusText.Text = LocalizationManager.Instance["UI_Ready"];
        }
        catch (Exception ex)
        {
            ShowAiPromptError(ex.Message);
            StatusText.Text = ex.Message;
        }
        finally
        {
            _isAiInProgress = false;
            AiPromptSubmitButton.IsEnabled = true;
            AiPromptCancelButton.IsEnabled = true;
        }
    }

    private void ShowAiPreviewOverlay(IReadOnlyList<AiCommand> commands)
    {
        AiPreviewListBox.ItemsSource = commands.Select(command => command.GetDisplayDescription()).ToList();
        HideAiPreviewError();
        PushChromeDimOverlay();
        AiPreviewOverlay.Visibility = Visibility.Visible;
        UiAnimationHelper.ShowOverlay(AiPreviewPanel);
        AiPreviewConfirmButton.IsEnabled = commands.Count > 0;
    }

    private void HideAiPreviewOverlay(bool animate = true)
    {
        if (AiPreviewOverlay.Visibility != Visibility.Visible)
            return;

        void CompleteHide()
        {
            AiPreviewOverlay.Visibility = Visibility.Collapsed;
            AiPreviewListBox.ItemsSource = null;
            HideAiPreviewError();
            PopChromeDimOverlay();
        }

        if (!animate)
        {
            CompleteHide();
            return;
        }

        UiAnimationHelper.HideOverlay(AiPreviewPanel, CompleteHide);
    }

    private void ShowAiPreviewError(string message)
    {
        AiPreviewErrorText.Text = message;
        AiPreviewErrorText.Visibility = Visibility.Visible;
    }

    private void HideAiPreviewError()
    {
        AiPreviewErrorText.Text = string.Empty;
        AiPreviewErrorText.Visibility = Visibility.Collapsed;
    }

    private void AiPreviewCancelButton_Click(object sender, RoutedEventArgs e)
    {
        HideAiPreviewOverlay();
        _aiPendingCommands = [];
        _aiTargetPane = null;
    }

    private void AiPreviewConfirmButton_Click(object sender, RoutedEventArgs e) =>
        _ = ExecuteAiCommandsAsync();

    private async Task ExecuteAiCommandsAsync()
    {
        if (_aiTargetPane is null || string.IsNullOrWhiteSpace(_aiTargetPane.CurrentPath) || _aiPendingCommands.Count == 0)
            return;

        var pane = _aiTargetPane;
        var directory = pane.CurrentPath;
        var commands = _aiPendingCommands.ToList();
        var loc = LocalizationManager.Instance;

        _isAiInProgress = true;
        AiPreviewConfirmButton.IsEnabled = false;
        AiPreviewCancelButton.IsEnabled = false;
        HideAiPreviewError();
        StatusText.Text = loc["UI_AiExecuting"];

        try
        {
            var result = await Task.Run(() => _aiCommandExecutor.ExecuteAll(directory, commands));

            HideAiPreviewOverlay();
            _aiPendingCommands = [];
            _aiTargetPane = null;

            StatusText.Text = result.Message;

            if (!result.Success)
            {
                MessageBox.Show(
                    result.Message,
                    loc["UI_AiPreviewTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            RefreshAfterFileMove(pane);
        }
        catch (Exception ex)
        {
            ShowAiPreviewError(ex.Message);
            StatusText.Text = ex.Message;
        }
        finally
        {
            _isAiInProgress = false;
            AiPreviewConfirmButton.IsEnabled = true;
            AiPreviewCancelButton.IsEnabled = true;
        }
    }

    private static List<FileSystemEntry> GetSelectedFileEntries(ListView listView) =>
        listView.SelectedItems.Cast<object>().OfType<FileSystemEntry>().ToList();

    private static List<ArchiveEntryItem> GetSelectedArchiveEntries(ListView listView) =>
        listView.SelectedItems.Cast<object>().OfType<ArchiveEntryItem>().ToList();

    private void DeleteSelectedItems(DirectoryPaneState pane)
    {
        var selectedEntries = GetSelectedFileEntries(pane.Control.FileListView);
        if (selectedEntries.Count == 0)
            return;

        ShowDeleteConfirmation(pane, selectedEntries);
    }

    private async Task ExtractSelectedArchivesFromPaneAsync(DirectoryPaneState pane)
    {
        var archiveEntries = GetSelectedFileEntries(pane.Control.FileListView)
            .Where(entry => !entry.IsDirectory && ArchiveHelper.IsArchiveFile(entry.FullPath))
            .ToList();

        foreach (var entry in archiveEntries)
            await ExtractArchiveAsync(pane, entry.FullPath);
    }

    private async void ArchiveExtractButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_openArchivePath))
            return;

        await ExtractArchiveAsync(ActivePane, _openArchivePath);
    }

    private void ArchiveCloseButton_Click(object sender, RoutedEventArgs e) =>
        CloseArchiveViewer();

    private void ArchiveContentsList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (ArchiveContentsList.ContextMenu is not ContextMenu menu)
            return;

        var selectedEntries = GetSelectedArchiveEntries(ArchiveContentsList);
        var hasSelection = selectedEntries.Count > 0;
        var activePath = ActivePane.CurrentPath;
        var canExtract = hasSelection &&
                         !string.IsNullOrEmpty(activePath) &&
                         !string.IsNullOrEmpty(_openArchivePath) &&
                         !_isExtracting;

        foreach (var item in menu.Items)
        {
            if (item is not MenuItem menuItem || (string?)menuItem.Tag != "ExtractSelected")
                continue;

            menuItem.IsEnabled = canExtract;
            menuItem.Header = selectedEntries.Count switch
            {
                0 => "Extract Selected File",
                1 => "Extract Selected File",
                _ => $"Extract {selectedEntries.Count} Selected Items"
            };
        }
    }

    private async void ExtractSelectedArchiveEntry_Click(object sender, RoutedEventArgs e)
    {
        var selectedEntries = GetSelectedArchiveEntries(ArchiveContentsList);
        if (selectedEntries.Count == 0)
            return;

        await ExtractSelectedArchiveEntriesAsync(selectedEntries);
    }

    private async Task ExtractSelectedArchiveEntriesAsync(IReadOnlyList<ArchiveEntryItem> entries)
    {
        var activePath = ActivePane.CurrentPath;
        if (string.IsNullOrEmpty(activePath) ||
            string.IsNullOrEmpty(_openArchivePath) ||
            _isExtracting)
        {
            return;
        }

        SetExtractingUiState(isExtracting: true);
        HideArchiveError();
        StatusText.Text = entries.Count == 1
            ? $"Extracting {entries[0].Name}..."
            : $"Extracting {entries.Count} items...";

        var extractedCount = 0;

        try
        {
            foreach (var entry in entries)
            {
                await _archiveService.ExtractEntryAsync(_openArchivePath, entry.EntryKey, activePath);
                extractedCount++;
            }

            StatusText.Text = extractedCount == 1
                ? $"Extracted \"{entries[0].Name}\"."
                : $"Extracted {extractedCount} item(s).";
            RefreshDirectoryListing(ActivePane);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Extraction cancelled.";
        }
        catch (Exception ex)
        {
            var message = GetArchiveErrorMessage(ex);
            if (_isArchiveViewerOpen)
                ShowArchiveError(message);
            StatusText.Text = message;
        }
        finally
        {
            SetExtractingUiState(isExtracting: false);
            if (_isArchiveViewerOpen)
                ArchiveExtractButton.IsEnabled = true;
        }
    }
    private async Task OpenArchiveViewerAsync(string archivePath)
    {
        if (TextFileHelper.TryGetFileSize(archivePath, out var archiveSize) &&
            archiveSize > ArchiveHelper.MaxInAppPreviewBytes)
        {
            MessageBox.Show(
                $"This archive is {TextFileHelper.FormatByteSize(archiveSize)}. " +
                $"Built-in preview supports archives up to {TextFileHelper.FormatByteSize(ArchiveHelper.MaxInAppPreviewBytes)}.",
                "Archive Too Large",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        CloseEditor(animate: false);
        CloseArchiveViewer(animate: false);

        _openArchivePath = archivePath;
        _isArchiveViewerOpen = true;

        ArchiveFileNameText.Text = Path.GetFileName(archivePath);
        ArchiveContentsList.ItemsSource = null;
        HideArchiveError();
        ArchiveContentsList.Visibility = Visibility.Visible;

        PushChromeDimOverlay();
        UiAnimationHelper.ShowOverlay(ArchiveOverlay);

        ArchiveExtractButton.IsEnabled = false;
        StatusText.Text = $"Loading {Path.GetFileName(archivePath)}...";

        try
        {
            var entries = await Task.Run(() => _archiveService.GetEntries(archivePath));
            ArchiveContentsList.ItemsSource = entries;
            ArchiveExtractButton.IsEnabled = !_isExtracting;
            StatusText.Text = $"{entries.Count} file(s) in {Path.GetFileName(archivePath)}";
        }
        catch (Exception ex)
        {
            ShowArchiveError(GetArchiveErrorMessage(ex));
            ArchiveContentsList.Visibility = Visibility.Collapsed;
            StatusText.Text = "Unable to open archive.";
        }
    }

    private void CloseArchiveViewer(bool animate = true)
    {
        if (!_isArchiveViewerOpen && ArchiveOverlay.Visibility != Visibility.Visible)
            return;

        _openArchivePath = null;
        _isArchiveViewerOpen = false;
        ArchiveContentsList.ItemsSource = null;
        HideArchiveError();

        if (!animate)
        {
            ArchiveOverlay.Visibility = Visibility.Collapsed;
            PopChromeDimOverlay();
            return;
        }

        UiAnimationHelper.HideOverlay(ArchiveOverlay, PopChromeDimOverlay);
    }

    private void ShowArchiveError(string message)
    {
        ArchiveErrorText.Text = message;
        ArchiveErrorText.Visibility = Visibility.Visible;
    }

    private void HideArchiveError()
    {
        ArchiveErrorText.Text = string.Empty;
        ArchiveErrorText.Visibility = Visibility.Collapsed;
    }

    private async Task ExtractArchiveAsync(DirectoryPaneState pane, string archivePath)
    {
        if (string.IsNullOrEmpty(pane.CurrentPath) || _isExtracting)
            return;

        var folderName = ArchiveHelper.GetExtractionFolderName(archivePath);
        var destinationDirectory = Path.Combine(pane.CurrentPath, folderName);

        if (Directory.Exists(destinationDirectory) || File.Exists(destinationDirectory))
        {
            var message = $"A file or folder named \"{folderName}\" already exists.";
            if (_isArchiveViewerOpen)
                ShowArchiveError(message);
            StatusText.Text = message;
            return;
        }

        SetExtractingUiState(isExtracting: true);
        HideArchiveError();

        try
        {
            await _archiveService.ExtractAllAsync(archivePath, destinationDirectory);
            StatusText.Text = $"Extracted to \"{folderName}\".";
            RefreshDirectoryListing(pane);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Extraction cancelled.";
        }
        catch (Exception ex)
        {
            var message = GetArchiveErrorMessage(ex);
            if (_isArchiveViewerOpen)
                ShowArchiveError(message);
            StatusText.Text = message;
        }
        finally
        {
            SetExtractingUiState(isExtracting: false);
            if (_isArchiveViewerOpen)
                ArchiveExtractButton.IsEnabled = true;
        }
    }

    private static string GetArchiveErrorMessage(Exception ex)
    {
        var typeName = ex.GetType().Name;
        if (typeName.Contains("Archive", StringComparison.OrdinalIgnoreCase) ||
            ex is InvalidOperationException or IOException)
        {
            return "The archive appears to be corrupted or cannot be read.";
        }

        return ex.Message;
    }

    private void SetExtractingUiState(bool isExtracting)
    {
        _isExtracting = isExtracting;
        ExtractingIndicator.Visibility = isExtracting ? Visibility.Visible : Visibility.Collapsed;
        Cursor = isExtracting ? Cursors.Wait : Cursors.Arrow;
        ArchiveExtractButton.IsEnabled = !isExtracting && _isArchiveViewerOpen;

        foreach (var pane in _panes)
            pane.Control.PathSearchTextBox.IsEnabled = !isExtracting;

        if (!isExtracting)
            UpdateNavigationButtons(ActivePane);
        else
        {
            BackNavigationButton.IsEnabled = false;
            ForwardNavigationButton.IsEnabled = false;
        }
    }

    private void ShowDeleteConfirmation(DirectoryPaneState pane, IReadOnlyList<FileSystemEntry> entries)
    {
        _deleteTargetPane = pane;
        _pendingDeleteEntries = entries.ToList();
        DeleteConfirmMessageText.Text = entries.Count switch
        {
            0 => string.Empty,
            1 => $"Are you sure you want to delete \"{entries[0].Name}\"? This cannot be undone.",
            _ => $"Are you sure you want to delete these {entries.Count} items? This cannot be undone."
        };
        HideDeleteConfirmError();
        PushChromeDimOverlay();
        DeleteConfirmOverlay.Visibility = Visibility.Visible;
        UiAnimationHelper.ShowOverlay(DeleteConfirmPanel);
    }

    private void HideDeleteConfirmation(bool animate = true)
    {
        if (DeleteConfirmOverlay.Visibility != Visibility.Visible)
        {
            _pendingDeleteEntries = [];
            _deleteTargetPane = null;
            HideDeleteConfirmError();
            return;
        }

        void CompleteHide()
        {
            DeleteConfirmOverlay.Visibility = Visibility.Collapsed;
            _pendingDeleteEntries = [];
            _deleteTargetPane = null;
            HideDeleteConfirmError();
            PopChromeDimOverlay();
        }

        if (!animate)
        {
            DeleteConfirmPanel.Visibility = Visibility.Collapsed;
            CompleteHide();
            return;
        }

        UiAnimationHelper.HideOverlay(DeleteConfirmPanel, CompleteHide);
    }

    private void ShowDeleteConfirmError(string message)
    {
        DeleteConfirmErrorText.Text = message;
        DeleteConfirmErrorText.Visibility = Visibility.Visible;
    }

    private void HideDeleteConfirmError()
    {
        DeleteConfirmErrorText.Text = string.Empty;
        DeleteConfirmErrorText.Visibility = Visibility.Collapsed;
    }

    private void DeleteConfirmCancelButton_Click(object sender, RoutedEventArgs e) =>
        HideDeleteConfirmation();

    private void DeleteConfirmDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingDeleteEntries.Count == 0 || _deleteTargetPane is null)
            return;

        var entries = _pendingDeleteEntries.ToList();
        var pane = _deleteTargetPane;
        var deletedCount = 0;
        var lastDeletedName = string.Empty;

        try
        {
            foreach (var entry in entries)
            {
                _recycleBinService.SendToRecycleBin(entry);
                deletedCount++;
                lastDeletedName = entry.Name;

                if (_isEditorOpen &&
                    !string.IsNullOrEmpty(_editorFilePath) &&
                    string.Equals(_editorFilePath, entry.FullPath, StringComparison.OrdinalIgnoreCase))
                {
                    CloseEditor();
                }
            }

            HideDeleteConfirmation();
            RefreshDirectoryListing(pane);
            StatusText.Text = deletedCount == 1
                ? $"Moved \"{lastDeletedName}\" to the Recycle Bin."
                : $"Moved {deletedCount} items to the Recycle Bin.";
        }
        catch (UnauthorizedAccessException)
        {
            var message = "Permission denied. Unable to delete this item.";
            ShowDeleteConfirmError(message);
            StatusText.Text = message;
        }
        catch (IOException ex)
        {
            var message = ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
                ? "This item is in use and cannot be deleted right now."
                : ex.Message;
            ShowDeleteConfirmError(message);
            StatusText.Text = message;
        }
        catch (Exception ex)
        {
            ShowDeleteConfirmError(ex.Message);
            StatusText.Text = ex.Message;
        }
    }

    private void ShowNamePrompt(
        NamePromptMode mode,
        DirectoryPaneState pane,
        string? gitTargetDirectory = null,
        int? changedFileCount = null,
        List<GitFileStatus>? gitChanges = null)
    {
        var loc = LocalizationManager.Instance;

        if (mode != NamePromptMode.GitCommit && mode != NamePromptMode.GitAmend && mode != NamePromptMode.GitBranch && string.IsNullOrEmpty(pane.CurrentPath))
        {
            StatusText.Text = "Select a folder before creating items.";
            return;
        }

        if ((mode == NamePromptMode.GitCommit || mode == NamePromptMode.GitAmend || mode == NamePromptMode.GitBranch) &&
            string.IsNullOrWhiteSpace(gitTargetDirectory))
        {
            StatusText.Text = loc["UI_GitNoDirectory"];
            return;
        }

        _namePromptTargetPane = pane;
        _namePromptMode = mode;
        _namePromptGitTargetDirectory = gitTargetDirectory is null ? null : Path.GetFullPath(gitTargetDirectory);

        GitCommitFilesList.Visibility = Visibility.Collapsed;
        GitCommitFilesList.ItemsSource = null;

        switch (mode)
        {
            case NamePromptMode.NewFolder:
                NamePromptTitleText.Text = loc["UI_NewFolder"];
                NamePromptTextBox.Text = "New Folder";
                NamePromptCreateButton.Content = loc["UI_Create"];
                break;
            case NamePromptMode.NewTextFile:
                NamePromptTitleText.Text = loc["UI_NewTextFile"];
                NamePromptTextBox.Text = "New Text Document.txt";
                NamePromptCreateButton.Content = loc["UI_Create"];
                break;
            case NamePromptMode.GitCommit:
                NamePromptTitleText.Text = changedFileCount is int commitCount
                    ? string.Format(loc["UI_GitCommitMessageWithCount"], commitCount)
                    : loc["UI_GitCommitMessage"];
                NamePromptTextBox.Text = string.Empty;
                NamePromptCreateButton.Content = loc["UI_Commit"];
                if (gitChanges is not null && gitChanges.Count > 0)
                {
                    GitCommitFilesList.ItemsSource = gitChanges;
                    GitCommitFilesList.Visibility = Visibility.Visible;
                }
                break;
            case NamePromptMode.GitAmend:
                NamePromptTitleText.Text = changedFileCount is int amendCount
                    ? string.Format(loc["UI_GitAmendMessageWithCount"], amendCount)
                    : loc["UI_GitAmendMessage"];
                NamePromptTextBox.Text = string.Empty;
                NamePromptCreateButton.Content = loc["UI_GitAmendConfirm"];
                if (gitChanges is not null && gitChanges.Count > 0)
                {
                    GitCommitFilesList.ItemsSource = gitChanges;
                    GitCommitFilesList.Visibility = Visibility.Visible;
                }
                break;
            case NamePromptMode.GitBranch:
                NamePromptTitleText.Text = loc["UI_GitCreateBranch"];
                NamePromptTextBox.Text = string.Empty;
                NamePromptCreateButton.Content = loc["UI_Create"];
                break;
        }

        HideNamePromptError();
        PushChromeDimOverlay();
        NamePromptOverlay.Visibility = Visibility.Visible;

        Dispatcher.BeginInvoke(() =>
        {
            NamePromptTextBox.Focus();
            Keyboard.Focus(NamePromptTextBox);
            NamePromptTextBox.CaretIndex = NamePromptTextBox.Text.Length;
        }, DispatcherPriority.Input);
    }

    private void HideNamePrompt()
    {
        if (NamePromptOverlay.Visibility != Visibility.Visible)
            return;

        NamePromptOverlay.Visibility = Visibility.Collapsed;
        NamePromptTextBox.Clear();
        GitCommitFilesList.ItemsSource = null;
        GitCommitFilesList.Visibility = Visibility.Collapsed;
        _namePromptTargetPane = null;
        _namePromptGitTargetDirectory = null;
        HideNamePromptError();
        PopChromeDimOverlay();
    }

    private void ShowNamePromptError(string message)
    {
        NamePromptErrorText.Text = message;
        NamePromptErrorText.Visibility = Visibility.Visible;
    }

    private void HideNamePromptError()
    {
        NamePromptErrorText.Text = string.Empty;
        NamePromptErrorText.Visibility = Visibility.Collapsed;
    }

    private void NamePromptCancelButton_Click(object sender, RoutedEventArgs e) =>
        HideNamePrompt();

    private void NamePromptCreateButton_Click(object sender, RoutedEventArgs e) =>
        TryCreateFromNamePrompt();

    private void NamePromptTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            TryCreateFromNamePrompt();
            return;
        }

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideNamePrompt();
        }
    }

    private async Task TryGitCreateBranchFromPromptAsync()
    {
        var loc = LocalizationManager.Instance;
        var branchName = NamePromptTextBox.Text.Trim();
        var pane = _namePromptTargetPane;
        var targetDirectory = _namePromptGitTargetDirectory;

        if (string.IsNullOrWhiteSpace(branchName))
        {
            ShowNamePromptError(loc["UI_GitNewBranchName"]);
            return;
        }

        if (pane is null || string.IsNullOrWhiteSpace(targetDirectory))
        {
            ShowNamePromptError(loc["UI_GitNoDirectory"]);
            return;
        }

        var result = await GitService.CreateBranchAsync(targetDirectory, branchName);
        if (result.Success)
        {
            HideNamePrompt();
            StatusText.Text = string.Format(loc["UI_GitBranchCreated"], branchName);
            RefreshDirectoryListing(pane);
        }
        else
        {
            ShowNamePromptError(result.Message);
        }
    }

    private void TryCreateFromNamePrompt()
    {
        if (_namePromptMode == NamePromptMode.GitCommit)
        {
            _ = TryGitCommitFromPromptAsync();
            return;
        }

        if (_namePromptMode == NamePromptMode.GitAmend)
        {
            _ = TryGitAmendFromPromptAsync();
            return;
        }

        if (_namePromptMode == NamePromptMode.GitBranch)
        {
            _ = TryGitCreateBranchFromPromptAsync();
            return;
        }

        if (_namePromptTargetPane is null || string.IsNullOrEmpty(_namePromptTargetPane.CurrentPath))
        {
            ShowNamePromptError("No folder is selected.");
            return;
        }

        var pane = _namePromptTargetPane;

        if (!TryNormalizeNewItemName(NamePromptTextBox.Text, _namePromptMode, out var itemName, out var error))
        {
            ShowNamePromptError(error);
            StatusText.Text = error;
            return;
        }

        try
        {
            var targetPath = Path.Combine(pane.CurrentPath, itemName);

            if (Directory.Exists(targetPath) || File.Exists(targetPath))
            {
                var message = "An item with that name already exists.";
                ShowNamePromptError(message);
                StatusText.Text = message;
                return;
            }

            switch (_namePromptMode)
            {
                case NamePromptMode.NewFolder:
                    Directory.CreateDirectory(targetPath);
                    StatusText.Text = $"Created folder \"{itemName}\".";
                    break;
                case NamePromptMode.NewTextFile:
                    File.WriteAllText(targetPath, string.Empty);
                    StatusText.Text = $"Created file \"{itemName}\".";
                    break;
            }

            HideNamePrompt();
            RefreshDirectoryListing(pane);
        }
        catch (UnauthorizedAccessException)
        {
            var message = "Permission denied. Unable to create the item.";
            ShowNamePromptError(message);
            StatusText.Text = message;
        }
        catch (IOException ex)
        {
            var message = ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                ? "An item with that name already exists."
                : ex.Message;
            ShowNamePromptError(message);
            StatusText.Text = message;
        }
        catch (Exception ex)
        {
            ShowNamePromptError(ex.Message);
            StatusText.Text = ex.Message;
        }
    }

    private async void HandleGitInit(DirectoryPaneState pane, string targetDirectory)
    {
        var loc = LocalizationManager.Instance;

        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            StatusText.Text = loc["UI_GitNoDirectory"];
            MessageBox.Show(loc["UI_GitNoDirectory"], loc["UI_GitInitErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string workingPath;
        try
        {
            workingPath = Path.GetFullPath(targetDirectory);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(ex.Message, loc["UI_GitInitErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!Directory.Exists(workingPath))
        {
            const string message = "Target directory does not exist.";
            StatusText.Text = message;
            MessageBox.Show(message, loc["UI_GitInitErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusText.Text = loc["UI_GitInitializing"];

        try
        {
            var result = await GitService.InitializeRepositoryAsync(workingPath).ConfigureAwait(true);

            if (result.Success && GitRepositoryHelper.GetGitRepositoryRoot(workingPath) is not null)
            {
                var successMessage = loc["UI_GitInitSuccess"];
                StatusText.Text = successMessage;
                RefreshDirectoryListing(pane);
                UpdatePaneFileListContextMenuState(pane);
                MessageBox.Show(successMessage, loc["UI_GitInitSuccessTitle"], MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var errorMessage = result.Success
                ? loc["UI_GitInitMissingMetadata"]
                : result.Message;

            StatusText.Text = errorMessage;
            MessageBox.Show(errorMessage, loc["UI_GitInitErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(ex.ToString(), loc["UI_GitInitErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task TryGitCommitFromPromptAsync()
    {
        await ExecuteGitCommitWorkflowAsync(
            emptyMessageKey: "UI_GitCommitMessageRequired",
            statusInProgressKey: "UI_GitCommitting",
            executeAsync: GitService.CommitAllAsync);
    }

    private async Task TryGitAmendFromPromptAsync()
    {
        await ExecuteGitCommitWorkflowAsync(
            emptyMessageKey: "UI_GitCommitMessageRequired",
            statusInProgressKey: "UI_GitAmending",
            executeAsync: GitService.AmendLastCommitAsync);
    }

    private async Task ExecuteGitCommitWorkflowAsync(
        string emptyMessageKey,
        string statusInProgressKey,
        Func<string, string, Task<GitResult>> executeAsync)
    {
        var loc = LocalizationManager.Instance;
        var commitMessage = NamePromptTextBox.Text.Trim();
        var pane = _namePromptTargetPane;
        var targetDirectory = _namePromptGitTargetDirectory;

        if (string.IsNullOrWhiteSpace(commitMessage))
        {
            ShowNamePromptError(loc[emptyMessageKey]);
            return;
        }

        if (pane is null || string.IsNullOrWhiteSpace(targetDirectory))
        {
            ShowNamePromptError(loc["UI_GitNoDirectory"]);
            return;
        }

        targetDirectory = Path.GetFullPath(targetDirectory);

        HideNamePrompt();

        _isGitCommitInProgress = true;
        StatusText.Text = loc[statusInProgressKey];

        try
        {
            if (!await TryFlushOpenEditorBeforeGitAsync(targetDirectory))
            {
                StatusText.Text = loc["UI_GitEditorSaveFailed"];
                return;
            }

            var result = await executeAsync(targetDirectory, commitMessage).ConfigureAwait(true);
            await HandleGitCommitResultAsync(pane, result);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            MessageBox.Show(ex.ToString(), loc["UI_GitCommitErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isGitCommitInProgress = false;
        }
    }

    private async Task HandleGitCommitResultAsync(DirectoryPaneState pane, GitResult result)
    {
        var loc = LocalizationManager.Instance;

        if (result.NoChangesToCommit)
        {
            StatusText.Text = loc["UI_GitNothingToCommit"];
            return;
        }

        if (result.NothingToAmend)
        {
            StatusText.Text = loc["UI_GitNothingToAmend"];
            return;
        }

        StatusText.Text = result.Message;

        if (!result.Success)
        {
            MessageBox.Show(result.Message, loc["UI_GitCommitErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        RefreshDirectoryListing(pane);
        await RefreshCommitHistoryIfVisibleAsync();
    }

    private async Task<bool> TryFlushOpenEditorBeforeGitAsync(string repositoryPath)
    {
        if (!_isEditorOpen || string.IsNullOrEmpty(_editorFilePath) || !_editorIsDirty)
            return true;

        if (!IsPathUnderDirectory(_editorFilePath, repositoryPath))
            return true;

        await SaveEditorFileAsync();
        return !_editorIsDirty;
    }

    private static bool IsPathUnderDirectory(string filePath, string directoryPath)
    {
        var fullFile = Path.GetFullPath(filePath);
        var fullDirectory = Path.GetFullPath(directoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var directoryPrefix = fullDirectory + Path.DirectorySeparatorChar;
        return fullFile.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RefreshCommitHistoryIfVisibleAsync()
    {
        if (CommitHistoryOverlay.Visibility != Visibility.Visible)
            return;

        await RefreshCommitHistoryListAsync();
    }

    private async Task ShowCommitHistoryAsync(DirectoryPaneState pane, string repositoryRoot)
    {
        var loc = LocalizationManager.Instance;

        if (string.IsNullOrWhiteSpace(repositoryRoot))
        {
            StatusText.Text = loc["UI_GitNoDirectory"];
            return;
        }

        repositoryRoot = Path.GetFullPath(repositoryRoot);

        _commitHistoryTargetPane = pane;
        _commitHistoryTargetDirectory = repositoryRoot;
        CommitHistoryListBox.ItemsSource = null;
        ResetCommitHistoryActionState();
        HideCommitHistoryError();

        _isGitHistoryInProgress = true;
        StatusText.Text = loc["UI_GitLoadingHistory"];

        try
        {
            var historyResult = await GitService.GetCommitHistoryAsync(repositoryRoot);
            if (!historyResult.Success)
            {
                StatusText.Text = historyResult.Message;
                return;
            }

            await BindCommitHistoryListAsync(historyResult.Commits);
            PushChromeDimOverlay();
            CommitHistoryOverlay.Visibility = Visibility.Visible;
            UiAnimationHelper.ShowOverlay(CommitHistoryPanel);
            StatusText.Text = loc["UI_Ready"];
        }
        finally
        {
            _isGitHistoryInProgress = false;
        }
    }

    private void HideCommitHistoryOverlay(bool animate = true)
    {
        if (CommitHistoryOverlay.Visibility != Visibility.Visible)
            return;

        void CompleteHide()
        {
            CommitHistoryOverlay.Visibility = Visibility.Collapsed;
            CommitHistoryPanel.Visibility = Visibility.Visible;
            CommitHistoryListBox.ItemsSource = null;
            _commitHistoryTargetPane = null;
            _commitHistoryTargetDirectory = null;
            ResetCommitHistoryActionState();
            HideCommitDeleteConfirmOverlay();
            HideCommitHistoryError();
            PopChromeDimOverlay();
        }

        if (!animate)
        {
            CompleteHide();
            return;
        }

        CommitHistoryPanel.Visibility = Visibility.Visible;
        UiAnimationHelper.HideOverlay(CommitHistoryPanel, CompleteHide);
    }

    private void ResetCommitHistoryActionState()
    {
        _commitHistoryRestorePending = false;
        CommitHistoryWarningText.Visibility = Visibility.Collapsed;
        CommitHistoryRestoreButton.Visibility = Visibility.Visible;
        CommitHistoryConfirmRestoreButton.Visibility = Visibility.Collapsed;

        var hasSelection = CommitHistoryListBox.SelectedItem is GitCommit;
        CommitHistoryRestoreButton.IsEnabled = hasSelection;
        CommitHistoryDeleteCommitButton.IsEnabled = hasSelection;
    }

    private async Task BindCommitHistoryListAsync(IReadOnlyList<GitCommit> commits)
    {
        CommitHistoryListBox.ItemsSource = commits;
        CommitHistoryListBox.SelectedIndex = commits.Count > 0 ? 0 : -1;
        ResetCommitHistoryActionState();
        await Task.CompletedTask;
    }

    private async Task RefreshCommitHistoryListAsync()
    {
        var targetDirectory = _commitHistoryTargetDirectory;
        if (string.IsNullOrWhiteSpace(targetDirectory))
            return;

        targetDirectory = Path.GetFullPath(targetDirectory);
        _commitHistoryTargetDirectory = targetDirectory;

        CommitHistoryListBox.ItemsSource = null;

        var historyResult = await GitService.GetCommitHistoryAsync(targetDirectory);
        if (!historyResult.Success)
        {
            ShowCommitHistoryError(historyResult.Message);
            StatusText.Text = historyResult.Message;
            return;
        }

        await BindCommitHistoryListAsync(historyResult.Commits);
    }

    private void ShowCommitDeleteConfirmOverlay()
    {
        if (CommitHistoryListBox.SelectedItem is not GitCommit)
            return;

        HideCommitHistoryError();
        CommitDeleteConfirmOverlay.Visibility = Visibility.Visible;
    }

    private void HideCommitDeleteConfirmOverlay()
    {
        CommitDeleteConfirmOverlay.Visibility = Visibility.Collapsed;
        CommitDeleteConfirmYesButton.IsEnabled = true;
        CommitDeleteConfirmCancelButton.IsEnabled = true;
    }

    private void SetCommitHistoryActionsEnabled(bool isEnabled)
    {
        CommitHistoryCancelButton.IsEnabled = isEnabled;
        CommitHistoryDeleteCommitButton.IsEnabled = isEnabled && CommitHistoryListBox.SelectedItem is GitCommit;
        CommitHistoryRestoreButton.IsEnabled = isEnabled && CommitHistoryListBox.SelectedItem is GitCommit;
        CommitHistoryConfirmRestoreButton.IsEnabled = isEnabled;
    }

    private void ShowCommitHistoryError(string message)
    {
        CommitHistoryErrorText.Text = message;
        CommitHistoryErrorText.Visibility = Visibility.Visible;
    }

    private void HideCommitHistoryError()
    {
        CommitHistoryErrorText.Text = string.Empty;
        CommitHistoryErrorText.Visibility = Visibility.Collapsed;
    }

    private void CommitHistoryListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_commitHistoryRestorePending)
            return;

        var hasSelection = CommitHistoryListBox.SelectedItem is GitCommit;
        CommitHistoryRestoreButton.IsEnabled = hasSelection;
        CommitHistoryDeleteCommitButton.IsEnabled = hasSelection;
    }

    private void CommitHistoryCancelButton_Click(object sender, RoutedEventArgs e)
    {
        HideCommitDeleteConfirmOverlay();
        HideCommitHistoryOverlay();
    }

    private void CommitHistoryDeleteCommitButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommitHistoryListBox.SelectedItem is not GitCommit)
            return;

        ShowCommitDeleteConfirmOverlay();
    }

    private void CommitDeleteConfirmCancelButton_Click(object sender, RoutedEventArgs e) =>
        HideCommitDeleteConfirmOverlay();

    private async void CommitDeleteConfirmYesButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommitHistoryListBox.SelectedItem is not GitCommit selectedCommit ||
            _commitHistoryTargetPane is null ||
            string.IsNullOrWhiteSpace(_commitHistoryTargetDirectory))
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        var pane = _commitHistoryTargetPane;
        var targetDirectory = pane?.CurrentPath ?? _commitHistoryTargetDirectory;

        if (string.IsNullOrWhiteSpace(targetDirectory))
            return;

        targetDirectory = Path.GetFullPath(targetDirectory);

        HideCommitDeleteConfirmOverlay();
        SetCommitHistoryActionsEnabled(false);
        StatusText.Text = loc["UI_GitDeletingCommit"];

        try
        {
            var result = await Task.Run(async () =>
                await GitService.DeleteCommitAsync(targetDirectory, selectedCommit.Hash));

            if (result.Success)
            {
                StatusText.Text = loc["UI_GitDeleteCommitSuccess"];
                await RefreshCommitHistoryListAsync();
                if (pane is not null)
                    RefreshDirectoryListing(pane);
                return;
            }

            var message = result.RebaseConflictAborted
                ? loc["UI_GitDeleteCommitConflict"]
                : result.Message;

            ShowCommitHistoryError(message);
            StatusText.Text = message;
        }
        catch (Exception ex)
        {
            ShowCommitHistoryError(ex.Message);
            StatusText.Text = ex.Message;
        }
        finally
        {
            SetCommitHistoryActionsEnabled(true);
            ResetCommitHistoryActionState();
        }
    }

    private void CommitHistoryRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommitHistoryListBox.SelectedItem is not GitCommit)
            return;

        _commitHistoryRestorePending = true;
        CommitHistoryWarningText.Visibility = Visibility.Visible;
        CommitHistoryRestoreButton.Visibility = Visibility.Collapsed;
        CommitHistoryConfirmRestoreButton.Visibility = Visibility.Visible;
        CommitHistoryDeleteCommitButton.IsEnabled = false;
        HideCommitHistoryError();
    }

    private async void CommitHistoryConfirmRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommitHistoryListBox.SelectedItem is not GitCommit selectedCommit ||
            _commitHistoryTargetPane is null ||
            string.IsNullOrWhiteSpace(_commitHistoryTargetDirectory))
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        var pane = _commitHistoryTargetPane;
        var targetDirectory = Path.GetFullPath(_commitHistoryTargetDirectory);
        var commitHash = selectedCommit.Hash.Trim();

        if (string.IsNullOrWhiteSpace(commitHash))
            return;

        CommitHistoryConfirmRestoreButton.IsEnabled = false;
        CommitHistoryCancelButton.IsEnabled = false;
        CommitHistoryDeleteCommitButton.IsEnabled = false;
        StatusText.Text = loc["UI_GitRestoring"];

        try
        {
            var result = await GitService.RestoreToCommitAsync(targetDirectory, commitHash);

            if (!result.Success)
            {
                ShowCommitHistoryError(result.Message);
                StatusText.Text = result.Message;
                MessageBox.Show(result.Message, loc["UI_GitRestoreErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Warning);
                CommitHistoryConfirmRestoreButton.IsEnabled = true;
                CommitHistoryCancelButton.IsEnabled = true;
                return;
            }

            HideCommitHistoryOverlay();
            CloseEditor();
            StatusText.Text = result.Message;
            NavigateToDirectory(pane, targetDirectory, syncTree: pane == _activePane, recordHistory: false);
        }
        catch (Exception ex)
        {
            ShowCommitHistoryError(ex.Message);
            StatusText.Text = ex.Message;
            MessageBox.Show(ex.ToString(), loc["UI_GitRestoreErrorTitle"], MessageBoxButton.OK, MessageBoxImage.Error);
            CommitHistoryConfirmRestoreButton.IsEnabled = true;
            CommitHistoryCancelButton.IsEnabled = true;
        }
    }

    private static bool TryNormalizeNewItemName(
        string rawName,
        NamePromptMode mode,
        out string normalizedName,
        out string error)
    {
        normalizedName = string.Empty;
        error = string.Empty;

        var trimmed = rawName.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            error = "Name cannot be empty.";
            return false;
        }

        if (trimmed is "." or "..")
        {
            error = "This name is not allowed.";
            return false;
        }

        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "The name contains invalid characters.";
            return false;
        }

        if (mode == NamePromptMode.NewTextFile &&
            !trimmed.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            trimmed += ".txt";
        }

        normalizedName = trimmed;
        return true;
    }

    private void SyncTreeToPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var startingNode = FindRootTreeNodeForPath(fullPath);

        if (startingNode is null)
            return;

        _suppressTreeSelectionChange = true;

        try
        {
            ClearTreeViewSelection();

            var currentNode = startingNode;
            ExpandTreeNode(currentNode);
            SelectTreeNode(currentNode);

            var rootPath = startingNode.FullPath.TrimEnd('\\');
            var relativePath = fullPath.Length > rootPath.Length
                ? fullPath[(rootPath.Length + 1)..]
                : string.Empty;

            if (string.IsNullOrEmpty(relativePath))
                return;

            foreach (var segment in relativePath.Split('\\', StringSplitOptions.RemoveEmptyEntries))
            {
                _fileSystemService.LoadChildDirectories(currentNode);
                DirectoryTree.UpdateLayout();

                var childNode = currentNode.Children.FirstOrDefault(child =>
                    string.Equals(child.Name, segment, StringComparison.OrdinalIgnoreCase));

                if (childNode is null)
                    break;

                currentNode = childNode;
                ExpandTreeNode(currentNode);
                SelectTreeNode(currentNode);
            }
        }
        finally
        {
            _suppressTreeSelectionChange = false;
        }
    }

    private DirectoryTreeNode? FindRootTreeNodeForPath(string fullPath)
    {
        var desktopNode = _driveNodes.FirstOrDefault(node =>
            string.Equals(node.Name, "Desktop", StringComparison.OrdinalIgnoreCase));

        if (desktopNode is not null && IsPathWithinRoot(fullPath, desktopNode.FullPath))
            return desktopNode;

        var driveRoot = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(driveRoot))
            return null;

        return _driveNodes.FirstOrDefault(node => PathsEqual(node.FullPath, driveRoot));
    }

    private static bool IsPathWithinRoot(string fullPath, string rootPath)
    {
        if (PathsEqual(fullPath, rootPath))
            return true;

        var normalizedRoot = rootPath.TrimEnd('\\');
        return fullPath.StartsWith(normalizedRoot + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private void ExpandTreeNode(DirectoryTreeNode node)
    {
        var container = FindTreeViewItem(DirectoryTree, node);
        if (container is null)
        {
            DirectoryTree.UpdateLayout();
            container = FindTreeViewItem(DirectoryTree, node);
        }

        if (container is null)
            return;

        container.IsExpanded = true;
        container.UpdateLayout();
    }

    private void SelectTreeNode(DirectoryTreeNode node)
    {
        ClearTreeViewSelection();

        var container = FindTreeViewItem(DirectoryTree, node);
        if (container is null)
        {
            DirectoryTree.UpdateLayout();
            container = FindTreeViewItem(DirectoryTree, node);
        }

        if (container is null)
            return;

        container.IsSelected = true;
        container.BringIntoView();
    }

    private void ClearTreeViewSelection()
    {
        ClearTreeViewItemSelection(DirectoryTree);
    }

    private static void ClearTreeViewItemSelection(ItemsControl parent)
    {
        for (var i = 0; i < parent.Items.Count; i++)
        {
            if (parent.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem item)
                continue;

            item.IsSelected = false;
            ClearTreeViewItemSelection(item);
        }
    }

    private static TreeViewItem? FindTreeViewItem(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem directContainer)
            return directContainer;

        foreach (var childItem in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(childItem) is not TreeViewItem childContainer)
                continue;

            var match = FindTreeViewItem(childContainer, item);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd('\\'),
            Path.GetFullPath(right).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

    private static void OpenFileWithDefaultApplication(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Unable to open file:\n{ex.Message}",
                "Open File",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnSettingChanged(object? sender, string propertyName)
    {
        switch (propertyName)
        {
            case nameof(AppSettings.Theme):
                ThemeManager.ApplyTheme(SettingsManager.Instance.Current.Theme);
                foreach (var pane in _panes)
                {
                    pane.Control.ResetThemeBrushes();
                    pane.Control.SetIsActive(pane == _activePane);
                }
                break;
            case nameof(AppSettings.TimeFormat):
                RefreshAllPaneDateDisplays();
                break;
            case nameof(AppSettings.Language):
                LocalizationManager.Instance.LoadLanguage(SettingsManager.Instance.Current.Language);
                ApplyLocalizedStrings();
                PopulateSettingsControls();
                break;
        }
    }

    private void ApplyLocalizedStrings()
    {
        var loc = LocalizationManager.Instance;
        Title = loc["UI_AppTitle"];
        TerminalToggleButton.ToolTip = loc["UI_TerminalToggle"];
        SettingsButton.ToolTip = loc["UI_Settings"];
        AddPaneButton.ToolTip = loc["UI_AddPane"];
        BackNavigationButton.ToolTip = loc["UI_Back"];
        ForwardNavigationButton.ToolTip = loc["UI_Forward"];
        SearchingIndicator.Text = loc["UI_Searching"];
        ExtractingIndicator.Text = loc["UI_Extracting"];
        ArchiveFileNameColumn.Header = loc["UI_ColumnFileName"];
        ArchiveOriginalSizeColumn.Header = loc["UI_ColumnOriginalSize"];
        ArchiveCompressedSizeColumn.Header = loc["UI_ColumnCompressedSize"];

        foreach (var pane in _panes)
            pane.Control.ApplyLocalization();
    }

    private void PopulateSettingsControls()
    {
        _suppressSettingsUiChange = true;
        _suppressAiApiKeyChange = true;
        try
        {
            var loc = LocalizationManager.Instance;
            SettingsThemeComboBox.Items.Clear();
            SettingsThemeComboBox.Items.Add(loc["UI_ThemeDark"]);
            SettingsThemeComboBox.Items.Add(loc["UI_ThemeLight"]);
            SettingsThemeComboBox.SelectedIndex =
                SettingsManager.Instance.Current.Theme == AppTheme.Light ? 1 : 0;

            SettingsTimeFormatCheckBox.IsChecked =
                SettingsManager.Instance.Current.TimeFormat == TimeFormatMode.Hour12;

            var locales = LocalizationManager.Instance.GetAvailableLocales();
            SettingsLanguageComboBox.ItemsSource = locales;
            SettingsLanguageComboBox.SelectedItem = locales.FirstOrDefault(locale =>
                locale.Code.Equals(
                    SettingsManager.Instance.Current.Language,
                    StringComparison.OrdinalIgnoreCase)) ?? locales.FirstOrDefault();

            SettingsAiApiKeyBox.Text = SettingsManager.Instance.Current.OpenRouterApiKey;
            SettingsAiModelTextBox.Text = SettingsManager.Instance.Current.PreferredAiModel;
        }
        finally
        {
            _suppressSettingsUiChange = false;
            _suppressAiApiKeyChange = false;
        }
    }

    private void RefreshAllPaneDateDisplays()
    {
        foreach (var pane in _panes)
            pane.Control.RefreshDateColumnDisplay();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) =>
        ShowSettingsOverlay();

    private void ShowSettingsOverlay()
    {
        PopulateSettingsControls();
        SettingsOverlay.Visibility = Visibility.Visible;
        UiAnimationHelper.ShowOverlay(SettingsPanel);
    }

    private void HideSettingsOverlay()
    {
        if (SettingsOverlay.Visibility != Visibility.Visible)
            return;

        void FinishHide()
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
            SettingsPanel.Visibility = Visibility.Visible;
            SettingsPanel.ClearValue(UIElement.OpacityProperty);
            SettingsPanel.RenderTransform = Transform.Identity;
        }

        if (SettingsPanel.Visibility != Visibility.Visible)
        {
            FinishHide();
            return;
        }

        UiAnimationHelper.HideOverlay(SettingsPanel, FinishHide);
    }

    private void SettingsOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            IsDescendantOf(source, SettingsPanel))
        {
            return;
        }

        HideSettingsOverlay();
    }

    private static bool IsDescendantOf(DependencyObject? source, DependencyObject ancestor)
    {
        while (source is not null)
        {
            if (ReferenceEquals(source, ancestor))
                return true;

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void SettingsCloseButton_Click(object sender, RoutedEventArgs e) =>
        HideSettingsOverlay();

    private void SettingsPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        e.Handled = true;

    private void SettingsThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsUiChange || SettingsThemeComboBox.SelectedIndex < 0)
            return;

        var theme = SettingsThemeComboBox.SelectedIndex == 1
            ? AppTheme.Light
            : AppTheme.Dark;
        SettingsManager.Instance.UpdateTheme(theme);
    }

    private void SettingsTimeFormatCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsUiChange)
            return;

        var mode = SettingsTimeFormatCheckBox.IsChecked == true
            ? TimeFormatMode.Hour12
            : TimeFormatMode.Hour24;
        SettingsManager.Instance.UpdateTimeFormat(mode);
    }

    private void SettingsLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsUiChange || SettingsLanguageComboBox.SelectedItem is not LocaleInfo locale)
            return;

        SettingsManager.Instance.UpdateLanguage(locale.Code);
    }

    private void SettingsAiApiKeyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressAiApiKeyChange)
            return;

        SettingsManager.Instance.UpdateOpenRouterApiKey(SettingsAiApiKeyBox.Text);
    }

    private void SettingsAiModelTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsUiChange)
            return;

        var model = SettingsAiModelTextBox.Text.Trim();
        if (string.IsNullOrEmpty(model))
            model = "openai/gpt-4o-mini";

        SettingsManager.Instance.UpdatePreferredAiModel(model);
        SettingsAiModelTextBox.Text = SettingsManager.Instance.Current.PreferredAiModel;
    }

    private void TerminalToggleButton_Click(object sender, RoutedEventArgs e) =>
        ToggleTerminalPanel();

    private void ToggleTerminalPanel()
    {
        _isTerminalVisible = !_isTerminalVisible;

        if (_isTerminalVisible)
        {
            TerminalSplitter.Visibility = Visibility.Visible;
            PowerShellTerminal.Visibility = Visibility.Visible;
            TerminalSplitterRow.Height = new GridLength(6);
            TerminalSplitterRow.MinHeight = 6;
            TerminalPanelRow.Height = new GridLength(220);
            TerminalPanelRow.MinHeight = 120;

            var path = ActivePane.CurrentPath;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            if (!PowerShellTerminal.IsRunning)
                PowerShellTerminal.StartSession(path);
            else
                PowerShellTerminal.SyncWorkingDirectory(path);

            PowerShellTerminal.FocusInput();
            return;
        }

        PowerShellTerminal.Stop();
        TerminalSplitter.Visibility = Visibility.Collapsed;
        PowerShellTerminal.Visibility = Visibility.Collapsed;
        TerminalSplitterRow.Height = new GridLength(0);
        TerminalSplitterRow.MinHeight = 0;
        TerminalPanelRow.Height = new GridLength(0);
        TerminalPanelRow.MinHeight = 0;

        MainChromeHost.UpdateLayout();
    }

    private void PowerShellTerminal_CloseRequested(object? sender, EventArgs e)
    {
        if (_isTerminalVisible)
            ToggleTerminalPanel();
    }

    private void PowerShellTerminal_WorkingDirectoryChanged(object? sender, string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;

        var pane = ActivePane;
        if (string.IsNullOrEmpty(pane.CurrentPath) || !PathsEqual(pane.CurrentPath, directory))
            NavigateToDirectory(pane, directory, syncTree: true);
    }
}
