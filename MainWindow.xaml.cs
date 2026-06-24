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
    private const int MaxPaneCount = 8;

    private enum NamePromptMode
    {
        NewFolder,
        NewTextFile,
        Rename,
        GitCommit,
        GitAmend,
        GitBranch
    }

    private readonly FileIconService _iconService = new();
    private readonly FileSystemService _fileSystemService;
    private readonly NavigationHistoryService _navigationHistory;
    private readonly BookmarkService _bookmarkService;
    private readonly RecycleBinService _recycleBinService = new();
    private readonly ArchiveService _archiveService = new();
    private readonly EncryptedZipService _encryptedZipService = new();
    private readonly FileMoveService _fileMoveService = new();
    private readonly FileRenameService _fileRenameService = new();
    private readonly AiService _aiService = new();
    private readonly AiCommandExecutor _aiCommandExecutor = new();
    private const double JellySafeInset = 72;
    private const double JellySafeTotal = JellySafeInset * 2;

    private readonly WindowJellyDragEffect _jellyDrag;
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
    private FileSystemEntry? _namePromptRenameEntry;
    private string? _namePromptGitTargetDirectory;
    private string? _namePromptCreateTargetDirectory;
    private bool _isGitCommitInProgress;
    private bool _isGitHistoryInProgress;
    private DirectoryPaneState? _commitHistoryTargetPane;
    private string? _commitHistoryTargetDirectory;
    private bool _isEditorOpen;
    private int _chromeDimDepth;
    private List<FileSystemEntry> _pendingDeleteEntries = [];
    private DirectoryPaneState? _deleteTargetPane;

    private string? _encryptFolderSourcePath;
    private DirectoryPaneState? _encryptTargetPane;
    private TaskCompletionSource<string?>? _archivePasswordPrompt;
    private string? _openArchivePath;
    private string? _openArchivePassword;
    private bool _isArchiveViewerOpen;
    private bool _isExtracting;
    private bool _isEncrypting;
    private bool _paneRemoveInProgress;
    private bool _suppressSettingsUiChange;
    private bool _suppressAiApiKeyChange;
    private bool _isAiInProgress;
    private DirectoryPaneState? _aiTargetPane;
    private List<AiCommand> _aiPendingCommands = [];
    private bool _isTerminalVisible;
    private bool _terminalAnimationInProgress;

    private const double TerminalSplitterHeight = 6;

    private double SavedTerminalPanelHeight =>
        Math.Clamp(
            SettingsManager.Instance.Current.TerminalPanelHeight,
            SettingsManager.MinTerminalPanelHeight,
            SettingsManager.MaxTerminalPanelHeight);

    public MainWindow()
    {
        _fileSystemService = new FileSystemService(_iconService);
        _navigationHistory = new NavigationHistoryService(_iconService);
        _bookmarkService = new BookmarkService(_iconService);
        _suppressSettingsUiChange = true;
        InitializeComponent();
        _jellyDrag = new WindowJellyDragEffect(WindowShell, JellyContentHost);
        ApplyJellyLayout();
        SyncJellyFromSettings();
        InitializeExplorerFeatures();
        TerminalSplitter.DragCompleted += TerminalSplitter_DragCompleted;
        Loaded += MainWindow_Loaded;
        StateChanged += MainWindow_StateChanged;
        ContentRendered += MainWindow_ContentRendered;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _jellyDrag.Reset();
        _terminalAnimationInProgress = false;
        DisposeDriveChangeNotifications();
        DisposeWindowResize();
        DisposeTrayIcon();
        PersistSessionSettings();
        FinishHideTerminal(animate: false, waitForProcessStop: true);
    }

    public void PersistSessionSettings()
    {
        var terminalHeight = _isTerminalVisible && TerminalPanelRow.ActualHeight > 0
            ? TerminalPanelRow.ActualHeight
            : SettingsManager.Instance.Current.TerminalPanelHeight;

        var scrollSensitivity = SettingsManager.Instance.Current.ScrollSensitivity;
        if (IsLoaded && SettingsScrollSensitivitySlider is not null)
        {
            scrollSensitivity = SettingsScrollSensitivitySlider.Value;
        }

        double windowWidth;
        double windowHeight;
        if (WindowState == WindowState.Maximized)
        {
            var bounds = RestoreBounds;
            windowWidth = bounds.Width;
            windowHeight = bounds.Height;
        }
        else
        {
            var jellyOn = SettingsManager.Instance.Current.JellyDragEnabled;
            windowWidth = ToStoredWindowSize(Width, jellyOn);
            windowHeight = ToStoredWindowSize(Height, jellyOn);
        }

        SettingsManager.Instance.PersistSession(
            scrollSensitivity,
            terminalHeight,
            _isTerminalVisible,
            windowWidth,
            windowHeight,
            (int)WindowState);
    }

    private void TerminalSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (!_isTerminalVisible || TerminalPanelRow.ActualHeight <= 0)
            return;

        SettingsManager.Instance.UpdateTerminalPreferences(
            TerminalPanelRow.ActualHeight,
            isOpen: true);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isMediaViewerOpen)
        {
            CloseMediaViewer();
            e.Handled = true;
            return;
        }

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
        RestoreTerminalFromSettings();
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
        RestoreWindowLayout();

        SettingsManager.Instance.SettingChanged += OnSettingChanged;
        LocalizationManager.Instance.PropertyChanged += (_, _) => ApplyLocalizedStrings();
        SizeChanged += MainWindow_MediaViewerSizeChanged;
        _navigationHistory.LoadFromSettings();
        HistoryListBox.ItemsSource = _navigationHistory.Items;
        ApplyLocalizedStrings();
        _suppressSettingsUiChange = true;
        PopulateSettingsControls();
        _suppressSettingsUiChange = false;
        ApplyFileIconSettings();
        ApplyCustomFont();
        LoadDriveTree();
        InitializeDriveChangeNotifications();
        InitializeWindowResize();
        ApplyExternalToolAvailability();
        InitializeSystemIntegration();
    }

    private void RestoreWindowLayout()
    {
        var settings = SettingsManager.Instance.Current;
        ApplyJellyLayout();

        if (settings.WindowState == (int)WindowState.Maximized)
        {
            WindowState = WindowState.Maximized;
        }
        else if (settings.WindowWidth >= MinWidth && settings.WindowHeight >= MinHeight)
        {
            var jellyOn = settings.JellyDragEnabled;
            Width = FromStoredWindowWidth(settings.WindowWidth, jellyOn);
            Height = FromStoredWindowHeight(settings.WindowHeight, jellyOn);
        }

        _jellyDrag.Reset();
    }

    private void RestoreWindowFromSettings() => RestoreWindowLayout();

    private void RestoreTerminalFromSettings()
    {
        if (!ExternalToolsService.IsAvailable(ExternalTool.PowerShell))
            return;

        if (!SettingsManager.Instance.Current.IsTerminalOpen)
            return;

        ShowTerminalFromSettings();
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        _jellyDrag.Reset();

        var loc = LocalizationManager.Instance;
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized
            ? loc["UI_Restore"]
            : loc["UI_Maximize"];

        if (WindowState == WindowState.Minimized &&
            SettingsManager.Instance.Current.MinimizeToTray)
        {
            HideToTray();
        }
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
        control.RenameRequested += (_, _) => BeginRenameSelectedItem(pane);
        control.DeleteRequested += (_, _) => DeleteSelectedItems(pane);
        control.ExtractArchiveRequested += async (_, _) => await ExtractSelectedArchivesFromPaneAsync(pane);
        control.EncryptFolderRequested += (_, _) => BeginEncryptFolderWorkflow(pane);
        control.FileDropRequested += (_, e) => HandleFileDrop(pane, e.SourcePaths, e.TargetDirectory);
        control.GitInitRequested += (_, e) => HandleGitInit(pane, e.TargetDirectory);
        control.GitCommitRequested += (_, _) => HandleGitCommitRequest(pane);
        control.GitAmendRequested += (_, _) => HandleGitAmendRequest(pane);
        control.GitHistoryRequested += (_, _) => HandleGitHistoryRequest(pane);
        control.GitBranchRequested += (_, e) => HandleGitBranchRequest(pane, e.TargetDirectory);
        control.AiExecuteQueryRequested += (_, _) => HandleAiExecuteQueryRequest(pane);

        WirePaneExplorerFeatures(pane);

        _panes.Add(pane);
        UpdatePaneCloseButtonsVisibility();
        pane.Control.ApplyLocalization();
        pane.Control.SetGitFeaturesAvailable(ExternalToolsService.IsAvailable(ExternalTool.Git));
        pane.Control.SetAiFeaturesAvailable(IsAiFeatureAvailable());
        return pane;
    }

    private static bool IsAiFeatureAvailable() =>
        AiProviderCatalog.IsConfigured(SettingsManager.Instance.Current);

    private void ApplyExternalToolAvailability()
    {
        var gitAvailable = ExternalToolsService.IsAvailable(ExternalTool.Git);
        var terminalAvailable = ExternalToolsService.IsAvailable(ExternalTool.PowerShell);
        var aiAvailable = IsAiFeatureAvailable();

        foreach (var pane in _panes)
        {
            pane.Control.SetGitFeaturesAvailable(gitAvailable);
            pane.Control.SetAiFeaturesAvailable(aiAvailable);
        }

        TerminalToggleButton.Visibility = terminalAvailable
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!terminalAvailable && _isTerminalVisible)
            FinishHideTerminal(animate: false, waitForProcessStop: true);

        if (!aiAvailable)
        {
            if (AiPromptOverlay.Visibility == Visibility.Visible)
                HideAiPromptOverlay();
            if (AiPreviewOverlay.Visibility == Visibility.Visible)
            {
                HideAiPreviewOverlay();
                _aiPendingCommands = [];
                _aiTargetPane = null;
            }
        }
    }

    private void AddPaneButton_Click(object sender, RoutedEventArgs e)
    {
        if (_panes.Count >= MaxPaneCount)
        {
            StatusText.Text = LocalizationManager.Instance["UI_MaxPanesReached"];
            return;
        }

        var initialPath = ActivePane.CurrentPath;
        var pane = CreatePane();
        AppendPaneToHost(pane.Control);

        if (!string.IsNullOrEmpty(initialPath))
            NavigateToDirectory(pane, initialPath, syncTree: false, recordHistory: false);

        SetActivePane(pane);
    }

    private void RemovePane(DirectoryPaneState pane)
    {
        if (_panes.Count <= 1 || _paneRemoveInProgress)
            return;

        var wrapper = DirectoryPaneHost.GetPaneWrapper(pane.Control);
        if (wrapper is null || !SettingsManager.Instance.Current.EnableCloseAnimations)
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

    private void AppendPaneToHost(DirectoryPaneControl entrancePane)
    {
        var wrapper = DirectoryPaneHost.AppendPaneColumn(entrancePane, prepareEntrance: true);
        UiAnimationHelper.AnimatePaneEntrance(wrapper);
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

        OnPaneFileListSelectionChanged(pane);

        if (pane.NeedsMetadataRefresh)
            TriggerDeferredMetadataRefreshAsync(pane);
    }

    private async void TriggerDeferredMetadataRefreshAsync(DirectoryPaneState pane)
    {
        if (pane.CurrentContents is null || string.IsNullOrEmpty(pane.CurrentPath))
            return;

        pane.NeedsMetadataRefresh = false;
        CancelListingRefresh(pane);
        pane.ListingRefreshCancellation = new CancellationTokenSource();
        var cancellationToken = pane.ListingRefreshCancellation.Token;
        pane.ListingRefreshVersion++;
        var refreshVersion = pane.ListingRefreshVersion;

        var contents = pane.CurrentContents;
        var path = pane.CurrentPath;

        StatusText.Text = $"{contents.Count} item(s)";

        try
        {
            var gitTask = ApplyGitMetadataAsync(pane, contents, refreshVersion, cancellationToken);
            var sizesTask = PopulateDirectorySizesAsync(pane, path, contents, refreshVersion, cancellationToken);
            await Task.WhenAll(gitTask, sizesTask);
        }
        catch (OperationCanceledException)
        {
            // Pane navigated away or deactivated before metadata completed.
        }
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
            CancelPaneContentSearch(pane);
            HideContentSearchPanel(restoreListing: false);

            if (!string.IsNullOrEmpty(pane.CurrentPath))
                RefreshDirectoryListing(pane);

            return;
        }

        if (FileSystemService.TryResolveDirectoryPath(input, out var directoryPath))
        {
            CancelPaneSearch(pane);
            CancelPaneContentSearch(pane);

            if (pane.CurrentPath is null || !PathsEqual(directoryPath, pane.CurrentPath))
                NavigateToDirectory(pane, directoryPath, syncTree: pane == _activePane);

            return;
        }

        if (ContentSearchService.TryParseQuery(input, out _))
        {
            await HandleContentSearchInputAsync(pane, input, token);
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
        CloseMediaViewer();
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
        catch (Exception ex)
        {
            node.Children.Clear();
            if (ActivePane is not null)
                StatusText.Text = ex.Message;
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
                var folderPath = NormalizeDropTargetPath(node.FullPath);

                if (!IsPathInDragSource(folderPath, sourcePaths))
                    return folderPath;
            }
        }

        for (var source = e.OriginalSource as DependencyObject; source is not null; source = VisualTreeHelper.GetParent(source))
        {
            if (source is TreeViewItem { DataContext: DirectoryTreeNode node } &&
                !string.IsNullOrEmpty(node.FullPath))
            {
                var folderPath = NormalizeDropTargetPath(node.FullPath);

                if (!IsPathInDragSource(folderPath, sourcePaths))
                    return folderPath;
            }
        }

        return ActivePane.CurrentPath;
    }

    private static string NormalizeDropTargetPath(string path) => Path.GetFullPath(path);

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
            CloseMediaViewer();
            NavigateToDirectory(pane, entry.FullPath, syncTree: true);
            return;
        }

        await OpenFileFromPathAsync(entry.FullPath);
    }

    private void NavigateToDirectory(
        DirectoryPaneState pane,
        string path,
        bool syncTree,
        bool recordHistory = true)
    {
        try
        {
            var loc = LocalizationManager.Instance;
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                StatusText.Text = loc["UI_FolderNotFound"];
                return;
            }

            if (!Directory.Exists(fullPath))
            {
                StatusText.Text = loc["UI_FolderNotFound"];
                return;
            }

            CancelPaneSearch(pane);
            CancelPaneContentSearch(pane);
            CancelListingRefresh(pane);

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
            CloseMediaViewer();
            pane.CurrentPath = fullPath;
            SetPathSearchBoxText(pane, fullPath);

            if (syncTree && pane == _activePane)
                ScheduleSyncTreeToPath(pane, fullPath);

            RefreshDirectoryListing(pane);

            _navigationHistory.RecordFolder(fullPath);

            SyncActiveTabPath(pane);

            if (pane == _activePane)
                UpdateNavigationButtons(pane);
        }
        catch (UnauthorizedAccessException)
        {
            StatusText.Text = LocalizationManager.Instance["UI_AccessDenied"];
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

    private void HistoryNavigationButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateHistoryEmptyState();
        HistoryPopup.IsOpen = !HistoryPopup.IsOpen;
    }

    private void UpdateHistoryEmptyState()
    {
        var hasItems = _navigationHistory.Items.Count > 0;
        HistoryListBox.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyText.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        HistoryClearButton.IsEnabled = hasItems;
    }

    private async void HistoryListBox_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (HistoryListBox.SelectedItem is not NavigationHistoryItem item)
            return;

        HistoryPopup.IsOpen = false;
        await OpenHistoryItemAsync(item);
    }

    private void HistoryClearButton_Click(object sender, RoutedEventArgs e)
    {
        _navigationHistory.Clear();
        UpdateHistoryEmptyState();
        HistoryPopup.IsOpen = false;
    }

    private async Task OpenHistoryItemAsync(NavigationHistoryItem item)
    {
        var pane = ActivePane;
        var loc = LocalizationManager.Instance;

        if (item.Kind == NavigationHistoryKind.Folder)
        {
            if (!Directory.Exists(item.Path))
            {
                StatusText.Text = loc["UI_HistoryMissing"];
                return;
            }

            CancelPaneSearch(pane);
            NavigateToDirectory(pane, item.Path, syncTree: true);
            return;
        }

        if (!File.Exists(item.Path))
        {
            StatusText.Text = loc["UI_HistoryMissing"];
            return;
        }

        var parentDirectory = Path.GetDirectoryName(item.Path);
        if (!string.IsNullOrWhiteSpace(parentDirectory) && Directory.Exists(parentDirectory))
            NavigateToDirectory(pane, parentDirectory, syncTree: true, recordHistory: false);

        await OpenFileFromPathAsync(item.Path);
    }

    private async Task OpenFileFromPathAsync(string filePath)
    {
        if (ArchiveHelper.IsArchiveFile(filePath))
        {
            await OpenArchiveViewerAsync(filePath);
            return;
        }

        if (MediaViewerHelper.IsMediaFile(filePath))
        {
            if (SettingsManager.Instance.Current.UseBuiltInMediaViewer)
                await OpenMediaViewerAsync(filePath);
            else
                OpenFileWithDefaultApplication(filePath);

            return;
        }

        if (TextFileHelper.IsEditableTextFile(filePath))
        {
            if (!TextFileHelper.IsWithinEditorSizeLimit(filePath, out var fileSize))
            {
                OpenFileWithDefaultApplication(filePath);
                StatusText.Text = $"Opened with default app ({TextFileHelper.FormatByteSize(fileSize)} exceeds built-in editor limit).";
                return;
            }

            await OpenFileInEditorAsync(filePath);
            return;
        }

        OpenFileWithDefaultApplication(filePath);
    }

    private async void RefreshDirectoryListing(DirectoryPaneState pane)
    {
        if (string.IsNullOrEmpty(pane.CurrentPath))
            return;

        CancelListingRefresh(pane);

        var path = pane.CurrentPath;
        pane.ListingRefreshCancellation = new CancellationTokenSource();
        var cancellationToken = pane.ListingRefreshCancellation.Token;
        pane.ListingRefreshVersion++;
        var refreshVersion = pane.ListingRefreshVersion;

        ClearPaneFileListSelection(pane);

        IReadOnlyList<FileSystemEntry> contents;
        try
        {
            contents = await _fileSystemService.GetDirectoryContentsAsync(path, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
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
        pane.CurrentContents = contents;

        if (!string.IsNullOrEmpty(pane.PendingSelectionPath))
        {
            var pathToSelect = pane.PendingSelectionPath;
            pane.PendingSelectionPath = null;
            await Dispatcher.InvokeAsync(
                () => SelectFileEntryByPath(pane, pathToSelect),
                DispatcherPriority.Loaded);
        }

        if (pane == _activePane)
        {
            StatusText.Text = $"{contents.Count} item(s)";
            var shouldAnimate = ShouldPlayListRefreshAnimation(pane);
            pane.LastListingShownUtc = DateTime.UtcNow;
            if (shouldAnimate)
                pane.Control.PlayListRefreshAnimation();

            pane.NeedsMetadataRefresh = false;
            var gitTask = ApplyGitMetadataAsync(pane, contents, refreshVersion, cancellationToken);
            var sizesTask = PopulateDirectorySizesAsync(pane, path, contents, refreshVersion, cancellationToken);
            await Task.WhenAll(gitTask, sizesTask);
        }
        else
        {
            pane.LastListingShownUtc = DateTime.UtcNow;
            pane.NeedsMetadataRefresh = true;
        }
    }

    private static void CancelListingRefresh(DirectoryPaneState pane)
    {
        if (pane.ListingRefreshCancellation is null)
            return;

        try
        {
            pane.ListingRefreshCancellation.Cancel();
        }
        catch
        {
            // Ignore cancellation races.
        }

        pane.ListingRefreshCancellation.Dispose();
        pane.ListingRefreshCancellation = null;
    }

    private static bool ShouldPlayListRefreshAnimation(DirectoryPaneState pane)
    {
        if (pane.LastListingShownUtc == default)
            return true;

        return (DateTime.UtcNow - pane.LastListingShownUtc).TotalMilliseconds > 300;
    }

    private void ScheduleSyncTreeToPath(DirectoryPaneState pane, string path)
    {
        pane.PendingTreeSyncVersion++;
        var syncVersion = pane.PendingTreeSyncVersion;
        var targetPath = path;

        Dispatcher.BeginInvoke(() =>
        {
            if (syncVersion != pane.PendingTreeSyncVersion)
                return;

            if (pane.CurrentPath is null ||
                !PathsEqual(pane.CurrentPath, targetPath))
            {
                return;
            }

            SyncTreeToPath(targetPath);
        }, DispatcherPriority.Background);
    }

    private async Task PopulateDirectorySizesAsync(
        DirectoryPaneState pane,
        string path,
        IReadOnlyList<FileSystemEntry> entries,
        int refreshVersion,
        CancellationToken cancellationToken)
    {
        var directories = entries.Where(entry => entry.IsDirectory).ToArray();
        if (directories.Length == 0)
            return;

        try
        {
            await Task.Delay(200, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (refreshVersion != pane.ListingRefreshVersion ||
            !string.Equals(pane.CurrentPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Dictionary<string, long> sizes;
        try
        {
            sizes = await Task.Run(() =>
            {
                var result = new Dictionary<string, long>(directories.Length, StringComparer.OrdinalIgnoreCase);

                Parallel.ForEach(
                    directories,
                    new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2)
                    },
                    directory =>
                    {
                        result[directory.FullPath] = DirectorySizeHelper.CalculateSize(
                            directory.FullPath,
                            cancellationToken);
                    });

                return result;
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (refreshVersion != pane.ListingRefreshVersion ||
            !string.Equals(pane.CurrentPath, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var directory in directories)
        {
            if (sizes.TryGetValue(directory.FullPath, out var size))
                directory.Size = size;
        }
    }

    private async Task ApplyGitMetadataAsync(
        DirectoryPaneState pane,
        IReadOnlyList<FileSystemEntry> entries,
        int refreshVersion,
        CancellationToken cancellationToken)
    {
        if (!ExternalToolsService.IsAvailable(ExternalTool.Git))
        {
            pane.Control.UpdateGitStatus(false, string.Empty, 0);
            return;
        }

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

        if (cancellationToken.IsCancellationRequested)
            return;

        var status = await GitService.GetWorkingTreeStatusAsync(repoRoot);
        if (cancellationToken.IsCancellationRequested ||
            refreshVersion != pane.ListingRefreshVersion)
        {
            return;
        }

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
                pane.Control.PlayListRefreshAnimation();
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
        if (isSearching)
        {
            SearchingIndicator.Visibility = Visibility.Visible;
            UiAnimationHelper.StartPulse(SearchingIndicator);
        }
        else
        {
            UiAnimationHelper.StopPulse(SearchingIndicator);
            SearchingIndicator.Visibility = Visibility.Collapsed;
        }

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

            _navigationHistory.RecordFile(filePath);
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

    private bool _terminalSuppressedForModal;

    private void PushChromeDimOverlay()
    {
        if (_chromeDimDepth == 0)
        {
            SuppressTerminalForModalIfNeeded();
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
            RestoreTerminalAfterModalIfNeeded();
        };
        ChromeDimOverlay.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    private void SuppressTerminalForModalIfNeeded()
    {
        if (!_isTerminalVisible || _terminalSuppressedForModal)
            return;

        _terminalSuppressedForModal = true;
        TerminalSplitter.Visibility = Visibility.Collapsed;
        PowerShellTerminal.Visibility = Visibility.Collapsed;
    }

    private void RestoreTerminalAfterModalIfNeeded()
    {
        if (!_terminalSuppressedForModal)
            return;

        _terminalSuppressedForModal = false;

        if (!_isTerminalVisible)
            return;

        TerminalSplitter.Visibility = Visibility.Visible;
        PowerShellTerminal.Visibility = Visibility.Visible;
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
        var folderEntries = selectedEntries.Where(entry => entry.IsDirectory).ToList();
        var isSingleFolderSelection = folderEntries.Count == 1 && selectedEntries.Count == 1;

        pane.Control.ExtractArchiveMenuItemControl.Visibility =
            isArchiveSelection ? Visibility.Visible : Visibility.Collapsed;
        pane.Control.ExtractArchiveSeparatorControl.Visibility =
            isArchiveSelection ? Visibility.Visible : Visibility.Collapsed;
        pane.Control.EncryptFolderMenuItemControl.Visibility =
            isSingleFolderSelection ? Visibility.Visible : Visibility.Collapsed;
        pane.Control.EncryptFolderSeparatorControl.Visibility =
            isSingleFolderSelection ? Visibility.Visible : Visibility.Collapsed;

        var hasResolvedPath = TryGetGitContextDirectory(pane, out var resolvedDirectory);
        var gitAvailable = ExternalToolsService.IsAvailable(ExternalTool.Git);
        var repositoryRoot = gitAvailable && hasResolvedPath
            ? GitRepositoryHelper.GetGitRepositoryRoot(resolvedDirectory)
            : null;
        var isGitRepo = repositoryRoot is not null;

        if (gitAvailable)
        {
            pane.Control.GitInitMenuItemControl.IsEnabled = hasResolvedPath && !isGitRepo;
            pane.Control.GitCommitMenuItemControl.IsEnabled = isGitRepo && !_isGitCommitInProgress;
            pane.Control.GitAmendMenuItemControl.IsEnabled = isGitRepo && !_isGitCommitInProgress;
            pane.Control.GitHistoryMenuItemControl.IsEnabled = isGitRepo && !_isGitHistoryInProgress;
        }

        if (IsAiFeatureAvailable())
            pane.Control.AiExecuteQueryMenuItemControl.IsEnabled = hasResolvedPath && !_isAiInProgress;

        foreach (var item in menu.Items)
        {
            if (item is not MenuItem menuItem)
                continue;

            var tag = (string?)menuItem.Tag;
            if (tag is "GitInit" or "GitCommit" or "GitAmend" or "GitHistory" or "AiExecuteQuery" or "PaneSync" or "Bookmark")
                continue;

            menuItem.IsEnabled = tag switch
            {
                "Delete" => hasSelection,
                "Rename" => hasSelection && selectedEntries.Count == 1,
                "ExtractArchive" => isArchiveSelection && hasResolvedPath && !_isExtracting,
                "EncryptFolder" => isSingleFolderSelection && hasResolvedPath && !_isExtracting && !_isEncrypting,
                "ShowWindowsMenu" => hasSelection || hasResolvedPath,
                _ => hasResolvedPath
            };
        }

        UpdateExplorerContextMenuState(pane);
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

    private void HandleGitCommitRequest(DirectoryPaneState pane)
    {
        if (!ExternalToolsService.IsAvailable(ExternalTool.Git))
            return;

        BeginGitCommitPromptWorkflow(pane, NamePromptMode.GitCommit, requireChanges: false);
    }

    private void HandleGitAmendRequest(DirectoryPaneState pane)
    {
        if (!ExternalToolsService.IsAvailable(ExternalTool.Git))
            return;

        BeginGitCommitPromptWorkflow(pane, NamePromptMode.GitAmend, requireChanges: true);
    }

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
        if (!ExternalToolsService.IsAvailable(ExternalTool.Git))
            return;

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
        if (!ExternalToolsService.IsAvailable(ExternalTool.Git))
            return;

        if (!EnsureGitRepositoryOrNotify(pane, out var repositoryRoot))
            return;

        _ = ShowCommitHistoryAsync(pane, repositoryRoot);
    }

    private void HandleAiExecuteQueryRequest(DirectoryPaneState pane)
    {
        if (!IsAiFeatureAvailable())
            return;

        SetActivePane(pane);

        if (string.IsNullOrWhiteSpace(pane.CurrentPath) || !Directory.Exists(pane.CurrentPath))
        {
            StatusText.Text = LocalizationManager.Instance["UI_GitNoDirectory"];
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

    private void BeginRenameSelectedItem(DirectoryPaneState pane)
    {
        if (NamePromptOverlay.Visibility == Visibility.Visible ||
            _isEditorOpen ||
            _isArchiveViewerOpen ||
            _isMediaViewerOpen)
        {
            return;
        }

        var selectedEntries = GetSelectedFileEntries(pane.Control.FileListView);
        if (selectedEntries.Count != 1)
            return;

        ShowRenamePrompt(pane, selectedEntries[0]);
    }

    private void ShowRenamePrompt(DirectoryPaneState pane, FileSystemEntry entry)
    {
        if (string.IsNullOrEmpty(pane.CurrentPath))
            return;

        _namePromptTargetPane = pane;
        _namePromptMode = NamePromptMode.Rename;
        _namePromptRenameEntry = entry;
        _namePromptGitTargetDirectory = null;

        var loc = LocalizationManager.Instance;
        NamePromptTitleText.Text = loc["UI_RenameTitle"];
        NamePromptTextBox.Text = entry.Name;
        NamePromptCreateButton.Content = loc["UI_RenameConfirm"];
        GitCommitFilesList.Visibility = Visibility.Collapsed;
        GitCommitFilesList.ItemsSource = null;

        HideNamePromptError();
        PushChromeDimOverlay();
        NamePromptOverlay.Visibility = Visibility.Visible;
        UiAnimationHelper.ShowOverlay(NamePromptPanel);

        Dispatcher.BeginInvoke(() =>
        {
            NamePromptTextBox.Focus();
            Keyboard.Focus(NamePromptTextBox);
            NamePromptTextBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private static void SelectFileEntryByPath(DirectoryPaneState pane, string fullPath)
    {
        if (pane.CurrentContents is null)
            return;

        var entry = pane.CurrentContents.FirstOrDefault(candidate =>
            string.Equals(candidate.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            return;

        pane.Control.FileListView.SelectedItem = entry;
        pane.Control.FileListView.ScrollIntoView(entry);
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
            var password = await ResolveArchivePasswordAsync(_openArchivePath, _openArchivePassword);
            if (password is null && _archiveService.RequiresPassword(_openArchivePath))
                return;

            _openArchivePassword = password;

            foreach (var entry in entries)
            {
                await _archiveService.ExtractEntryAsync(_openArchivePath, entry.EntryKey, activePath, password);
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
        catch (Exception ex) when (ArchiveHelper.IsPasswordRelatedError(ex))
        {
            _openArchivePassword = null;
            var message = LocalizationManager.Instance["UI_ArchivePasswordWrong"];
            if (_isArchiveViewerOpen)
                ShowArchiveError(message);
            StatusText.Text = message;
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
        CloseMediaViewer(animate: false);

        _openArchivePath = archivePath;
        _openArchivePassword = null;
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
            var password = await ResolveArchivePasswordAsync(archivePath);
            if (password is null && _archiveService.RequiresPassword(archivePath))
            {
                CloseArchiveViewer(animate: false);
                return;
            }

            _openArchivePassword = password;
            var entries = await Task.Run(() => _archiveService.GetEntries(archivePath, password));
            ArchiveContentsList.ItemsSource = entries;
            ArchiveExtractButton.IsEnabled = !_isExtracting;
            _navigationHistory.RecordFile(archivePath);
            StatusText.Text = $"{entries.Count} file(s) in {Path.GetFileName(archivePath)}";
        }
        catch (Exception ex) when (ArchiveHelper.IsPasswordRelatedError(ex))
        {
            _openArchivePassword = null;
            ShowArchiveError(LocalizationManager.Instance["UI_ArchivePasswordWrong"]);
            ArchiveContentsList.Visibility = Visibility.Collapsed;
            StatusText.Text = LocalizationManager.Instance["UI_ArchivePasswordWrong"];
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
        _openArchivePassword = null;
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
            var password = await ResolveArchivePasswordAsync(archivePath);
            if (password is null && _archiveService.RequiresPassword(archivePath))
                return;

            await _archiveService.ExtractAllAsync(archivePath, destinationDirectory, password);
            StatusText.Text = $"Extracted to \"{folderName}\".";
            RefreshDirectoryListing(pane);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Extraction cancelled.";
        }
        catch (Exception ex) when (ArchiveHelper.IsPasswordRelatedError(ex))
        {
            var message = LocalizationManager.Instance["UI_ArchivePasswordWrong"];
            if (_isArchiveViewerOpen)
                ShowArchiveError(message);
            StatusText.Text = message;
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
        if (ArchiveHelper.IsPasswordRelatedError(ex))
            return LocalizationManager.Instance["UI_ArchivePasswordWrong"];

        var typeName = ex.GetType().Name;
        if (typeName.Contains("Archive", StringComparison.OrdinalIgnoreCase) ||
            ex is InvalidOperationException or IOException)
        {
            return "The archive appears to be corrupted or cannot be read.";
        }

        return ex.Message;
    }

    private async Task<string?> ResolveArchivePasswordAsync(string archivePath, string? knownPassword = null)
    {
        if (!string.IsNullOrEmpty(knownPassword))
            return knownPassword;

        if (!_archiveService.RequiresPassword(archivePath))
            return null;

        return await PromptArchivePasswordAsync();
    }

    private Task<string?> PromptArchivePasswordAsync()
    {
        _archivePasswordPrompt = new TaskCompletionSource<string?>();
        ArchivePasswordBox.Password = string.Empty;
        HideArchivePasswordError();
        ArchivePasswordOverlay.Visibility = Visibility.Visible;
        PushChromeDimOverlay();
        UiAnimationHelper.ShowOverlay(ArchivePasswordPanel);

        Dispatcher.BeginInvoke(() =>
        {
            ArchivePasswordBox.Focus();
            Keyboard.Focus(ArchivePasswordBox);
        }, DispatcherPriority.Loaded);

        return _archivePasswordPrompt.Task;
    }

    private void CompleteArchivePasswordPrompt(string? password)
    {
        _archivePasswordPrompt?.TrySetResult(password);
        _archivePasswordPrompt = null;
        HideArchivePasswordOverlay();
    }

    private void HideArchivePasswordOverlay()
    {
        if (ArchivePasswordOverlay.Visibility != Visibility.Visible)
            return;

        void FinishHide()
        {
            ArchivePasswordOverlay.Visibility = Visibility.Collapsed;
            ArchivePasswordPanel.Visibility = Visibility.Visible;
            ArchivePasswordBox.Password = string.Empty;
            HideArchivePasswordError();
            PopChromeDimOverlay();
        }

        if (ArchivePasswordPanel.Visibility != Visibility.Visible)
        {
            FinishHide();
            return;
        }

        UiAnimationHelper.HideOverlay(ArchivePasswordPanel, FinishHide);
    }

    private void ShowArchivePasswordError(string message)
    {
        ArchivePasswordErrorText.Text = message;
        ArchivePasswordErrorText.Visibility = Visibility.Visible;
    }

    private void HideArchivePasswordError()
    {
        ArchivePasswordErrorText.Text = string.Empty;
        ArchivePasswordErrorText.Visibility = Visibility.Collapsed;
    }

    private void ArchivePasswordCancelButton_Click(object sender, RoutedEventArgs e) =>
        CompleteArchivePasswordPrompt(null);

    private void ArchivePasswordConfirmButton_Click(object sender, RoutedEventArgs e) =>
        ConfirmArchivePassword();

    private void ArchivePasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ConfirmArchivePassword();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            CompleteArchivePasswordPrompt(null);
            e.Handled = true;
        }
    }

    private void ConfirmArchivePassword()
    {
        var loc = LocalizationManager.Instance;
        if (string.IsNullOrEmpty(ArchivePasswordBox.Password))
        {
            ShowArchivePasswordError(loc["UI_ArchivePasswordEmpty"]);
            return;
        }

        CompleteArchivePasswordPrompt(ArchivePasswordBox.Password);
    }

    private void BeginEncryptFolderWorkflow(DirectoryPaneState pane)
    {
        if (_isEncrypting || _isExtracting)
            return;

        var selected = GetSelectedFileEntries(pane.Control.FileListView)
            .Where(entry => entry.IsDirectory)
            .ToList();

        if (selected.Count != 1)
            return;

        _encryptTargetPane = pane;
        _encryptFolderSourcePath = selected[0].FullPath;
        ShowEncryptFolderOverlay();
    }

    private void ShowEncryptFolderOverlay()
    {
        if (string.IsNullOrWhiteSpace(_encryptFolderSourcePath))
            return;

        var loc = LocalizationManager.Instance;
        var folderName = Path.GetFileName(_encryptFolderSourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(folderName))
            folderName = "folder";

        EncryptFolderTitleText.Text = loc["UI_EncryptFolderTitle"];
        EncryptFolderPathLabel.Text = loc["UI_EncryptFolderSource"];
        EncryptFolderArchiveNameLabel.Text = loc["UI_EncryptFolderArchiveName"];
        EncryptFolderMethodLabel.Text = loc["UI_EncryptFolderMethod"];
        EncryptFolderPasswordLabel.Text = loc["UI_EncryptFolderPassword"];
        EncryptFolderConfirmPasswordLabel.Text = loc["UI_EncryptFolderConfirmPassword"];
        EncryptFolderCreateButton.Content = loc["UI_EncryptFolderCreate"];
        EncryptFolderPathText.Text = _encryptFolderSourcePath;
        EncryptFolderArchiveNameTextBox.Text = $"{folderName}.zip";
        EncryptFolderPasswordBox.Password = string.Empty;
        EncryptFolderConfirmPasswordBox.Password = string.Empty;
        PopulateEncryptFolderMethodCombo();
        HideEncryptFolderError();

        EncryptFolderOverlay.Visibility = Visibility.Visible;
        PushChromeDimOverlay();
        UiAnimationHelper.ShowOverlay(EncryptFolderPanel);

        Dispatcher.BeginInvoke(() =>
        {
            EncryptFolderArchiveNameTextBox.Focus();
            EncryptFolderArchiveNameTextBox.CaretIndex = EncryptFolderArchiveNameTextBox.Text.Length;
        }, DispatcherPriority.Loaded);
    }

    private void PopulateEncryptFolderMethodCombo()
    {
        var loc = LocalizationManager.Instance;
        var selected = EncryptFolderMethodComboBox.SelectedItem is ComboItem<FolderZipEncryptionMethod> current
            ? current.Value
            : FolderZipEncryptionMethod.Aes256;
        EncryptFolderMethodComboBox.Items.Clear();
        EncryptFolderMethodComboBox.Items.Add(new ComboItem<FolderZipEncryptionMethod>(
            loc["UI_EncryptFolderMethodZipCrypto"],
            FolderZipEncryptionMethod.ZipCrypto));
        EncryptFolderMethodComboBox.Items.Add(new ComboItem<FolderZipEncryptionMethod>(
            loc["UI_EncryptFolderMethodAes256"],
            FolderZipEncryptionMethod.Aes256));
        EncryptFolderMethodComboBox.DisplayMemberPath = nameof(ComboItem<FolderZipEncryptionMethod>.Label);
        EncryptFolderMethodComboBox.SelectedValuePath = nameof(ComboItem<FolderZipEncryptionMethod>.Value);
        EncryptFolderMethodComboBox.SelectedIndex = selected == FolderZipEncryptionMethod.Aes256 ? 1 : 0;
    }

    private void HideEncryptFolderOverlay()
    {
        if (EncryptFolderOverlay.Visibility != Visibility.Visible)
            return;

        void FinishHide()
        {
            EncryptFolderOverlay.Visibility = Visibility.Collapsed;
            EncryptFolderPanel.Visibility = Visibility.Visible;
            _encryptFolderSourcePath = null;
            _encryptTargetPane = null;
            HideEncryptFolderError();
            PopChromeDimOverlay();
        }

        if (EncryptFolderPanel.Visibility != Visibility.Visible)
        {
            FinishHide();
            return;
        }

        UiAnimationHelper.HideOverlay(EncryptFolderPanel, FinishHide);
    }

    private void ShowEncryptFolderError(string message)
    {
        EncryptFolderErrorText.Text = message;
        EncryptFolderErrorText.Visibility = Visibility.Visible;
    }

    private void HideEncryptFolderError()
    {
        EncryptFolderErrorText.Text = string.Empty;
        EncryptFolderErrorText.Visibility = Visibility.Collapsed;
    }

    private void EncryptFolderCancelButton_Click(object sender, RoutedEventArgs e) =>
        HideEncryptFolderOverlay();

    private void EncryptFolderCreateButton_Click(object sender, RoutedEventArgs e) =>
        _ = CreateEncryptedFolderArchiveAsync();

    private void EncryptFolderConfirmPasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _ = CreateEncryptedFolderArchiveAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            HideEncryptFolderOverlay();
            e.Handled = true;
        }
    }

    private void EncryptFolderMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private async Task CreateEncryptedFolderArchiveAsync()
    {
        var loc = LocalizationManager.Instance;

        if (string.IsNullOrWhiteSpace(_encryptFolderSourcePath) || _encryptTargetPane is null)
            return;

        var password = EncryptFolderPasswordBox.Password;
        var confirmPassword = EncryptFolderConfirmPasswordBox.Password;

        if (string.IsNullOrEmpty(password))
        {
            ShowEncryptFolderError(loc["UI_EncryptFolderEmptyPassword"]);
            return;
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            ShowEncryptFolderError(loc["UI_EncryptFolderPasswordMismatch"]);
            return;
        }

        var archiveName = EncryptFolderArchiveNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(archiveName) ||
            !archiveName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            archiveName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            ShowEncryptFolderError(loc["UI_EncryptFolderInvalidName"]);
            return;
        }

        var parentDirectory = Path.GetDirectoryName(_encryptFolderSourcePath);
        if (string.IsNullOrEmpty(parentDirectory))
        {
            ShowEncryptFolderError(loc["UI_EncryptFolderFailed"]);
            return;
        }

        var destinationZipPath = Path.Combine(parentDirectory, archiveName);
        if (File.Exists(destinationZipPath))
        {
            ShowEncryptFolderError($"A file named \"{archiveName}\" already exists.");
            return;
        }

        var method = EncryptFolderMethodComboBox.SelectedItem is ComboItem<FolderZipEncryptionMethod> item
            ? item.Value
            : FolderZipEncryptionMethod.Aes256;

        var pane = _encryptTargetPane;
        var sourcePath = _encryptFolderSourcePath;
        HideEncryptFolderOverlay();
        _isEncrypting = true;
        StatusText.Text = loc["UI_EncryptFolderCreating"];

        try
        {
            await _encryptedZipService.CreateFromDirectoryAsync(
                sourcePath,
                destinationZipPath,
                password,
                method,
                new Progress<string>(path => StatusText.Text = $"{loc["UI_EncryptFolderCreating"]} {path}"));

            StatusText.Text = string.Format(loc["UI_EncryptFolderSuccess"], archiveName);
            RefreshDirectoryListing(pane);
        }
        catch (Exception ex)
        {
            StatusText.Text = loc["UI_EncryptFolderFailed"];
            MessageBox.Show(
                ex.Message,
                loc["UI_EncryptFolderFailed"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isEncrypting = false;
        }
    }

    private sealed class ComboItem<T>(string label, T value)
    {
        public string Label { get; } = label;
        public T Value { get; } = value;
    }

    private void SetExtractingUiState(bool isExtracting)
    {
        _isExtracting = isExtracting;

        if (isExtracting)
        {
            ExtractingIndicator.Visibility = Visibility.Visible;
            UiAnimationHelper.StartPulse(ExtractingIndicator);
        }
        else
        {
            UiAnimationHelper.StopPulse(ExtractingIndicator);
            ExtractingIndicator.Visibility = Visibility.Collapsed;
        }

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

    private void ShowGitError(string title, string message)
    {
        GitErrorTitleText.Text = title;
        GitErrorMessageText.Text = message;
        PushChromeDimOverlay();
        GitErrorOverlay.Visibility = Visibility.Visible;
        UiAnimationHelper.ShowOverlay(GitErrorPanel);
    }

    private void HideGitError()
    {
        if (GitErrorOverlay.Visibility != Visibility.Visible)
            return;

        UiAnimationHelper.HideOverlay(GitErrorPanel, () =>
        {
            GitErrorOverlay.Visibility = Visibility.Collapsed;
            PopChromeDimOverlay();
        });
    }

    private void GitErrorCloseButton_Click(object sender, RoutedEventArgs e) =>
        HideGitError();

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
        List<GitFileStatus>? gitChanges = null,
        string? createTargetDirectory = null)
    {
        var loc = LocalizationManager.Instance;
        var createBasePath = string.IsNullOrWhiteSpace(createTargetDirectory)
            ? pane.CurrentPath
            : Path.GetFullPath(createTargetDirectory);

        if (mode is NamePromptMode.NewFolder or NamePromptMode.NewTextFile &&
            string.IsNullOrEmpty(createBasePath))
        {
            StatusText.Text = "Select a folder before creating items.";
            return;
        }

        if (mode != NamePromptMode.GitCommit &&
            mode != NamePromptMode.GitAmend &&
            mode != NamePromptMode.GitBranch &&
            mode != NamePromptMode.Rename &&
            string.IsNullOrEmpty(pane.CurrentPath) &&
            string.IsNullOrEmpty(createBasePath))
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
        _namePromptRenameEntry = null;
        _namePromptCreateTargetDirectory = mode is NamePromptMode.NewFolder or NamePromptMode.NewTextFile &&
                                           !string.IsNullOrWhiteSpace(createTargetDirectory)
            ? Path.GetFullPath(createTargetDirectory)
            : null;

        GitCommitFilesList.Visibility = Visibility.Collapsed;
        GitCommitFilesList.ItemsSource = null;

        switch (mode)
        {
            case NamePromptMode.NewFolder:
                NamePromptTitleText.Text = loc["UI_NewFolder"];
                NamePromptTextBox.Text = loc["UI_NewFolder"];
                NamePromptCreateButton.Content = loc["UI_Create"];
                break;
            case NamePromptMode.NewTextFile:
                NamePromptTitleText.Text = loc["UI_NewTextFile"];
                NamePromptTextBox.Text = loc["UI_NewTextFileDefaultName"];
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
        UiAnimationHelper.ShowOverlay(NamePromptPanel);

        Dispatcher.BeginInvoke(() =>
        {
            NamePromptTextBox.Focus();
            Keyboard.Focus(NamePromptTextBox);
            NamePromptTextBox.CaretIndex = NamePromptTextBox.Text.Length;
        }, DispatcherPriority.Input);
    }

    private void HideNamePrompt(bool animate = true)
    {
        if (NamePromptOverlay.Visibility != Visibility.Visible)
            return;

        void CompleteHide()
        {
            NamePromptOverlay.Visibility = Visibility.Collapsed;
            NamePromptPanel.Visibility = Visibility.Visible;
            NamePromptTextBox.Clear();
            GitCommitFilesList.ItemsSource = null;
            GitCommitFilesList.Visibility = Visibility.Collapsed;
            _namePromptTargetPane = null;
            _namePromptGitTargetDirectory = null;
            _namePromptRenameEntry = null;
            _namePromptCreateTargetDirectory = null;
            HideNamePromptError();
            PopChromeDimOverlay();
        }

        if (!animate)
        {
            CompleteHide();
            return;
        }

        NamePromptPanel.Visibility = Visibility.Visible;
        UiAnimationHelper.HideOverlay(NamePromptPanel, CompleteHide);
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

        if (_namePromptMode == NamePromptMode.Rename)
        {
            TryRenameFromNamePrompt();
            return;
        }

        if (_namePromptTargetPane is null)
        {
            ShowNamePromptError("No folder is selected.");
            return;
        }

        var pane = _namePromptTargetPane;
        var createBasePath = _namePromptCreateTargetDirectory ?? pane.CurrentPath;

        if (string.IsNullOrEmpty(createBasePath))
        {
            ShowNamePromptError("No folder is selected.");
            return;
        }

        if (!TryNormalizeNewItemName(NamePromptTextBox.Text, _namePromptMode, out var itemName, out var error))
        {
            ShowNamePromptError(error);
            StatusText.Text = error;
            return;
        }

        try
        {
            var targetPath = Path.Combine(createBasePath, itemName);

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
            if (pane.CurrentPath is not null && PathsEqual(pane.CurrentPath, createBasePath))
                RefreshDirectoryListing(pane);

            if (_directoryTreeMenuTarget is not null &&
                !string.IsNullOrEmpty(_directoryTreeMenuTarget.FullPath) &&
                IsPathWithinRoot(createBasePath, _directoryTreeMenuTarget.FullPath))
            {
                ReloadTreeBranch(_directoryTreeMenuTarget);
            }
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

    private void TryRenameFromNamePrompt()
    {
        var loc = LocalizationManager.Instance;
        var pane = _namePromptTargetPane;
        var entry = _namePromptRenameEntry;

        if (pane is null || entry is null)
        {
            ShowNamePromptError(loc["UI_RenameNoSelection"]);
            return;
        }

        var result = _fileRenameService.Rename(entry.FullPath, NamePromptTextBox.Text);
        if (!result.Success)
        {
            ShowNamePromptError(result.ErrorMessage ?? loc["UI_RenameFailed"]);
            StatusText.Text = result.ErrorMessage ?? loc["UI_RenameFailed"];
            return;
        }

        HideNamePrompt();

        if (result.NoChange)
            return;

        if (!string.IsNullOrEmpty(result.DestinationPath))
            pane.PendingSelectionPath = result.DestinationPath;

        RefreshDirectoryListing(pane);

        var newName = Path.GetFileName(result.DestinationPath ?? entry.Name);
        StatusText.Text = string.Format(loc["UI_RenameSuccess"], newName);

        if (pane == _activePane &&
            !string.IsNullOrEmpty(pane.CurrentPath) &&
            result.DestinationPath is not null &&
            IsPathWithinRoot(pane.CurrentPath, result.DestinationPath))
        {
            SyncTreeToPath(pane.CurrentPath);
        }
    }

    private async void HandleGitInit(DirectoryPaneState pane, string targetDirectory)
    {
        if (!ExternalToolsService.IsAvailable(ExternalTool.Git))
            return;

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
            ShowGitError(loc["UI_GitCommitErrorTitle"], ex.ToString());
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
            ShowGitError(loc["UI_GitCommitErrorTitle"], result.Message);
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

        _isGitHistoryInProgress = true;
        StatusText.Text = loc["UI_GitLoadingHistory"];

        try
        {
            var historyResult = await GitService.GetCommitHistoryAsync(repositoryRoot).ConfigureAwait(true);
            if (!historyResult.Success)
            {
                StatusText.Text = historyResult.Message;
                return;
            }

            CommitHistoryListBox.ItemsSource = null;
            ResetCommitHistoryActionState();
            HideCommitHistoryError();
            BindCommitHistoryList(historyResult.Commits);

            if (historyResult.Commits.Count == 0)
                ShowCommitHistoryError(loc["UI_GitNoCommitsYet"]);

            PushChromeDimOverlay();
            CommitHistoryOverlay.Visibility = Visibility.Visible;
            UiAnimationHelper.ShowOverlay(CommitHistoryPanel);
            StatusText.Text = loc["UI_Ready"];
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            ShowCommitHistoryError(ex.Message);
        }
        finally
        {
            _isGitHistoryInProgress = false;
        }
    }

    private void BindCommitHistoryList(IReadOnlyList<GitCommit> commits)
    {
        CommitHistoryListBox.ItemsSource = commits;
        CommitHistoryListBox.SelectedIndex = commits.Count > 0 ? 0 : -1;
        ResetCommitHistoryActionState();
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
        var hasSelection = CommitHistoryListBox.SelectedItem is GitCommit;
        CommitHistoryRestoreButton.IsEnabled = hasSelection;
        CommitHistoryDeleteCommitButton.IsEnabled = hasSelection;
    }

    private async Task BindCommitHistoryListAsync(IReadOnlyList<GitCommit> commits)
    {
        await Dispatcher.InvokeAsync(() => BindCommitHistoryList(commits));
    }

    private async Task RefreshCommitHistoryListAsync()
    {
        var targetDirectory = _commitHistoryTargetDirectory;
        if (string.IsNullOrWhiteSpace(targetDirectory))
            return;

        targetDirectory = Path.GetFullPath(targetDirectory);
        _commitHistoryTargetDirectory = targetDirectory;

        CommitHistoryListBox.ItemsSource = null;

        try
        {
            var historyResult = await GitService.GetCommitHistoryAsync(targetDirectory).ConfigureAwait(true);
            if (!historyResult.Success)
            {
                ShowCommitHistoryError(historyResult.Message);
                return;
            }

            await BindCommitHistoryListAsync(historyResult.Commits);

            if (historyResult.Commits.Count == 0)
                ShowCommitHistoryError(LocalizationManager.Instance["UI_GitNoCommitsYet"]);
        }
        catch (Exception ex)
        {
            ShowCommitHistoryError(ex.Message);
            StatusText.Text = ex.Message;
        }
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
            var result = await GitService.DeleteCommitAsync(targetDirectory, selectedCommit.Hash)
                .ConfigureAwait(true);

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

    private async void CommitHistoryRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommitHistoryListBox.SelectedItem is not GitCommit selectedCommit ||
            _commitHistoryTargetPane is null ||
            string.IsNullOrWhiteSpace(_commitHistoryTargetDirectory))
        {
            return;
        }

        var loc = LocalizationManager.Instance;
        var pane = _commitHistoryTargetPane;
        var repositoryRoot = Path.GetFullPath(_commitHistoryTargetDirectory);
        var commitHash = selectedCommit.Hash.Trim();
        if (string.IsNullOrWhiteSpace(commitHash))
            return;

        HideCommitHistoryError();

        var confirmMessage = string.Format(
            loc["UI_GitRestoreConfirmMessage"],
            selectedCommit.Hash,
            selectedCommit.Message);

        var confirm = MessageBox.Show(
            confirmMessage,
            loc["UI_GitRestoreConfirmTitle"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        SetCommitHistoryActionsEnabled(false);
        StatusText.Text = loc["UI_GitRestoring"];

        try
        {
            CloseEditor(animate: false);

            var result = await GitService.RestoreToCommitAsync(repositoryRoot, commitHash)
                .ConfigureAwait(true);

            if (!result.Success)
            {
                ShowCommitHistoryError(result.Message);
                StatusText.Text = result.Message;
                SetCommitHistoryActionsEnabled(true);
                return;
            }

            HideCommitHistoryOverlay(animate: false);
            StatusText.Text = loc["UI_GitRestoreSuccess"];
            NavigateToDirectory(
                pane,
                ResolvePanePathAfterGitOperation(pane, repositoryRoot),
                syncTree: pane == _activePane,
                recordHistory: false);
        }
        catch (Exception ex)
        {
            ShowCommitHistoryError(ex.Message);
            StatusText.Text = ex.Message;
            SetCommitHistoryActionsEnabled(true);
        }
    }

    private static string ResolvePanePathAfterGitOperation(DirectoryPaneState pane, string repositoryRoot)
    {
        var currentPath = pane.CurrentPath;
        if (string.IsNullOrWhiteSpace(currentPath))
            return repositoryRoot;

        try
        {
            currentPath = Path.GetFullPath(currentPath);
            if (Directory.Exists(currentPath) && IsPathUnderDirectory(currentPath, repositoryRoot))
                return currentPath;
        }
        catch
        {
            // Fall back to repository root.
        }

        return repositoryRoot;
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

            var segments = relativePath.Split('\\', StringSplitOptions.RemoveEmptyEntries);

            foreach (var segment in segments)
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
        var desktopNode = _driveNodes.FirstOrDefault(node => !node.IsDrive);

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

    private void OpenFileWithDefaultApplication(string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });

            _navigationHistory.RecordFile(filePath);
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

        var jellyOn = SettingsManager.Instance.Current.JellyDragEnabled;
        if (jellyOn)
        {
            SyncJellyFromSettings();
            _jellyDrag.Begin(this);
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // Mouse released before DragMove could capture it.
        }
        finally
        {
            if (jellyOn)
                _jellyDrag.End();
        }
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
            case nameof(AppSettings.CustomThemePath):
                ThemeManager.ApplyTheme(
                    SettingsManager.Instance.Current.Theme,
                    SettingsManager.Instance.Current.CustomThemePath);
                foreach (var pane in _panes)
                {
                    pane.Control.ResetThemeBrushes();
                    pane.Control.SetIsActive(pane == _activePane);
                }
                if (SettingsOverlay.Visibility == Visibility.Visible)
                {
                    UpdateCustomThemePanelVisibility();
                    UpdateThemeDetailsText();
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
            case nameof(AppSettings.FileIconStyle):
            case nameof(AppSettings.CustomIconPackPath):
                ApplyFileIconSettings();
                RefreshAllFileIcons();
                break;
            case nameof(AppSettings.CustomFontPath):
            case nameof(AppSettings.UiFontId):
                ApplyCustomFont();
                break;
            case nameof(AppSettings.OpenRouterApiKey):
            case nameof(AppSettings.AiProvider):
            case nameof(AppSettings.LocalAiEndpoint):
            case nameof(AppSettings.PreferredAiModel):
                ApplyExternalToolAvailability();
                UpdateAiSettingsPanelVisibility();
                break;
            case nameof(AppSettings.JellyDragEnabled):
                ApplyJellyLayout(adjustWindowSize: true);
                _jellyDrag.Reset();
                if (SettingsOverlay.Visibility == Visibility.Visible)
                    UpdateJellyIntensityPanelState();
                break;
            case nameof(AppSettings.JellyIntensity):
                SyncJellyFromSettings();
                break;
        }
    }

    private void SyncJellyFromSettings()
    {
        _jellyDrag.Intensity = SettingsManager.Instance.Current.JellyIntensity;
    }

    /// <summary>
    /// Jelly needs an outer safe zone so transforms and shadow are not clipped.
    /// When jelly is off, the margin is removed and the window shrinks to match visible chrome.
    /// </summary>
    private void ApplyJellyLayout(bool adjustWindowSize = false)
    {
        var enabled = SettingsManager.Instance.Current.JellyDragEnabled;
        WindowShell.Margin = new Thickness(enabled ? JellySafeInset : 0);

        if (!adjustWindowSize || WindowState != WindowState.Normal)
            return;

        var delta = enabled ? JellySafeTotal : -JellySafeTotal;
        Width = Math.Max(MinWidth, Width + delta);
        Height = Math.Max(MinHeight, Height + delta);
    }

    private static double ToStoredWindowSize(double actual, bool jellyEnabled) =>
        jellyEnabled ? actual : actual + JellySafeTotal;

    private double FromStoredWindowWidth(double stored, bool jellyEnabled) =>
        jellyEnabled ? stored : Math.Max(stored - JellySafeTotal, MinWidth);

    private double FromStoredWindowHeight(double stored, bool jellyEnabled) =>
        jellyEnabled ? stored : Math.Max(stored - JellySafeTotal, MinHeight);

    private void ApplyCustomFont()
    {
        var settings = SettingsManager.Instance.Current;
        FontManager.Apply(this, settings.UiFontId, settings.CustomFontPath);
    }

    private void ApplyFileIconSettings()
    {
        var settings = SettingsManager.Instance.Current;
        _iconService.ApplySettings(settings.FileIconStyle, settings.CustomIconPackPath);
    }

    private void RefreshAllFileIcons()
    {
        LoadDriveTree();

        foreach (var pane in _panes)
        {
            if (!string.IsNullOrEmpty(pane.CurrentPath))
                RefreshDirectoryListing(pane);
        }

        _navigationHistory.LoadFromSettings();
        UpdateHistoryEmptyState();
    }

    private void ApplyLocalizedStrings()
    {
        var loc = LocalizationManager.Instance;
        Title = loc["UI_AppTitle"];
        TerminalToggleButton.ToolTip = loc["UI_TerminalToggle"];
        SettingsButton.ToolTip = loc["UI_Settings"];
        SettingsGeneralTabButton.Content = loc["UI_SettingsTabGeneral"];
        SettingsCustomizationTabButton.Content = loc["UI_SettingsTabCustomization"];
        SettingsSupportTabButton.Content = loc["UI_SettingsTabSupport"];
        SettingsTimeFormatLabel.Text = loc["UI_TimeFormat"];
        SettingsLanguageLabel.Text = loc["UI_Language"];
        SettingsThemeLabel.Text = loc["UI_Theme"];
        SettingsUseBuiltInMediaViewerCheckBox.Content = loc["UI_UseBuiltInMediaViewer"];
        SettingsUseBuiltInMediaViewerHint.Text = loc["UI_UseBuiltInMediaViewerHint"];
        SettingsEnableCloseAnimationsCheckBox.Content = loc["UI_EnableCloseAnimations"];
        SettingsEnableCloseAnimationsHint.Text = loc["UI_EnableCloseAnimationsHint"];
        SettingsMinimizeToTrayCheckBox.Content = loc["UI_MinimizeToTray"];
        SettingsMinimizeToTrayHint.Text = loc["UI_MinimizeToTrayHint"];
        SettingsRunAtStartupCheckBox.Content = loc["UI_RunAtStartup"];
        SettingsRunAtStartupHint.Text = loc["UI_RunAtStartupHint"];
        SettingsJellyDragCheckBox.Content = loc["UI_JellyDrag"];
        SettingsJellyDragHint.Text = loc["UI_JellyDragHint"];
        SettingsJellyIntensityLabel.Text = loc["UI_JellyIntensity"];
        SettingsJellyIntensityLiveLabel.Text = loc["UI_JellyIntensityLive"];
        SettingsJellyIntensityLowText.Text = loc["UI_JellyIntensityLow"];
        SettingsJellyIntensityDefaultText.Text = loc["UI_JellyIntensityDefault"];
        SettingsJellyIntensityHighText.Text = loc["UI_JellyIntensityHigh"];
        UpdateJellyIntensityValueText(SettingsJellyIntensitySlider.Value);
        RefreshTrayLocalization();
        SettingsCustomFontLabel.Text = loc["UI_Font"];
        SettingsCustomFontHint.Text = loc["UI_CustomFontHint"];
        SettingsBrowseFontButton.Content = loc["UI_BrowseFont"];
        SettingsClearFontButton.Content = loc["UI_ClearFont"];
        PopulateFontComboBox(loc);
        SettingsIconStyleLabel.Text = loc["UI_IconStyle"];
        SettingsSyncIconPacksButton.Content = loc["UI_SyncIconPacks"];
        SettingsBrowseThemeButton.Content = loc["UI_BrowseTheme"];
        SettingsClearThemeButton.Content = loc["UI_ClearTheme"];
        SettingsExportThemeButton.Content = loc["UI_ExportTheme"];
        SettingsSyncThemesButton.Content = loc["UI_SyncThemes"];
        SettingsCustomThemeHint.Text = loc["UI_CustomThemeHint"];
        SettingsCustomIconPackLabel.Text = loc["UI_CustomIconPack"];
        SettingsCustomIconPackHint.Text = loc["UI_CustomIconPackHint"];
        SettingsBrowseIconPackButton.Content = loc["UI_BrowseIconPack"];
        SettingsClearIconPackButton.Content = loc["UI_ClearIconPack"];
        SettingsEditIconPackButton.Content = loc["UI_EditIconPack"];
        SettingsCustomizationProfileLabel.Text = loc["UI_CustomizationProfile"];
        SettingsCustomizationProfileHint.Text = loc["UI_CustomizationProfileHint"];
        SettingsExportProfileButton.Content = loc["UI_ExportCustomizationProfile"];
        SettingsImportProfileButton.Content = loc["UI_ImportCustomizationProfile"];
        SettingsClearCacheButton.Content = loc["UI_ClearCache"];
        if (IconPackEditorOverlay.Visibility == Visibility.Visible)
            ApplyIconPackEditorLocalizedStrings(loc);
        AddPaneButton.ToolTip = loc["UI_AddPane"];
        BackNavigationButton.ToolTip = loc["UI_Back"];
        ForwardNavigationButton.ToolTip = loc["UI_Forward"];
        HistoryNavigationButton.ToolTip = loc["UI_History"];
        HistoryTitleText.Text = loc["UI_HistoryTitle"];
        HistoryEmptyText.Text = loc["UI_HistoryEmpty"];
        HistoryClearButton.Content = loc["UI_HistoryClear"];
        ApplyExplorerFeatureLocalization(loc);
        ApplyDirectoryTreeLocalization(loc);
        _navigationHistory.LoadFromSettings();
        UpdateHistoryEmptyState();
        SettingsScrollSensitivityLabel.Text = loc["UI_ScrollSensitivity"];
        SettingsScrollSensitivityLiveLabel.Text = loc["UI_ScrollSensitivityLive"];
        SettingsScrollSensitivityHint.Text = loc["UI_ScrollSensitivityHint"];
        SettingsScrollSensitivitySlowText.Text = loc["UI_ScrollSensitivitySlow"];
        SettingsScrollSensitivityDefaultText.Text = loc["UI_ScrollSensitivityDefault"];
        SettingsScrollSensitivityFastText.Text = loc["UI_ScrollSensitivityFast"];
        UpdateScrollSensitivityValueText(SettingsScrollSensitivitySlider.Value);
        SearchingIndicator.Text = loc["UI_Searching"];
        ExtractingIndicator.Text = loc["UI_Extracting"];
        ArchiveFileNameColumn.Header = loc["UI_ColumnFileName"];
        ArchiveOriginalSizeColumn.Header = loc["UI_ColumnOriginalSize"];
        ArchiveCompressedSizeColumn.Header = loc["UI_ColumnCompressedSize"];
        EncryptFolderTitleText.Text = loc["UI_EncryptFolderTitle"];
        EncryptFolderPathLabel.Text = loc["UI_EncryptFolderSource"];
        EncryptFolderArchiveNameLabel.Text = loc["UI_EncryptFolderArchiveName"];
        EncryptFolderMethodLabel.Text = loc["UI_EncryptFolderMethod"];
        EncryptFolderPasswordLabel.Text = loc["UI_EncryptFolderPassword"];
        EncryptFolderConfirmPasswordLabel.Text = loc["UI_EncryptFolderConfirmPassword"];
        EncryptFolderCreateButton.Content = loc["UI_EncryptFolderCreate"];
        ArchivePasswordTitleText.Text = loc["UI_ArchivePasswordTitle"];
        ArchivePasswordHintText.Text = loc["UI_ArchivePasswordHint"];
        ArchivePasswordConfirmButton.Content = loc["UI_ArchivePasswordUnlock"];
        MediaViewerCloseButton.Content = loc["UI_Close"];
        MediaViewerPlayPauseButton.Content = _mediaIsPlaying
            ? loc["UI_MediaPause"]
            : loc["UI_MediaPlay"];
        if (EncryptFolderOverlay.Visibility == Visibility.Visible)
            PopulateEncryptFolderMethodCombo();

        SettingsAiProviderLabel.Text = loc["UI_AiProvider"];
        SettingsAiEndpointLabel.Text = loc["UI_AiEndpoint"];
        SettingsAiApiKeyLabel.Text = loc["UI_AiOpenRouterApiKey"];
        SettingsAiModelLabel.Text = loc["UI_AiPreferredModel"];
        PopulateAiProviderComboBox(loc);
        UpdateAiSettingsPanelVisibility();

        foreach (var pane in _panes)
            pane.Control.ApplyLocalization();
    }

    private void PopulateSettingsControls()
    {
        var suppressSettings = _suppressSettingsUiChange;
        _suppressSettingsUiChange = true;
        _suppressAiApiKeyChange = true;
        try
        {
            var loc = LocalizationManager.Instance;

            SettingsCustomFontPathBox.Text = SettingsManager.Instance.Current.CustomFontPath;
            SelectFontComboBoxItem(SettingsManager.Instance.Current.UiFontId);
            UpdateCustomFontPanelVisibility();

            PopulateThemeComboBox(loc);
            SettingsCustomThemePathBox.Text = SettingsManager.Instance.Current.CustomThemePath;
            UpdateCustomThemePanelVisibility();
            UpdateThemeDetailsText();

            var locales = LocalizationManager.Instance.GetAvailableLocales();
            SettingsLanguageComboBox.ItemsSource = locales;
            var selectedLocale = locales.FirstOrDefault(locale =>
                locale.Code.Equals(
                    SettingsManager.Instance.Current.Language,
                    StringComparison.OrdinalIgnoreCase)) ?? locales.FirstOrDefault();
            SettingsLanguageComboBox.SelectedItem = selectedLocale;

            PopulateIconStyleComboBox(loc);
            SelectIconPackComboBoxItem(
                SettingsManager.Instance.Current.FileIconStyle,
                SettingsManager.Instance.Current.CustomIconPackPath);
            SettingsCustomIconPackPathBox.Text = SettingsManager.Instance.Current.CustomIconPackPath;
            UpdateCustomIconPackPanelVisibility();
            UpdateIconPackDetailsText();

            SettingsTimeFormatCheckBox.IsChecked =
                SettingsManager.Instance.Current.TimeFormat == TimeFormatMode.Hour12;

            SettingsUseBuiltInMediaViewerCheckBox.IsChecked =
                SettingsManager.Instance.Current.UseBuiltInMediaViewer;

            SettingsEnableCloseAnimationsCheckBox.IsChecked =
                SettingsManager.Instance.Current.EnableCloseAnimations;

            SettingsMinimizeToTrayCheckBox.IsChecked =
                SettingsManager.Instance.Current.MinimizeToTray;

            SettingsRunAtStartupCheckBox.IsChecked =
                SettingsManager.Instance.Current.RunAtStartup;

            SettingsJellyDragCheckBox.IsChecked =
                SettingsManager.Instance.Current.JellyDragEnabled;

            SettingsJellyIntensitySlider.Value = Math.Clamp(
                SettingsManager.Instance.Current.JellyIntensity,
                SettingsManager.MinJellyIntensity,
                SettingsManager.MaxJellyIntensity);
            UpdateJellyIntensityValueText(SettingsJellyIntensitySlider.Value);
            UpdateJellyIntensityPanelState();

            PopulateAiProviderComboBox(loc);
            SelectAiProviderComboBoxItem(SettingsManager.Instance.Current.AiProvider);
            SettingsAiEndpointBox.Text = SettingsManager.Instance.Current.LocalAiEndpoint;
            SettingsAiApiKeyBox.Text = SettingsManager.Instance.Current.OpenRouterApiKey;
            SettingsAiModelTextBox.Text = SettingsManager.Instance.Current.PreferredAiModel;
            UpdateAiSettingsPanelVisibility();

            SettingsScrollSensitivitySlider.Value = Math.Clamp(
                SettingsManager.Instance.Current.ScrollSensitivity,
                SettingsManager.MinScrollSensitivity,
                SettingsManager.MaxScrollSensitivity);
            UpdateScrollSensitivityValueText(SettingsScrollSensitivitySlider.Value);
        }
        finally
        {
            _suppressSettingsUiChange = suppressSettings;
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

    private enum SettingsTabKind
    {
        General,
        Customization,
        Support
    }

    private void ShowSettingsOverlay()
    {
        PopulateSettingsControls();
        SelectSettingsTab(SettingsTabKind.General);
        SettingsOverlay.Visibility = Visibility.Visible;
        UiAnimationHelper.ShowOverlay(SettingsPanel);
    }

    private void SelectSettingsTab(SettingsTabKind tab)
    {
        SettingsGeneralPanel.Visibility = tab == SettingsTabKind.General
            ? Visibility.Visible
            : Visibility.Collapsed;
        SettingsCustomizationPanel.Visibility = tab == SettingsTabKind.Customization
            ? Visibility.Visible
            : Visibility.Collapsed;
        SettingsSupportPanel.Visibility = tab == SettingsTabKind.Support
            ? Visibility.Visible
            : Visibility.Collapsed;

        SettingsGeneralTabButton.Tag = tab == SettingsTabKind.General ? "selected" : null;
        SettingsCustomizationTabButton.Tag = tab == SettingsTabKind.Customization ? "selected" : null;
        SettingsSupportTabButton.Tag = tab == SettingsTabKind.Support ? "selected" : null;

        if (tab == SettingsTabKind.Support)
            PopulateSupportPanel();
    }

    private void PopulateSupportPanel()
    {
        var loc = LocalizationManager.Instance;
        var info = AuthorSupportService.GetInfo();

        SettingsSupportIntroText.Text = loc["UI_SupportIntro"];
        SettingsSupportTokenText.Text = loc["UI_SupportToken"];
        SettingsSupportNetworkText.Text = loc["UI_SupportNetwork"];
        SettingsSupportAddressLabel.Text = loc["UI_SupportWalletAddress"];
        SettingsSupportWarningText.Text = loc["UI_SupportNetworkWarning"];
        SettingsSupportThanksText.Text = loc["UI_SupportThanks"];
        SettingsSupportCopyButton.Content = loc["UI_SupportCopyAddress"];
        SettingsSupportOpenBscScanButton.Content = loc["UI_SupportOpenBscScan"];
        SettingsSupportWalletAddressBox.Text = info.UsdtBep20Address.Trim();
        SettingsSupportCopyButton.IsEnabled = true;
        SettingsSupportOpenBscScanButton.IsEnabled = true;
    }

    private void SettingsGeneralTabButton_Click(object sender, RoutedEventArgs e) =>
        SelectSettingsTab(SettingsTabKind.General);

    private void SettingsCustomizationTabButton_Click(object sender, RoutedEventArgs e) =>
        SelectSettingsTab(SettingsTabKind.Customization);

    private void SettingsSupportTabButton_Click(object sender, RoutedEventArgs e) =>
        SelectSettingsTab(SettingsTabKind.Support);

    private void SettingsSupportCopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorSupportService.HasWalletAddress())
            return;

        var address = AuthorSupportService.GetInfo().UsdtBep20Address.Trim();
        Clipboard.SetText(address);

        var loc = LocalizationManager.Instance;
        MessageBox.Show(
            loc["UI_SupportCopySuccess"],
            loc["UI_SettingsTabSupport"],
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void SettingsSupportOpenBscScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (!AuthorSupportService.HasWalletAddress())
            return;

        var address = AuthorSupportService.GetInfo().UsdtBep20Address.Trim();
        var url = AuthorSupportService.GetBscScanAddressUrl(address);

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                LocalizationManager.Instance["UI_SupportOpenBscScan"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void HideSettingsOverlay()
    {
        if (SettingsOverlay.Visibility != Visibility.Visible)
            return;

        CommitScrollSensitivitySetting();
        CommitJellyIntensitySetting();

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
        if (_suppressSettingsUiChange || SettingsThemeComboBox.SelectedItem is not UiThemeComboItem item)
            return;

        var path = item.JsonPath ?? (item.RequiresManualPath
            ? SettingsManager.Instance.Current.CustomThemePath
            : string.Empty);

        SettingsManager.Instance.ApplyThemeSelection(item.Theme, path);
        SettingsCustomThemePathBox.Text = SettingsManager.Instance.Current.CustomThemePath;
        UpdateCustomThemePanelVisibility();
        UpdateThemeDetailsText();
    }

    private void PopulateThemeComboBox(LocalizationManager loc)
    {
        var settings = SettingsManager.Instance.Current;
        SettingsThemeComboBox.Items.Clear();

        foreach (var theme in ThemeCatalog.GetBuiltInThemes())
        {
            if (theme == AppTheme.Custom)
                continue;

            SettingsThemeComboBox.Items.Add(new UiThemeComboItem
            {
                Theme = theme,
                Label = loc[ThemeCatalog.GetLabelKey(theme)]
            });
        }

        foreach (var imported in BuiltInThemeCatalog.GetImportedThemeOptions())
            SettingsThemeComboBox.Items.Add(imported);

        SettingsThemeComboBox.Items.Add(new UiThemeComboItem
        {
            Theme = AppTheme.Custom,
            RequiresManualPath = true,
            Label = loc["UI_ThemeCustom"],
            Description = loc["UI_CustomThemeHint"]
        });

        SettingsThemeComboBox.DisplayMemberPath = nameof(UiThemeComboItem.Label);
        SelectThemeComboBoxItem(settings.Theme, settings.CustomThemePath);
    }

    private void SelectThemeComboBoxItem(AppTheme theme, string? customThemePath)
    {
        var normalizedPath = customThemePath?.Trim() ?? string.Empty;

        for (var index = 0; index < SettingsThemeComboBox.Items.Count; index++)
        {
            if (SettingsThemeComboBox.Items[index] is not UiThemeComboItem item)
                continue;

            if (theme == AppTheme.Custom && !string.IsNullOrWhiteSpace(normalizedPath))
            {
                if (!string.IsNullOrWhiteSpace(item.JsonPath) &&
                    string.Equals(item.JsonPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                {
                    SettingsThemeComboBox.SelectedIndex = index;
                    return;
                }

                if (item.RequiresManualPath)
                {
                    SettingsThemeComboBox.SelectedIndex = index;
                    return;
                }

                continue;
            }

            if (item.Theme == theme && string.IsNullOrWhiteSpace(item.JsonPath) && !item.RequiresManualPath)
            {
                SettingsThemeComboBox.SelectedIndex = index;
                return;
            }
        }

        if (SettingsThemeComboBox.Items.Count > 0)
            SettingsThemeComboBox.SelectedIndex = 0;
    }

    private void UpdateCustomThemePanelVisibility()
    {
        var show = SettingsThemeComboBox.SelectedItem is UiThemeComboItem item &&
                   item.Theme == AppTheme.Custom;
        SettingsCustomThemePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateThemeDetailsText()
    {
        if (SettingsThemeComboBox.SelectedItem is UiThemeComboItem item &&
            !string.IsNullOrWhiteSpace(item.Description))
        {
            SettingsThemeDetailsText.Text = item.Description;
            SettingsThemeDetailsText.Visibility = Visibility.Visible;
            return;
        }

        if (SettingsThemeComboBox.SelectedItem is UiThemeComboItem customItem &&
            customItem.Theme == AppTheme.Custom &&
            !string.IsNullOrWhiteSpace(SettingsCustomThemePathBox.Text))
        {
            var manifest = CustomThemeLoader.ReadManifest(SettingsCustomThemePathBox.Text);
            SettingsThemeDetailsText.Text = BuiltInThemeCatalog.BuildDescription(manifest);
            SettingsThemeDetailsText.Visibility = Visibility.Visible;
            return;
        }

        SettingsThemeDetailsText.Text = string.Empty;
        SettingsThemeDetailsText.Visibility = Visibility.Collapsed;
    }

    private void SettingsBrowseThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = LocalizationManager.Instance["UI_BrowseTheme"],
            Filter = "Theme JSON|*.json|All files|*.*",
            CheckFileExists = true
        };

        var currentPath = SettingsManager.Instance.Current.CustomThemePath;
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            var directory = Path.GetDirectoryName(currentPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                dialog.InitialDirectory = directory;
        }
        else if (Directory.Exists(PackSyncService.UserThemesRoot))
        {
            dialog.InitialDirectory = PackSyncService.UserThemesRoot;
        }

        if (dialog.ShowDialog() != true)
            return;

        ApplyCustomThemeFile(dialog.FileName);
    }

    private void SettingsClearThemeButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsCustomThemePathBox.Text = string.Empty;
        SettingsManager.Instance.ApplyThemeSelection(AppTheme.Dark, string.Empty);
        _suppressSettingsUiChange = true;
        try
        {
            SelectThemeComboBoxItem(AppTheme.Dark, string.Empty);
            UpdateCustomThemePanelVisibility();
            UpdateThemeDetailsText();
        }
        finally
        {
            _suppressSettingsUiChange = false;
        }
    }

    private void SettingsExportThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        Directory.CreateDirectory(PackSyncService.UserThemesRoot);
        var defaultName = $"my-theme-{DateTime.Now:yyyyMMdd-HHmm}.json";
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = loc["UI_ExportTheme"],
            Filter = "Theme JSON|*.json",
            FileName = defaultName,
            InitialDirectory = PackSyncService.UserThemesRoot
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            CustomThemeLoader.ExportCurrent(dialog.FileName, loc["UI_ThemeCustomExportName"]);
            ApplyCustomThemeFile(dialog.FileName);
            PopulateThemeComboBox(loc);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, loc["UI_ExportTheme"], MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void SettingsSyncThemesButton_Click(object sender, RoutedEventArgs e)
    {
        var result = PackSyncService.SyncBuiltInThemes();
        var loc = LocalizationManager.Instance;
        MessageBox.Show(
            FormatSyncResult(loc["UI_SyncThemes"], result),
            loc["UI_SyncThemes"],
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        PopulateThemeComboBox(loc);
    }

    private void ApplyCustomThemeFile(string filePath)
    {
        SettingsCustomThemePathBox.Text = filePath;
        SettingsManager.Instance.ApplyThemeSelection(AppTheme.Custom, filePath);
        _suppressSettingsUiChange = true;
        try
        {
            SelectThemeComboBoxItem(AppTheme.Custom, filePath);
            UpdateCustomThemePanelVisibility();
            UpdateThemeDetailsText();
        }
        finally
        {
            _suppressSettingsUiChange = false;
        }
    }

    private void PopulateIconStyleComboBox(LocalizationManager loc)
    {
        SettingsIconStyleComboBox.Items.Clear();

        foreach (var option in BuiltInIconPackCatalog.GetStyleOptions(loc))
            SettingsIconStyleComboBox.Items.Add(option);

        foreach (var pack in BuiltInIconPackCatalog.GetBuiltInPackOptions())
            SettingsIconStyleComboBox.Items.Add(pack);

        SettingsIconStyleComboBox.Items.Add(new UiIconPackOption
        {
            Style = FileIconStyle.Custom,
            RequiresManualPath = true,
            Label = loc["UI_IconStyleCustom"],
            Description = loc["UI_IconStyleCustomHint"]
        });

        SettingsIconStyleComboBox.DisplayMemberPath = nameof(UiIconPackOption.Label);
    }

    private void SelectIconPackComboBoxItem(FileIconStyle style, string? packPath)
    {
        var normalizedPath = packPath?.Trim() ?? string.Empty;

        for (var index = 0; index < SettingsIconStyleComboBox.Items.Count; index++)
        {
            if (SettingsIconStyleComboBox.Items[index] is not UiIconPackOption item)
                continue;

            if (style == FileIconStyle.Custom && !string.IsNullOrWhiteSpace(normalizedPath))
            {
                if (!string.IsNullOrWhiteSpace(item.PackFolderPath) &&
                    string.Equals(item.PackFolderPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                {
                    SettingsIconStyleComboBox.SelectedIndex = index;
                    return;
                }

                if (item.RequiresManualPath)
                {
                    SettingsIconStyleComboBox.SelectedIndex = index;
                    return;
                }

                continue;
            }

            if (item.Style == style && string.IsNullOrWhiteSpace(item.PackFolderPath) && !item.RequiresManualPath)
            {
                SettingsIconStyleComboBox.SelectedIndex = index;
                return;
            }
        }

        if (SettingsIconStyleComboBox.Items.Count > 0)
            SettingsIconStyleComboBox.SelectedIndex = 0;
    }

    private void SettingsIconStyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsUiChange || SettingsIconStyleComboBox.SelectedItem is not UiIconPackOption item)
            return;

        if (item.RequiresManualPath)
        {
            UpdateCustomIconPackPanelVisibility();
            UpdateIconPackDetailsText();
            return;
        }

        if (!string.IsNullOrWhiteSpace(item.PackFolderPath))
        {
            SettingsCustomIconPackPathBox.Text = item.PackFolderPath;
            SettingsManager.Instance.UpdateCustomIconPackPath(item.PackFolderPath);
            SettingsManager.Instance.UpdateFileIconStyle(FileIconStyle.Custom);
        }
        else
        {
            SettingsCustomIconPackPathBox.Text = string.Empty;
            SettingsManager.Instance.UpdateCustomIconPackPath(string.Empty);
            SettingsManager.Instance.UpdateFileIconStyle(item.Style);
        }

        UpdateCustomIconPackPanelVisibility();
        UpdateIconPackDetailsText();
    }

    private void UpdateCustomIconPackPanelVisibility()
    {
        var show = SettingsIconStyleComboBox.SelectedItem is UiIconPackOption item &&
                   item.Style == FileIconStyle.Custom &&
                   item.RequiresManualPath;
        SettingsCustomIconPackPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateIconPackDetailsText()
    {
        if (SettingsIconStyleComboBox.SelectedItem is UiIconPackOption item &&
            !string.IsNullOrWhiteSpace(item.Description))
        {
            SettingsIconPackDetailsText.Text = item.Description;
            SettingsIconPackDetailsText.Visibility = Visibility.Visible;
            return;
        }

        var path = SettingsManager.Instance.Current.CustomIconPackPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            var info = BuiltInIconPackCatalog.InspectFolder(path);
            if (info is not null)
            {
                SettingsIconPackDetailsText.Text = BuiltInIconPackCatalog.BuildPackDescription(info);
                SettingsIconPackDetailsText.Visibility = Visibility.Visible;
                return;
            }
        }

        SettingsIconPackDetailsText.Text = string.Empty;
        SettingsIconPackDetailsText.Visibility = Visibility.Collapsed;
    }

    private void SettingsSyncIconPacksButton_Click(object sender, RoutedEventArgs e)
    {
        var result = PackSyncService.SyncBuiltInIconPacks();
        var loc = LocalizationManager.Instance;
        MessageBox.Show(
            FormatSyncResult(loc["UI_SyncIconPacks"], result),
            loc["UI_SyncIconPacks"],
            MessageBoxButton.OK,
            MessageBoxImage.Information);

        var settings = SettingsManager.Instance.Current;
        PopulateIconStyleComboBox(loc);
        SelectIconPackComboBoxItem(settings.FileIconStyle, settings.CustomIconPackPath);
        UpdateIconPackDetailsText();
    }

    private static string FormatSyncResult(string title, PackSyncResult result)
    {
        var lines = new List<string>
        {
            $"{title}",
            $"Updated: {result.Updated}",
            $"Skipped: {result.Skipped}"
        };

        if (result.Messages.Count > 0)
            lines.AddRange(result.Messages.Select(message => $"• {message}"));

        return string.Join(Environment.NewLine, lines);
    }

    private void SettingsBrowseIconPackButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = LocalizationManager.Instance["UI_BrowseIconPack"]
        };

        var currentPath = SettingsManager.Instance.Current.CustomIconPackPath;
        if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
            dialog.InitialDirectory = currentPath;

        if (dialog.ShowDialog() != true)
            return;

        SettingsCustomIconPackPathBox.Text = dialog.FolderName;
        _suppressSettingsUiChange = true;
        try
        {
            SelectIconPackComboBoxItem(FileIconStyle.Custom, dialog.FolderName);
            UpdateCustomIconPackPanelVisibility();
        }
        finally
        {
            _suppressSettingsUiChange = false;
        }

        SettingsManager.Instance.UpdateCustomIconPackPath(dialog.FolderName);
        if (SettingsManager.Instance.Current.FileIconStyle != FileIconStyle.Custom)
            SettingsManager.Instance.UpdateFileIconStyle(FileIconStyle.Custom);
        UpdateIconPackDetailsText();
    }

    private void SettingsClearIconPackButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsCustomIconPackPathBox.Text = string.Empty;
        SettingsManager.Instance.UpdateCustomIconPackPath(string.Empty);
        UpdateIconPackDetailsText();
    }

    private void SettingsBrowseFontButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = LocalizationManager.Instance["UI_BrowseFont"],
            Filter = "Fonts|*.ttf;*.otf;*.ttc|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        var currentPath = SettingsManager.Instance.Current.CustomFontPath;
        if (!string.IsNullOrWhiteSpace(currentPath))
        {
            var directory = Path.GetDirectoryName(currentPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                dialog.InitialDirectory = directory;
        }

        if (dialog.ShowDialog() != true)
            return;

        if (!FontManager.TryImportFont(dialog.FileName, out var storedPath))
        {
            MessageBox.Show(
                LocalizationManager.Instance["UI_CustomFontLoadFailed"],
                LocalizationManager.Instance["UI_SettingsTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        SettingsCustomFontPathBox.Text = storedPath;
        _suppressSettingsUiChange = true;
        try
        {
            SelectFontComboBoxItem(UiFontIds.Custom);
            UpdateCustomFontPanelVisibility();
        }
        finally
        {
            _suppressSettingsUiChange = false;
        }

        SettingsManager.Instance.UpdateCustomFontPath(storedPath);
        SettingsManager.Instance.UpdateUiFontId(UiFontIds.Custom);
    }

    private void SettingsClearFontButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsCustomFontPathBox.Text = string.Empty;
        _suppressSettingsUiChange = true;
        try
        {
            SelectFontComboBoxItem(UiFontIds.Default);
            UpdateCustomFontPanelVisibility();
        }
        finally
        {
            _suppressSettingsUiChange = false;
        }

        SettingsManager.Instance.UpdateCustomFontPath(string.Empty);
        SettingsManager.Instance.UpdateUiFontId(UiFontIds.Default);
    }

    private void SettingsFontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsUiChange)
            return;

        if (SettingsFontComboBox.SelectedItem is not UiFontComboItem item)
            return;

        UpdateCustomFontPanelVisibility();
        SettingsManager.Instance.UpdateUiFontId(item.Id);
    }

    private void PopulateFontComboBox(LocalizationManager loc)
    {
        var selectedId = SettingsFontComboBox.SelectedItem is UiFontComboItem current
            ? current.Id
            : SettingsManager.Instance.Current.UiFontId;

        var suppress = _suppressSettingsUiChange;
        _suppressSettingsUiChange = true;
        try
        {
            SettingsFontComboBox.Items.Clear();
            foreach (var option in BuiltInFontCatalog.GetOptions())
            {
                SettingsFontComboBox.Items.Add(new UiFontComboItem
                {
                    Id = option.Id,
                    Label = loc[option.LabelKey]
                });
            }

            SettingsFontComboBox.DisplayMemberPath = nameof(UiFontComboItem.Label);
            SelectFontComboBoxItem(selectedId);
            UpdateCustomFontPanelVisibility();
        }
        finally
        {
            _suppressSettingsUiChange = suppress;
        }
    }

    private void SelectFontComboBoxItem(string fontId)
    {
        var normalized = BuiltInFontCatalog.NormalizeFontId(fontId, SettingsManager.Instance.Current.CustomFontPath);
        for (var index = 0; index < SettingsFontComboBox.Items.Count; index++)
        {
            if (SettingsFontComboBox.Items[index] is UiFontComboItem item &&
                item.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                SettingsFontComboBox.SelectedIndex = index;
                return;
            }
        }

        SettingsFontComboBox.SelectedIndex = 0;
    }

    private void UpdateCustomFontPanelVisibility()
    {
        var isCustom = SettingsFontComboBox.SelectedItem is UiFontComboItem item &&
                       item.Id.Equals(UiFontIds.Custom, StringComparison.OrdinalIgnoreCase);
        SettingsCustomFontPanel.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
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

    private void SettingsUseBuiltInMediaViewerCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsUiChange)
            return;

        var enabled = SettingsUseBuiltInMediaViewerCheckBox.IsChecked == true;
        SettingsManager.Instance.UpdateUseBuiltInMediaViewer(enabled);

        if (!enabled)
            CloseMediaViewer();
    }

    private void SettingsEnableCloseAnimationsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsUiChange)
            return;

        var enabled = SettingsEnableCloseAnimationsCheckBox.IsChecked == true;
        SettingsManager.Instance.UpdateEnableCloseAnimations(enabled);
    }

    private void SettingsMinimizeToTrayCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsUiChange)
            return;

        var enabled = SettingsMinimizeToTrayCheckBox.IsChecked == true;
        SettingsManager.Instance.UpdateMinimizeToTray(enabled);
        ApplyTrayPreference(enabled);
    }

    private void SettingsRunAtStartupCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsUiChange)
            return;

        var enabled = SettingsRunAtStartupCheckBox.IsChecked == true;
        if (!StartupManager.SetEnabled(enabled))
        {
            // Roll back the checkbox if the registry update failed.
            _suppressSettingsUiChange = true;
            SettingsRunAtStartupCheckBox.IsChecked = StartupManager.IsEnabled();
            _suppressSettingsUiChange = false;
            return;
        }

        SettingsManager.Instance.UpdateRunAtStartup(enabled);
    }

    private void SettingsJellyDragCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsUiChange)
            return;

        var enabled = SettingsJellyDragCheckBox.IsChecked == true;
        SettingsManager.Instance.UpdateJellyDragEnabled(enabled);
        UpdateJellyIntensityPanelState();
    }

    private void SettingsJellyIntensitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateJellyIntensityValueText(e.NewValue);

        if (_suppressSettingsUiChange || !IsLoaded)
            return;

        SettingsManager.Instance.UpdateJellyIntensity(e.NewValue);
    }

    private void SettingsJellyIntensitySlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Slider slider)
            return;

        e.Handled = true;
        var delta = e.Delta > 0 ? slider.SmallChange : -slider.SmallChange;
        slider.Value = Math.Clamp(slider.Value + delta, slider.Minimum, slider.Maximum);
    }

    private void CommitJellyIntensitySetting()
    {
        SettingsManager.Instance.UpdateJellyIntensity(SettingsJellyIntensitySlider.Value);
    }

    private void UpdateJellyIntensityValueText(double intensity)
    {
        intensity = Math.Clamp(
            intensity,
            SettingsManager.MinJellyIntensity,
            SettingsManager.MaxJellyIntensity);

        var percent = intensity * 100.0;
        SettingsJellyIntensityValueText.Text = Math.Abs(percent - 100.0) < 0.05
            ? "100%"
            : $"{percent:0.#}%";
    }

    private void UpdateJellyIntensityPanelState()
    {
        var enabled = SettingsJellyDragCheckBox.IsChecked == true;
        SettingsJellyIntensityPanel.IsEnabled = enabled;
        SettingsJellyIntensityPanel.Opacity = enabled ? 1.0 : 0.45;
    }

    private void SettingsLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsUiChange || SettingsLanguageComboBox.SelectedItem is not LocaleInfo locale)
            return;

        SettingsManager.Instance.UpdateLanguage(locale.Code);
    }

    private void SettingsScrollSensitivitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateScrollSensitivityValueText(e.NewValue);

        if (_suppressSettingsUiChange || !IsLoaded)
            return;

        SettingsManager.Instance.UpdateScrollSensitivity(e.NewValue);
    }

    private void SettingsScrollSensitivitySlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        e.Handled = true;

        if (sender is not Slider slider)
            return;

        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.01 : 0.05;
        var delta = e.Delta > 0 ? step : -step;
        slider.Value = Math.Clamp(
            slider.Value + delta,
            slider.Minimum,
            slider.Maximum);
    }

    private void CommitScrollSensitivitySetting()
    {
        SettingsManager.Instance.UpdateScrollSensitivity(SettingsScrollSensitivitySlider.Value);
    }

    private void UpdateScrollSensitivityValueText(double sensitivity)
    {
        sensitivity = Math.Clamp(
            sensitivity,
            SettingsManager.MinScrollSensitivity,
            SettingsManager.MaxScrollSensitivity);

        var percent = sensitivity * 100.0;
        SettingsScrollSensitivityValueText.Text = Math.Abs(percent - 100.0) < 0.05
            ? "100%"
            : $"{percent:0.#}%";
    }

    private void PopulateAiProviderComboBox(LocalizationManager loc)
    {
        var currentProvider = SettingsManager.Instance.Current.AiProvider;
        SettingsAiProviderComboBox.Items.Clear();

        foreach (AiProvider provider in Enum.GetValues<AiProvider>())
        {
            SettingsAiProviderComboBox.Items.Add(new UiAiProviderOption
            {
                Provider = provider,
                Label = loc[AiProviderCatalog.GetLabelKey(provider)]
            });
        }

        SelectAiProviderComboBoxItem(currentProvider);
    }

    private void SelectAiProviderComboBoxItem(AiProvider provider)
    {
        provider = AiProviderCatalog.Normalize(provider);

        for (var index = 0; index < SettingsAiProviderComboBox.Items.Count; index++)
        {
            if (SettingsAiProviderComboBox.Items[index] is UiAiProviderOption item &&
                item.Provider == provider)
            {
                SettingsAiProviderComboBox.SelectedIndex = index;
                return;
            }
        }

        if (SettingsAiProviderComboBox.Items.Count > 0)
            SettingsAiProviderComboBox.SelectedIndex = 0;
    }

    private void UpdateAiSettingsPanelVisibility()
    {
        var settings = AiProviderCatalog.NormalizeSettings(SettingsManager.Instance.Current);
        var loc = LocalizationManager.Instance;
        var isOpenRouter = settings.AiProvider == AiProvider.OpenRouter;
        var isLocal = settings.AiProvider is AiProvider.Ollama or AiProvider.LmStudio;

        SettingsAiApiKeyPanel.Visibility = isOpenRouter ? Visibility.Visible : Visibility.Collapsed;
        SettingsAiEndpointPanel.Visibility = isLocal ? Visibility.Visible : Visibility.Collapsed;

        SettingsAiApiKeyHint.Text = loc["UI_AiApiKeyHint"];
        SettingsAiModelHint.Text = loc[AiProviderCatalog.GetModelHintKey(settings.AiProvider)];

        if (isLocal)
        {
            SettingsAiEndpointHint.Text = string.Format(
                loc["UI_AiEndpointDefaultHint"],
                AiProviderCatalog.GetDefaultEndpoint(settings.AiProvider));
            SettingsAiEndpointHint.Visibility = Visibility.Visible;
        }
        else
        {
            SettingsAiEndpointHint.Text = string.Empty;
            SettingsAiEndpointHint.Visibility = Visibility.Collapsed;
        }
    }

    private void SettingsAiProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettingsUiChange || SettingsAiProviderComboBox.SelectedItem is not UiAiProviderOption item)
            return;

        SettingsManager.Instance.UpdateAiProvider(item.Provider);

        _suppressSettingsUiChange = true;
        try
        {
            SettingsAiEndpointBox.Text = SettingsManager.Instance.Current.LocalAiEndpoint;
            SettingsAiModelTextBox.Text = SettingsManager.Instance.Current.PreferredAiModel;
            UpdateAiSettingsPanelVisibility();
        }
        finally
        {
            _suppressSettingsUiChange = false;
        }
    }

    private void SettingsAiEndpointBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_suppressSettingsUiChange)
            return;

        SettingsManager.Instance.UpdateLocalAiEndpoint(SettingsAiEndpointBox.Text);
        SettingsAiEndpointBox.Text = SettingsManager.Instance.Current.LocalAiEndpoint;
        UpdateAiSettingsPanelVisibility();
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

        var settings = SettingsManager.Instance.Current;
        var model = SettingsAiModelTextBox.Text.Trim();
        if (string.IsNullOrEmpty(model))
            model = AiProviderCatalog.GetDefaultModel(settings.AiProvider);

        SettingsManager.Instance.UpdatePreferredAiModel(model);
        SettingsAiModelTextBox.Text = SettingsManager.Instance.Current.PreferredAiModel;
        UpdateAiSettingsPanelVisibility();
    }

    private void TerminalToggleButton_Click(object sender, RoutedEventArgs e) =>
        ToggleTerminalPanel();

    private void ToggleTerminalPanel()
    {
        if (!ExternalToolsService.IsAvailable(ExternalTool.PowerShell))
            return;

        if (_terminalAnimationInProgress)
            return;

        if (_isTerminalVisible)
        {
            HideTerminalWithAnimation();
            return;
        }

        ShowTerminalWithAnimation();
    }

    private void ShowTerminalFromSettings()
    {
        var height = SavedTerminalPanelHeight;
        _isTerminalVisible = true;

        TerminalSplitter.Visibility = Visibility.Visible;
        TerminalSplitter.Opacity = 1;
        PowerShellTerminal.Visibility = Visibility.Visible;
        PowerShellTerminal.Opacity = 1;

        TerminalPanelRow.MinHeight = SettingsManager.MinTerminalPanelHeight;
        TerminalPanelRow.Height = new GridLength(height);
        TerminalSplitterRow.MinHeight = TerminalSplitterHeight;
        TerminalSplitterRow.Height = new GridLength(TerminalSplitterHeight);

        var path = ActivePane.CurrentPath;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        PowerShellTerminal.StartSession(path);
        PowerShellTerminal.FocusInput();
    }

    private void ShowTerminalWithAnimation()
    {
        _terminalAnimationInProgress = true;
        TerminalToggleButton.IsEnabled = false;
        _isTerminalVisible = true;

        TerminalSplitter.Visibility = Visibility.Visible;
        TerminalSplitter.Opacity = 1;
        PowerShellTerminal.Visibility = Visibility.Visible;

        TerminalSplitterRow.Height = new GridLength(0);
        TerminalSplitterRow.MinHeight = 0;
        TerminalPanelRow.Height = new GridLength(0);
        TerminalPanelRow.MinHeight = 0;

        var path = ActivePane.CurrentPath;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!PowerShellTerminal.IsRunning)
            PowerShellTerminal.StartSession(path);
        else
            PowerShellTerminal.SyncWorkingDirectory(path);

        var targetHeight = SavedTerminalPanelHeight;

        var stepComplete = UiAnimationHelper.CreateParallelCallback(2, () =>
        {
            TerminalPanelRow.MinHeight = SettingsManager.MinTerminalPanelHeight;
            TerminalPanelRow.Height = new GridLength(targetHeight);
            TerminalSplitterRow.MinHeight = TerminalSplitterHeight;
            TerminalSplitterRow.Height = new GridLength(TerminalSplitterHeight);
            SettingsManager.Instance.UpdateTerminalPreferences(targetHeight, isOpen: true);
            _terminalAnimationInProgress = false;
            TerminalToggleButton.IsEnabled = true;
            PowerShellTerminal.FocusInput();
        });

        UiAnimationHelper.AnimateTerminalEntrance(PowerShellTerminal, stepComplete);
        UiAnimationHelper.AnimateTerminalLayout(
            TerminalSplitterRow, 0, TerminalSplitterHeight,
            TerminalPanelRow, 0, targetHeight,
            fadeIn: true,
            stepComplete);
    }

    private void HideTerminalWithAnimation()
    {
        _terminalAnimationInProgress = true;
        TerminalToggleButton.IsEnabled = false;
        PowerShellTerminal.BeginStop();

        var panelHeight = TerminalPanelRow.ActualHeight > 0
            ? TerminalPanelRow.ActualHeight
            : SavedTerminalPanelHeight;
        var splitterHeight = TerminalSplitterRow.ActualHeight > 0
            ? TerminalSplitterRow.ActualHeight
            : TerminalSplitterHeight;

        TerminalPanelRow.MinHeight = 0;
        TerminalSplitterRow.MinHeight = 0;

        var stepComplete = UiAnimationHelper.CreateParallelCallback(2, () =>
        {
            SettingsManager.Instance.UpdateTerminalPreferences(panelHeight, isOpen: false);
            FinishHideTerminal(animate: false, waitForProcessStop: false);
            _terminalAnimationInProgress = false;
            TerminalToggleButton.IsEnabled = true;
        });

        UiAnimationHelper.AnimateTerminalExit(PowerShellTerminal, stepComplete);
        UiAnimationHelper.AnimateTerminalLayout(
            TerminalSplitterRow, splitterHeight, 0,
            TerminalPanelRow, panelHeight, 0,
            fadeIn: false,
            stepComplete);
    }

    private void FinishHideTerminal(bool animate, bool waitForProcessStop = false)
    {
        if (waitForProcessStop)
            PowerShellTerminal.Stop();

        TerminalSplitter.Visibility = Visibility.Collapsed;
        TerminalSplitter.Opacity = 1;
        PowerShellTerminal.Visibility = Visibility.Collapsed;
        PowerShellTerminal.ClearValue(UIElement.OpacityProperty);
        TerminalSplitterRow.Height = new GridLength(0);
        TerminalSplitterRow.MinHeight = 0;
        TerminalPanelRow.Height = new GridLength(0);
        TerminalPanelRow.MinHeight = 0;
        _isTerminalVisible = false;

        if (!animate)
            MainChromeHost.UpdateLayout();
        else
            Dispatcher.BeginInvoke(MainChromeHost.UpdateLayout);
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
