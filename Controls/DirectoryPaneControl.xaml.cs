using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using FileExplorer.Models;

namespace FileExplorer.Controls;

public partial class DirectoryPaneControl : UserControl
{
    private const double MarqueeDragThreshold = 4;

    private static readonly SolidColorBrush ActiveBorderBrush = new(Color.FromArgb(0xFF, 0x00, 0x78, 0xD7));
    private static readonly SolidColorBrush InactiveBorderBrush = new(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

    private Point _marqueeStartPoint;
    private Point _dragStartPoint;
    private bool _marqueePending;
    private bool _marqueeActive;
    private bool _extendMarqueeSelection;
    private bool _dragPending;

    static DirectoryPaneControl()
    {
        ActiveBorderBrush.Freeze();
        InactiveBorderBrush.Freeze();
    }

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
    public event EventHandler<FileDropEventArgs>? FileDropRequested;

    public DirectoryPaneControl()
    {
        InitializeComponent();
        SetIsActive(false);
    }

    public ListView FileListView => FileList;

    public TextBox PathSearchTextBox => PathSearchBox;

    public MenuItem ExtractArchiveMenuItemControl => ExtractArchiveMenuItem;

    public Separator ExtractArchiveSeparatorControl => ExtractArchiveSeparator;

    public void SetIsActive(bool isActive) =>
        PaneChrome.BorderBrush = isActive ? ActiveBorderBrush : InactiveBorderBrush;

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

    private void FileList_ContextMenuOpening(object sender, ContextMenuEventArgs e) =>
        FileListContextMenuOpening?.Invoke(sender, e);

    private void NewFolderMenuItem_Click(object sender, RoutedEventArgs e) =>
        NewFolderRequested?.Invoke(sender, e);

    private void NewTextFileMenuItem_Click(object sender, RoutedEventArgs e) =>
        NewTextFileRequested?.Invoke(sender, e);

    private void DeleteMenuItem_Click(object sender, RoutedEventArgs e) =>
        DeleteRequested?.Invoke(sender, e);

    private void ExtractArchiveMenuItem_Click(object sender, RoutedEventArgs e) =>
        ExtractArchiveRequested?.Invoke(sender, e);
}
