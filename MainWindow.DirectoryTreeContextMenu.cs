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
        DirectoryTreeEjectMenuItem.Header = loc["UI_TreeSafelyEject"];
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

        var showEject = hasTarget && node is { IsDrive: true, IsRemovable: true };
        DirectoryTreeEjectMenuItem.Visibility = showEject ? Visibility.Visible : Visibility.Collapsed;
        DirectoryTreeDriveSeparator.Visibility = showEject ? Visibility.Visible : Visibility.Collapsed;
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

    private void DirectoryTreeRefreshDrivesMenuItem_Click(object sender, RoutedEventArgs e) =>
        RefreshDriveTreeAfterDeviceChange();

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
