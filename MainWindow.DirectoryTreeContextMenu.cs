using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer;

public partial class MainWindow
{
    private DirectoryTreeNode? _directoryTreeMenuTarget;

    private void ApplyDirectoryTreeLocalization(LocalizationManager loc)
    {
        DirectoryTreeOpenMenuItem.Header = loc["UI_TreeOpen"];
        DirectoryTreeExpandMenuItem.Header = loc["UI_TreeExpand"];
        DirectoryTreeReloadMenuItem.Header = loc["UI_TreeReloadFolder"];
        DirectoryTreeNewFolderMenuItem.Header = loc["UI_NewFolder"];
        DirectoryTreeNewTextFileMenuItem.Header = loc["UI_NewTextFile"];
        DirectoryTreeOpenInOtherPaneMenuItem.Header = loc["UI_OpenInOtherPane"];
        DirectoryTreeCompareFoldersMenuItem.Header = loc["UI_CompareFolders"];
        DirectoryTreeAddBookmarkMenuItem.Header = loc["UI_AddBookmark"];
        DirectoryTreeRemoveBookmarkMenuItem.Header = loc["UI_RemoveBookmark"];
        DirectoryTreeEncryptFolderMenuItem.Header = loc["UI_EncryptFolder"];
        DirectoryTreeRenameMenuItem.Header = loc["UI_Rename"];
        DirectoryTreeDeleteMenuItem.Header = loc["UI_Delete"];
        DirectoryTreeAiExecuteQueryMenuItem.Header = loc["UI_AiExecuteQuery"];
        DirectoryTreeGitInitMenuItem.Header = loc["UI_GitInit"];
        DirectoryTreeGitCommitMenuItem.Header = loc["UI_GitCommitAll"];
        DirectoryTreeGitAmendMenuItem.Header = loc["UI_GitAmendLastCommit"];
        DirectoryTreeGitHistoryMenuItem.Header = loc["UI_GitVersionHistory"];
        DirectoryTreeShowWindowsMenuMenuItem.Header = loc["UI_ShowWindowsMenu"];
        DirectoryTreeEjectMenuItem.Header = loc["UI_TreeSafelyEject"];
        DirectoryTreeDrivePropertiesMenuItem.Header = loc["UI_TreeDriveProperties"];
        DirectoryTreeRefreshDrivesMenuItem.Header = loc["UI_TreeRefreshDrives"];
    }

    private void DirectoryTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var node = GetDirectoryTreeNodeUnderMouse(e.OriginalSource as DependencyObject);
        if (node is null || string.IsNullOrEmpty(node.FullPath))
            return;

        _directoryTreeMenuTarget = node;
        SelectTreeNode(node);

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            return;

