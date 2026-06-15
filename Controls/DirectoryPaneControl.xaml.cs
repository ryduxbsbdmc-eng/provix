using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FileExplorer.Helpers;
using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Controls;

public partial class DirectoryPaneControl : UserControl
{
    private const double MarqueeDragThreshold = 4;

    private Brush? _activeBorderBrush;
    private Brush? _inactiveBorderBrush;

    private Point _marqueeStartPoint;
    private Point _dragStartPoint;
    private bool _marqueePending;
    private bool _marqueeActive;
    private bool _extendMarqueeSelection;
    private bool _dragPending;

    public DirectoryPaneState? PaneState { get; set; }

    public event EventHandler? Activated;
    public event EventHandler? CloseRequested;
    public event TextChangedEventHandler? PathSearchTextChanged;
    public event KeyEventHandler? PathSearchKeyDown;
    public event MouseButtonEventHandler? FileListMouseDoubleClick;
    public event ContextMenuEventHandler? FileListContextMenuOpening;
    public event RoutedEventHandler? NewFolderRequested;
    public event RoutedEventHandler? NewTextFileRequested;
    public event RoutedEventHandler? DeleteRequested;
    public event RoutedEventHandler? ExtractArchiveRequested;
    public event RoutedEventHandler? EncryptFolderRequested;
    public event EventHandler<FileDropEventArgs>? FileDropRequested;
    public event EventHandler<GitDirectoryEventArgs>? GitInitRequested;
    public event EventHandler<GitDirectoryEventArgs>? GitCommitRequested;

    public event EventHandler<GitDirectoryEventArgs>? GitAmendRequested;
    public event EventHandler<GitDirectoryEventArgs>? GitHistoryRequested;
    public event EventHandler<GitDirectoryEventArgs>? GitBranchRequested;
    public event RoutedEventHandler? AiExecuteQueryRequested;

    public DirectoryPaneControl()
    {
        InitializeComponent();
        SetIsActive(false);
    }

    public ListView FileListView => FileList;

    public TextBox PathSearchTextBox => PathSearchBox;

    public MenuItem ExtractArchiveMenuItemControl => ExtractArchiveMenuItem;
    public MenuItem EncryptFolderMenuItemControl => EncryptFolderMenuItem;

    public MenuItem GitInitMenuItemControl => GitInitMenuItem;

    public MenuItem GitCommitMenuItemControl => GitCommitMenuItem;

    public MenuItem GitAmendMenuItemControl => GitAmendMenuItem;

    public MenuItem GitHistoryMenuItemControl => GitHistoryMenuItem;

    public MenuItem AiExecuteQueryMenuItemControl => AiExecuteQueryMenuItem;

    public Separator ExtractArchiveSeparatorControl => ExtractArchiveSeparator;
    public Separator EncryptFolderSeparatorControl => EncryptFolderSeparator;

    public void ResetThemeBrushes()
    {
        _activeBorderBrush = null;
        _inactiveBorderBrush = null;
    }

    public void SetIsActive(bool isActive)
    {
        EnsurePaneBorderBrushes();
        PaneChrome.BorderBrush = isActive ? _activeBorderBrush : _inactiveBorderBrush;
    }

    public void ApplyLocalization()
    {
        var loc = LocalizationManager.Instance;
        NameColumn.Header = loc["UI_ColumnName"];
        DateModifiedColumn.Header = loc["UI_ColumnDateModified"];
        TypeColumn.Header = loc["UI_ColumnType"];
        SizeColumn.Header = loc["UI_ColumnSize"];
        ClosePaneButton.ToolTip = loc["UI_ClosePane"];
    }

    public void RefreshDateColumnDisplay() =>
        FileList.Items.Refresh();

    public void PlayListRefreshAnimation() =>
        UiAnimationHelper.FadeIn(FileList);

    private void EnsurePaneBorderBrushes()
    {
        if (_activeBorderBrush is not null && _inactiveBorderBrush is not null)
            return;

        _activeBorderBrush = Application.Current.FindResource("SelectionBorderBrush") as Brush;
        _inactiveBorderBrush = Application.Current.FindResource("PaneInactiveBorderBrush") as Brush;
    }

    public void SetCloseButtonVisible(bool isVisible) =>
        ClosePaneButton.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

    private void FileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            ResetDragAndMarqueeState();

            var clickedItem = FindListViewItem(e.OriginalSource as DependencyObject);
            if (clickedItem?.DataContext is FileSystemEntry entry)
            {
                FileList.SelectedItem = entry;
                FileListMouseDoubleClick?.Invoke(sender, e);
                e.Handled = true;
            }

            return;
        }

        var clickedListItem = FindListViewItem(e.OriginalSource as DependencyObject);

        if (clickedListItem is not null)
        {
            HandleItemDragMouseDown(clickedListItem, e);
            _dragStartPoint = e.GetPosition(FileList);
            _dragPending = true;
            _marqueePending = false;
            _marqueeActive = false;
            FileList.Focus();
            return;
        }

        if (!IsEmptyListViewBackground(e.OriginalSource as DependencyObject))
            return;

        _dragPending = false;
        _marqueeStartPoint = e.GetPosition(MarqueeCanvas);
        _marqueePending = true;
        _marqueeActive = false;
        _extendMarqueeSelection = IsExtendSelectionModifierPressed();
        FileList.Focus();
        FileList.CaptureMouse();
        e.Handled = true;
    }

    private void FileList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragPending && !_marqueePending && !_marqueeActive && e.LeftButton == MouseButtonState.Pressed)
        {
            TryStartFileDrag(e);
            return;
        }

        if (!_marqueePending && !_marqueeActive)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
            return;

        var currentPoint = e.GetPosition(MarqueeCanvas);

        if (!_marqueeActive)
        {
            var deltaX = currentPoint.X - _marqueeStartPoint.X;
            var deltaY = currentPoint.Y - _marqueeStartPoint.Y;

            if (Math.Abs(deltaX) < MarqueeDragThreshold && Math.Abs(deltaY) < MarqueeDragThreshold)
                return;

            BeginMarqueeSelection();
            _marqueeActive = true;
            _marqueePending = false;
            MarqueeRectangle.Visibility = Visibility.Visible;
        }

        UpdateMarqueeRectangle(_marqueeStartPoint, currentPoint);
        UpdateMarqueeSelection();
    }

    private void FileList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_marqueePending || _marqueeActive)
        {
            var wasActive = _marqueeActive;
            var wasPending = _marqueePending;

            if (wasActive)
                UpdateMarqueeSelection();

            EndMarquee();

            if (wasPending && !wasActive && !IsExtendSelectionModifierPressed())
                FileList.SelectedItems.Clear();

            _marqueePending = false;
            e.Handled = true;
        }

        _dragPending = false;
    }

    private void FileList_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_marqueePending && !_marqueeActive)
            return;

        EndMarquee();
        _marqueePending = false;
    }

    private void TryStartFileDrag(MouseEventArgs e)
    {
        var currentPoint = e.GetPosition(FileList);

        if (Math.Abs(currentPoint.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPoint.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragPending = false;

        var pathList = new List<string>(FileList.SelectedItems.Count);
        foreach (var selectedItem in FileList.SelectedItems)
        {
            if (selectedItem is FileSystemEntry entry && !string.IsNullOrWhiteSpace(entry.FullPath))
                pathList.Add(entry.FullPath);
        }

        var paths = pathList
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (paths.Length == 0)
            return;

        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, paths);
        DragDrop.DoDragDrop(FileList, data, DragDropEffects.Move | DragDropEffects.Copy);
    }

    private void ResetDragAndMarqueeState()
    {
        _dragPending = false;
        _marqueePending = false;
        EndMarquee();
    }

    private void HandleItemDragMouseDown(ListViewItem clickedItem, MouseButtonEventArgs e)
    {
        if (clickedItem.DataContext is not FileSystemEntry entry)
            return;

        if (FileList.SelectedItems.Contains(entry))
        {
            e.Handled = true;
            return;
        }

        var extendSelection = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;

        if (extendSelection)
        {
            FileList.SelectedItems.Add(entry);
        }
        else
        {
            FileList.SelectedItems.Clear();
            FileList.SelectedItems.Add(entry);
        }

        e.Handled = true;
    }

    private void FileList_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var sourcePaths = ExtractDroppedPaths(e.Data);
        var targetDirectory = ResolveListViewDropTarget(e, sourcePaths);

        e.Effects = string.IsNullOrEmpty(targetDirectory)
            ? DragDropEffects.None
            : DragDropEffects.Move;
        e.Handled = true;
    }

    private void FileList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var sourcePaths = ExtractDroppedPaths(e.Data);
        if (sourcePaths.Length == 0)
            return;

        var targetDirectory = ResolveListViewDropTarget(e, sourcePaths);
        if (string.IsNullOrEmpty(targetDirectory))
            return;

        FileDropRequested?.Invoke(this, new FileDropEventArgs
        {
            SourcePaths = sourcePaths,
            TargetDirectory = targetDirectory
        });

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private string? ResolveListViewDropTarget(DragEventArgs e, IReadOnlyList<string> sourcePaths)
    {
        var droppedOnItem = FindDropTargetListViewItem(e);

        if (droppedOnItem?.DataContext is FileSystemEntry entry && entry.IsDirectory)
        {
            var folderPath = Path.GetFullPath(entry.FullPath);

            if (!IsPathInSourceList(folderPath, sourcePaths))
                return folderPath;
        }

        return PaneState?.CurrentPath;
    }

    private ListViewItem? FindDropTargetListViewItem(DragEventArgs e)
    {
        var dropPoint = e.GetPosition(FileList);
        var hitResult = VisualTreeHelper.HitTest(FileList, dropPoint);
        var droppedOnItem = FindListViewItem(hitResult?.VisualHit);

        if (droppedOnItem is not null)
            return droppedOnItem;

        return FindListViewItem(e.OriginalSource as DependencyObject);
    }

    private static bool IsPathInSourceList(string path, IReadOnlyList<string> sourcePaths)
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

    private static string[] ExtractDroppedPaths(IDataObject data)
    {
        if (data.GetData(DataFormats.FileDrop) is not string[] paths)
            return [];

        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void BeginMarqueeSelection()
    {
        _extendMarqueeSelection = IsExtendSelectionModifierPressed();

        if (!_extendMarqueeSelection)
            FileList.SelectedItems.Clear();

        FileList.Focus();
    }

    private void EndMarquee()
    {
        MarqueeRectangle.Visibility = Visibility.Collapsed;
        MarqueeRectangle.Width = 0;
        MarqueeRectangle.Height = 0;
        _marqueeActive = false;

        if (FileList.IsMouseCaptured)
            FileList.ReleaseMouseCapture();
    }

    private void UpdateMarqueeRectangle(Point startPoint, Point currentPoint)
    {
        var left = Math.Min(startPoint.X, currentPoint.X);
        var top = Math.Min(startPoint.Y, currentPoint.Y);
        var width = Math.Abs(currentPoint.X - startPoint.X);
        var height = Math.Abs(currentPoint.Y - startPoint.Y);

        Canvas.SetLeft(MarqueeRectangle, left);
        Canvas.SetTop(MarqueeRectangle, top);
        MarqueeRectangle.Width = width;
        MarqueeRectangle.Height = height;
    }

    private Rect GetSelectionBoxRect()
    {
        var left = Canvas.GetLeft(MarqueeRectangle);
        var top = Canvas.GetTop(MarqueeRectangle);

        if (double.IsNaN(left))
            left = 0;

        if (double.IsNaN(top))
            top = 0;

        return new Rect(left, top, MarqueeRectangle.Width, MarqueeRectangle.Height);
    }

    private void UpdateMarqueeSelection()
    {
        var selectionRect = GetSelectionBoxRect();
        if (selectionRect.Width < 1 || selectionRect.Height < 1)
            return;

        EnsureItemContainersRealized();

        Rect selectionBoundsInListView;
        try
        {
            var canvasToListView = MarqueeCanvas.TransformToVisual(FileList);
            selectionBoundsInListView = canvasToListView.TransformBounds(selectionRect);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        foreach (var item in FileList.Items)
        {
            var container = FileList.ItemContainerGenerator.ContainerFromItem(item) as ListViewItem;
            if (container is null || !container.IsVisible)
                continue;

            if (container.ActualWidth <= 0 || container.ActualHeight <= 0)
                continue;

            var topLeft = container.TranslatePoint(new Point(0, 0), FileList);
            var itemBounds = new Rect(topLeft.X, topLeft.Y, container.ActualWidth, container.ActualHeight);

            if (selectionBoundsInListView.IntersectsWith(itemBounds))
            {
                if (!FileList.SelectedItems.Contains(item))
                    FileList.SelectedItems.Add(item);
            }
            else if (FileList.SelectedItems.Contains(item))
            {
                FileList.SelectedItems.Remove(item);
            }
        }
    }

    private void EnsureItemContainersRealized()
    {
        FileList.ApplyTemplate();
        FileList.UpdateLayout();

        for (var index = 0; index < FileList.Items.Count; index++)
        {
            if (FileList.ItemContainerGenerator.ContainerFromIndex(index) is not null)
                continue;

            FileList.UpdateLayout();
            break;
        }
    }

    private static ListViewItem? FindListViewItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ListViewItem listViewItem)
                return listViewItem;

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private static bool IsExtendSelectionModifierPressed() =>
        (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;

    private static bool IsEmptyListViewBackground(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is ListViewItem or GridViewColumnHeader or ScrollBar or Thumb)
                return false;

            if (source is ListView)
                return true;

            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private void DirectoryPaneControl_PreviewMouseDown(object sender, MouseButtonEventArgs e) =>
        Activated?.Invoke(this, EventArgs.Empty);

    private void DirectoryPaneControl_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        Activated?.Invoke(this, EventArgs.Empty);

    private void ClosePaneButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);

    private void PathSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        PathSearchTextChanged?.Invoke(sender, e);

    private void PathSearchBox_KeyDown(object sender, KeyEventArgs e) =>
        PathSearchKeyDown?.Invoke(sender, e);

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e) =>
        FileListMouseDoubleClick?.Invoke(sender, e);

    private void FileList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        SelectItemUnderCursor(e.OriginalSource as DependencyObject);
        FileList.Focus();

        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            e.Handled = true;
            Dispatcher.BeginInvoke(ShowNativeShellContextMenu, DispatcherPriority.ApplicationIdle);
        }
    }

    private void SelectItemUnderCursor(DependencyObject? source)
    {
        var clickedItem = FindListViewItem(source);

        if (clickedItem?.DataContext is FileSystemEntry entry)
        {
            if (!FileList.SelectedItems.Contains(entry))
            {
                if (!IsExtendSelectionModifierPressed())
                    FileList.SelectedItems.Clear();

                FileList.SelectedItems.Add(entry);
            }

            clickedItem.Focus();
            return;
        }

        if (IsEmptyListViewBackground(source) && !IsExtendSelectionModifierPressed())
            FileList.SelectedItems.Clear();
    }

    private void FileList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            e.Handled = true;
            return;
        }

        FileListContextMenuOpening?.Invoke(sender, e);
    }

    private void NewFolderMenuItem_Click(object sender, RoutedEventArgs e) =>
        NewFolderRequested?.Invoke(sender, e);

    private void NewTextFileMenuItem_Click(object sender, RoutedEventArgs e) =>
        NewTextFileRequested?.Invoke(sender, e);

    private void DeleteMenuItem_Click(object sender, RoutedEventArgs e) =>
        DeleteRequested?.Invoke(sender, e);

    private void ExtractArchiveMenuItem_Click(object sender, RoutedEventArgs e) =>
        ExtractArchiveRequested?.Invoke(sender, e);

    private void EncryptFolderMenuItem_Click(object sender, RoutedEventArgs e) =>
        EncryptFolderRequested?.Invoke(sender, e);

    private void AiExecuteQueryMenuItem_Click(object sender, RoutedEventArgs e) =>
        AiExecuteQueryRequested?.Invoke(sender, e);

    private void GitInitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetContextMenuTargetDirectory(out var directory))
            return;

        GitInitRequested?.Invoke(this, new GitDirectoryEventArgs { TargetDirectory = directory });
    }

    private void GitCommitMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetPaneCurrentDirectory(out var directory))
            return;

        GitCommitRequested?.Invoke(this, new GitDirectoryEventArgs { TargetDirectory = directory });
    }

    private void GitAmendMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetPaneCurrentDirectory(out var directory))
            return;

        GitAmendRequested?.Invoke(this, new GitDirectoryEventArgs { TargetDirectory = directory });
    }

    private void GitHistoryMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetPaneCurrentDirectory(out var directory))
            return;

        GitHistoryRequested?.Invoke(this, new GitDirectoryEventArgs { TargetDirectory = directory });
    }

    public void UpdateGitStatus(bool isRepo, string branch, int changeCount)
    {
        var wasVisible = GitStatusBar.Visibility == Visibility.Visible;
        GitStatusBar.Visibility = isRepo ? Visibility.Visible : Visibility.Collapsed;
        if (!isRepo)
            return;

        if (!wasVisible)
            UiAnimationHelper.FadeIn(GitStatusBar);

        GitBranchText.Text = string.IsNullOrEmpty(branch) ? "DETACHED" : branch;
        var loc = LocalizationManager.Instance;
        GitStatusText.Text = changeCount == 1 
            ? $"1 change" 
            : $"{changeCount} changes";
        
        GitBranchButton.Content = loc["UI_GitSwitchBranch"] ?? "Switch Branch";
    }

    private void GitBranchButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetPaneCurrentDirectory(out var directory))
            return;

        GitBranchRequested?.Invoke(this, new GitDirectoryEventArgs { TargetDirectory = directory });
    }

    public bool TryGetPaneCurrentDirectory(out string directory)
    {
        directory = string.Empty;

        if (string.IsNullOrWhiteSpace(PaneState?.CurrentPath))
            return false;

        try
        {
            directory = Path.GetFullPath(PaneState.CurrentPath);
            return Directory.Exists(directory);
        }
        catch
        {
            return false;
        }
    }

    public bool TryResolveGitContextDirectory(out string directory)
    {
        directory = string.Empty;

        var selectedEntries = FileList.SelectedItems
            .Cast<object>()
            .OfType<FileSystemEntry>()
            .ToList();

        if (selectedEntries.Count == 1)
        {
            var entry = selectedEntries[0];
            var candidate = entry.IsDirectory
                ? entry.FullPath
                : Path.GetDirectoryName(entry.FullPath) ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(candidate))
            {
                try
                {
                    directory = Path.GetFullPath(candidate);
                    return Directory.Exists(directory);
                }
                catch
                {
                    return false;
                }
            }
        }

        return TryGetPaneCurrentDirectory(out directory);
    }

    public bool TryGetContextMenuTargetDirectory(out string directory) =>
        TryResolveGitContextDirectory(out directory);

    private void ShowWindowsMenuMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (FileList.ContextMenu is ContextMenu menu)
            menu.IsOpen = false;

        // Defer until the WPF context menu has fully closed; TrackPopupMenuEx is modal Win32 UI.
        Dispatcher.BeginInvoke(ShowNativeShellContextMenu, DispatcherPriority.ApplicationIdle);
    }

    private void ShowNativeShellContextMenu()
    {
        try
        {
            var paths = GetContextMenuTargetPaths();
            if (paths.Count == 0)
                throw new InvalidOperationException("No items available for the Windows context menu.");

            var mainWindow = Application.Current.MainWindow
                ?? throw new InvalidOperationException("Application.Current.MainWindow is not available.");

            var windowHelper = new WindowInteropHelper(mainWindow);
            if (windowHelper.Handle == IntPtr.Zero)
                windowHelper.EnsureHandle();

            var hwnd = windowHelper.Handle;
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException("Unable to obtain a valid MainWindow HWND.");

            var point = FileList.PointToScreen(Mouse.GetPosition(FileList));

            NativeShellContextMenuService.ShowContextMenu(
                paths,
                (int)point.X,
                (int)point.Y,
                hwnd,
                mainWindow);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.ToString(), "Native Menu Error");
        }
    }

    private List<string> GetContextMenuTargetPaths()
    {
        var selectedPaths = FileList.SelectedItems
            .Cast<object>()
            .OfType<FileSystemEntry>()
            .Select(entry => entry.FullPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (selectedPaths.Count > 0)
            return selectedPaths;

        if (!string.IsNullOrWhiteSpace(PaneState?.CurrentPath))
            return [PaneState.CurrentPath];

        return [];
    }
}