        e.Handled = true;
        var targetNode = node;
        Dispatcher.BeginInvoke(
            () => ShowDirectoryTreeNativeContextMenu(targetNode),
            DispatcherPriority.ApplicationIdle);
    }

    private void DirectoryTree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            e.Handled = true;
            return;
        }

        _directoryTreeMenuTarget =
            GetDirectoryTreeNodeUnderMouse(e.OriginalSource as DependencyObject) ??
            _directoryTreeMenuTarget;
    }

    private void DirectoryTreeContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var node = _directoryTreeMenuTarget;
        var hasTarget = node is not null && !string.IsNullOrEmpty(node.FullPath);

        DirectoryTreeOpenMenuItem.IsEnabled = hasTarget;

        if (hasTarget && FindTreeViewItem(DirectoryTree, node!) is { } container)
        {
            DirectoryTreeExpandMenuItem.IsEnabled = !node!.HasDummyChild;
            DirectoryTreeExpandMenuItem.Visibility = node.HasDummyChild
                ? Visibility.Collapsed
                : Visibility.Visible;
            DirectoryTreeExpandMenuItem.Header = container.IsExpanded
                ? LocalizationManager.Instance["UI_TreeCollapse"]
                : LocalizationManager.Instance["UI_TreeExpand"];
        }
        else
        {
            DirectoryTreeExpandMenuItem.IsEnabled = false;
            DirectoryTreeExpandMenuItem.Visibility = Visibility.Collapsed;
        }

        var showReload = hasTarget && node is { IsDrive: false };
        DirectoryTreeReloadMenuItem.Visibility = showReload ? Visibility.Visible : Visibility.Collapsed;
        DirectoryTreeReloadMenuItem.IsEnabled = showReload;

        var showFolderActions = hasTarget && node is { IsDrive: false } && Directory.Exists(node!.FullPath);
        var folderActionsVisibility = showFolderActions ? Visibility.Visible : Visibility.Collapsed;

        DirectoryTreeFolderActionsSeparator.Visibility = folderActionsVisibility;
        DirectoryTreeNewFolderMenuItem.Visibility = folderActionsVisibility;
        DirectoryTreeNewTextFileMenuItem.Visibility = folderActionsVisibility;
        DirectoryTreePaneActionsSeparator.Visibility = folderActionsVisibility;
        DirectoryTreeOpenInOtherPaneMenuItem.Visibility = folderActionsVisibility;
        DirectoryTreeCompareFoldersMenuItem.Visibility = folderActionsVisibility;
        DirectoryTreeAddBookmarkMenuItem.Visibility = folderActionsVisibility;
        DirectoryTreeRemoveBookmarkMenuItem.Visibility = folderActionsVisibility;
        DirectoryTreeItemActionsSeparator.Visibility = folderActionsVisibility;
        DirectoryTreeEncryptFolderMenuItem.Visibility = folderActionsVisibility;
        DirectoryTreeRenameMenuItem.Visibility = folderActionsVisibility;
        DirectoryTreeDeleteMenuItem.Visibility = folderActionsVisibility;
        DirectoryTreeShowWindowsMenuMenuItem.Visibility = folderActionsVisibility;

        var aiAvailable = IsAiFeatureAvailable();
        var aiVisibility = showFolderActions && aiAvailable ? Visibility.Visible : Visibility.Collapsed;
        DirectoryTreeAiMenuSeparator.Visibility = aiVisibility;
        DirectoryTreeAiExecuteQueryMenuItem.Visibility = aiVisibility;

        var gitAvailable = ExternalToolsService.IsAvailable(ExternalTool.Git);
        var gitVisibility = showFolderActions && gitAvailable ? Visibility.Visible : Visibility.Collapsed;
        DirectoryTreeGitMenuSeparator.Visibility = gitVisibility;
        DirectoryTreeGitInitMenuItem.Visibility = gitVisibility;
        DirectoryTreeGitCommitMenuItem.Visibility = gitVisibility;
        DirectoryTreeGitAmendMenuItem.Visibility = gitVisibility;
        DirectoryTreeGitHistoryMenuItem.Visibility = gitVisibility;

        if (showFolderActions)
        {
            var folderPath = node!.FullPath;
            var isBookmarked = _bookmarkService.Contains(folderPath);
            DirectoryTreeAddBookmarkMenuItem.Visibility = isBookmarked ? Visibility.Collapsed : Visibility.Visible;
            DirectoryTreeRemoveBookmarkMenuItem.Visibility = isBookmarked ? Visibility.Visible : Visibility.Collapsed;

            var repositoryRoot = gitAvailable
                ? GitRepositoryHelper.GetGitRepositoryRoot(folderPath)
                : null;
            var isGitRepo = repositoryRoot is not null;

            DirectoryTreeOpenInOtherPaneMenuItem.IsEnabled = true;
            DirectoryTreeCompareFoldersMenuItem.IsEnabled = _panes.Count > 1;
            DirectoryTreeEncryptFolderMenuItem.IsEnabled = !_isExtracting && !_isEncrypting;
            DirectoryTreeRenameMenuItem.IsEnabled = true;
            DirectoryTreeDeleteMenuItem.IsEnabled = true;
            DirectoryTreeShowWindowsMenuMenuItem.IsEnabled = true;

            if (gitAvailable)
            {
                DirectoryTreeGitInitMenuItem.IsEnabled = !isGitRepo;
                DirectoryTreeGitCommitMenuItem.IsEnabled = isGitRepo && !_isGitCommitInProgress;
                DirectoryTreeGitAmendMenuItem.IsEnabled = isGitRepo && !_isGitCommitInProgress;
                DirectoryTreeGitHistoryMenuItem.IsEnabled = isGitRepo && !_isGitHistoryInProgress;
            }

            if (aiAvailable)
                DirectoryTreeAiExecuteQueryMenuItem.IsEnabled = !_isAiInProgress;
        }

        var showDriveProperties = hasTarget && node is { IsDrive: true };
        DirectoryTreeDrivePropertiesMenuItem.Visibility = showDriveProperties ? Visibility.Visible : Visibility.Collapsed;
        DirectoryTreeDrivePropertiesMenuItem.IsEnabled = showDriveProperties;

        var showEject = hasTarget && node is { IsDrive: true, IsRemovable: true };
        DirectoryTreeEjectMenuItem.Visibility = showEject ? Visibility.Visible : Visibility.Collapsed;
        DirectoryTreeDriveSeparator.Visibility = showEject || showDriveProperties ? Visibility.Visible : Visibility.Collapsed;
        DirectoryTreeEjectMenuItem.IsEnabled = showEject;
    }

    private void DirectoryTreeOpenMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_directoryTreeMenuTarget is not { FullPath: { Length: > 0 } path })
            return;

        CancelActivePaneSearch();
        CloseEditor();
        CloseArchiveViewer();
        CloseMediaViewer();
        NavigateToDirectory(ActivePane, path, syncTree: true);
    }

    private void DirectoryTreeExpandMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_directoryTreeMenuTarget is null)
            return;

        if (FindTreeViewItem(DirectoryTree, _directoryTreeMenuTarget) is not { } container)
            return;

        container.IsExpanded = !container.IsExpanded;
        if (container.IsExpanded)
            container.BringIntoView();
    }

    private void DirectoryTreeReloadMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_directoryTreeMenuTarget is not { FullPath: { Length: > 0 } path })
            return;

        ReloadTreeBranch(_directoryTreeMenuTarget);
        if (ActivePane.CurrentPath is not null && IsPathWithinRoot(ActivePane.CurrentPath, path))
            SyncTreeToPath(ActivePane.CurrentPath);
    }

    private void DirectoryTreeNewFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDirectoryTreeFolderTarget(out var folderPath))
            return;

        ShowNamePrompt(NamePromptMode.NewFolder, ActivePane, createTargetDirectory: folderPath);
    }

    private void DirectoryTreeNewTextFileMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDirectoryTreeFolderTarget(out var folderPath))
            return;

        ShowNamePrompt(NamePromptMode.NewTextFile, ActivePane, createTargetDirectory: folderPath);
    }

    private void DirectoryTreeOpenInOtherPaneMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDirectoryTreeFolderTarget(out var folderPath))
            return;

        OpenPathInOtherPane(folderPath);
    }

    private void DirectoryTreeCompareFoldersMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDirectoryTreeFolderTarget(out var folderPath))
            return;

        if (_panes.Count < 2)
        {
            StatusText.Text = LocalizationManager.Instance["UI_CompareNeedsTwoPanes"];
            return;
        }

        var otherPane = _panes.FirstOrDefault(pane => !ReferenceEquals(pane, ActivePane));
        if (otherPane is null || string.IsNullOrWhiteSpace(otherPane.CurrentPath))
            return;

        ShowFolderCompare(folderPath, otherPane.CurrentPath);
    }

    private void DirectoryTreeAddBookmarkMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDirectoryTreeFolderTarget(out var folderPath))
            return;

        _bookmarkService.Add(folderPath);
        UpdateBookmarksEmptyState();
        StatusText.Text = LocalizationManager.Instance["UI_BookmarkAdded"];
    }

    private void DirectoryTreeRemoveBookmarkMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDirectoryTreeFolderTarget(out var folderPath))
            return;

        _bookmarkService.Remove(folderPath);
        UpdateBookmarksEmptyState();
        StatusText.Text = LocalizationManager.Instance["UI_BookmarkRemoved"];
    }

    private void DirectoryTreeEncryptFolderMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDirectoryTreeFolderTarget(out var folderPath) || _isEncrypting || _isExtracting)
            return;

        _encryptTargetPane = ActivePane;
        _encryptFolderSourcePath = folderPath;
        ShowEncryptFolderOverlay();
    }

    private void DirectoryTreeRenameMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDirectoryTreeFolderTarget(out var folderPath))
            return;

        ShowRenamePrompt(ActivePane, CreateDirectoryTreeEntry(folderPath));
    }

    private void DirectoryTreeDeleteMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDirectoryTreeFolderTarget(out var folderPath))
            return;

        ShowDeleteConfirmation(ActivePane, [CreateDirectoryTreeEntry(folderPath)]);
    }

    private void DirectoryTreeAiExecuteQueryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        WithDirectoryTreeFolderTarget((pane, _) => HandleAiExecuteQueryRequest(pane));
    }

    private void DirectoryTreeGitInitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDirectoryTreeFolderTarget(out var folderPath))
            return;

        HandleGitInit(ActivePane, folderPath);
    }

    private void DirectoryTreeGitCommitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        WithDirectoryTreeFolderTarget((pane, _) => HandleGitCommitRequest(pane));
    }

    private void DirectoryTreeGitAmendMenuItem_Click(object sender, RoutedEventArgs e)
    {
        WithDirectoryTreeFolderTarget((pane, _) => HandleGitAmendRequest(pane));
    }

    private void DirectoryTreeGitHistoryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        WithDirectoryTreeFolderTarget((pane, _) => HandleGitHistoryRequest(pane));
    }

    private void DirectoryTreeShowWindowsMenuMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_directoryTreeMenuTarget is null)
            return;

        if (DirectoryTreeContextMenu is ContextMenu menu)
            menu.IsOpen = false;

        var targetNode = _directoryTreeMenuTarget;
        Dispatcher.BeginInvoke(
            () => ShowDirectoryTreeNativeContextMenu(targetNode),
            DispatcherPriority.ApplicationIdle);
    }

    private void DirectoryTreeEjectMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_directoryTreeMenuTarget is not { IsDrive: true, IsRemovable: true, FullPath: { Length: > 0 } driveRoot })
            return;

        var loc = LocalizationManager.Instance;
        foreach (var pane in _panes)
        {
            if (IsPathOnDrive(pane.CurrentPath, driveRoot))
                NavigatePaneFromUnavailableDrive(pane);
        }

        if (RemovableDriveEjectService.TryEject(driveRoot, out var error))
        {
            StatusText.Text = string.Format(loc["UI_TreeEjectSuccess"], _directoryTreeMenuTarget.Name);
            RefreshDriveTreeAfterDeviceChange();
            return;
        }

        StatusText.Text = string.Format(loc["UI_TreeEjectFailed"], error ?? loc["UI_TreeEjectInvalidDrive"]);
    }

    private void DirectoryTreeDrivePropertiesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_directoryTreeMenuTarget is not { IsDrive: true, FullPath: { Length: > 0 } driveRoot })
            return;

        try
        {
            var helper = new WindowInteropHelper(this);
            if (helper.Handle == IntPtr.Zero)
                throw new InvalidOperationException("Window handle is not available.");

            DrivePropertiesService.Show(driveRoot, helper.Handle);
        }
        catch (Exception ex)
        {
            StatusText.Text = string.Format(
                LocalizationManager.Instance["UI_TreeDrivePropertiesFailed"],
                ex.Message);
        }
    }

    private void DirectoryTreeRefreshDrivesMenuItem_Click(object sender, RoutedEventArgs e) =>
        RefreshDriveTreeAfterDeviceChange();

    private void WithDirectoryTreeFolderTarget(Action<DirectoryPaneState, string> action)
    {
        if (!TryGetDirectoryTreeFolderTarget(out var folderPath))
            return;

        CancelActivePaneSearch();
        CloseEditor();
        CloseArchiveViewer();
        CloseMediaViewer();

        var pane = ActivePane;
        SetActivePane(pane);
        NavigateToDirectory(pane, folderPath, syncTree: false);
        action(pane, folderPath);
    }

    private bool TryGetDirectoryTreeFolderTarget(out string folderPath)
    {
        folderPath = string.Empty;

        if (_directoryTreeMenuTarget is not { IsDrive: false, FullPath: { Length: > 0 } path })
            return false;

        try
        {
            folderPath = Path.GetFullPath(path);
            return Directory.Exists(folderPath);
        }
        catch
        {
            return false;
        }
    }

    private void OpenPathInOtherPane(string path)
    {
        if (_panes.Count < 2)
        {
            var newPane = CreatePane();
            AppendPaneToHost(newPane.Control);
            NavigateToDirectory(newPane, ActivePane.CurrentPath ?? string.Empty, syncTree: false, recordHistory: false);
        }

        var targetPane = _panes.FirstOrDefault(pane => !ReferenceEquals(pane, ActivePane)) ?? _panes.Last();
        SetActivePane(targetPane);
        NavigateToDirectory(targetPane, path, syncTree: true);
    }

    private static FileSystemEntry CreateDirectoryTreeEntry(string folderPath)
    {
        var fullPath = Path.GetFullPath(folderPath);
        var name = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name))
            name = fullPath.TrimEnd('\\');

        return new FileSystemEntry
        {
            Name = name,
            FullPath = fullPath,
            IsDirectory = true,
            Type = "File folder"
        };
    }

    private void ShowDirectoryTreeNativeContextMenu(DirectoryTreeNode node)
    {
        try
        {
            var helper = new WindowInteropHelper(this);
            if (helper.Handle == IntPtr.Zero)
                return;

            var position = Mouse.GetPosition(this);
            var screenPoint = PointToScreen(position);

            NativeShellContextMenuService.ShowContextMenu(
                [node.FullPath],
                (int)screenPoint.X,
                (int)screenPoint.Y,
                helper.Handle,
                this);
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private static DirectoryTreeNode? GetDirectoryTreeNodeUnderMouse(DependencyObject? source)
    {
        var item = FindParentTreeViewItem(source);
        return item?.DataContext as DirectoryTreeNode;
    }

    private static TreeViewItem? FindParentTreeViewItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is TreeViewItem treeViewItem)
                return treeViewItem;

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static bool IsPathOnDrive(string? path, string driveRoot)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        var root = Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(root) && PathsEqual(root, driveRoot);
    }
}
