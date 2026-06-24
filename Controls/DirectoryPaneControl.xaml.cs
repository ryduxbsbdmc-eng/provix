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
    private const double TabDragThreshold = 4;

    private Brush? _activeBorderBrush;
    private Brush? _inactiveBorderBrush;

    private Point _marqueeStartPoint;
    private Point _dragStartPoint;
    private bool _marqueePending;
    private bool _marqueeActive;
    private bool _extendMarqueeSelection;
    private bool _dragPending;

    private Border? _tabDragHighlightHost;
    private Point _tabDragStartPoint;
    private bool _tabDragPending;
    private PaneTab? _tabDragSourceTab;
    private Border? _tabDragSourceHost;

    public DirectoryPaneState? PaneState { get; set; }

    public event EventHandler? Activated;
    public event EventHandler? CloseRequested;
    public event TextChangedEventHandler? PathSearchTextChanged;
    public event KeyEventHandler? PathSearchKeyDown;
    public event MouseButtonEventHandler? FileListMouseDoubleClick;
    public event ContextMenuEventHandler? FileListContextMenuOpening;
    public event SelectionChangedEventHandler? FileListSelectionChanged;
    public event RoutedEventHandler? NewFolderRequested;
    public event RoutedEventHandler? NewTextFileRequested;
    public event RoutedEventHandler? RenameRequested;
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

    public event RoutedEventHandler? OpenInOtherPaneRequested;
    public event RoutedEventHandler? CompareFoldersRequested;
    public event RoutedEventHandler? AddBookmarkRequested;
    public event RoutedEventHandler? RemoveBookmarkRequested;
    public event EventHandler<PaneTabEventArgs>? TabSelected;
    public event EventHandler<PaneTabEventArgs>? TabCloseRequested;
    public event RoutedEventHandler? NewTabRequested;
    public event EventHandler<PaneTabMoveEventArgs>? TabMoveRequested;

    public DirectoryPaneControl()
    {
        InitializeComponent();
        SetIsActive(false);

        AllowDrop = true;
        PreviewDragOver += DirectoryPaneControl_PreviewDragOver;
        PreviewDrop += DirectoryPaneControl_PreviewDrop;
        PaneChrome.AllowDrop = true;
        PaneChrome.DragOver += PaneSurface_DragOver;
        PaneChrome.Drop += PaneSurface_Drop;
        FileListHost.AllowDrop = true;
        FileListHost.DragOver += PaneSurface_DragOver;
        FileListHost.Drop += PaneSurface_Drop;
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

    public MenuItem OpenInOtherPaneMenuItemControl => OpenInOtherPaneMenuItem;

    public MenuItem CompareFoldersMenuItemControl => CompareFoldersMenuItem;

    public MenuItem AddBookmarkMenuItemControl => AddBookmarkMenuItem;

    public MenuItem RemoveBookmarkMenuItemControl => RemoveBookmarkMenuItem;

    public Separator AiMenuSeparatorControl => AiMenuSeparator;

    public Separator GitMenuSeparatorControl => GitMenuSeparator;

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
        NewTabButton.ToolTip = loc["UI_NewTab"];
    }

    public void RefreshTabBar()
    {
        TabBarPanel.Children.Clear();
        if (PaneState is null)
            return;

        foreach (var tab in PaneState.Tabs)
        {
            var isActive = ReferenceEquals(tab, PaneState.ActiveTab);
            TabBarPanel.Children.Add(CreateTabHost(tab, isActive));
        }
    }

    private UIElement CreateTabHost(PaneTab tab, bool isActive)
    {
        var loc = LocalizationManager.Instance;
        var title = tab.GetTitle();
        if (string.IsNullOrWhiteSpace(title))
            title = loc["UI_NewTab"];

        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        var titleText = new TextBlock
        {
            Text = title,
            Margin = new Thickness(8, 4, 4, 4),
            Foreground = isActive
                ? Application.Current.FindResource("SelectionForegroundBrush") as Brush
                : Application.Current.FindResource("TextPrimaryBrush") as Brush,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 140
        };
        panel.Children.Add(titleText);

        if (PaneState!.Tabs.Count > 1)
        {
            var closeButton = new Button
            {
                Content = "\uE8BB",
                Tag = tab,
                Width = 20,
                Height = 20,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 8,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Application.Current.FindResource("TextSecondaryBrush") as Brush,
                Cursor = Cursors.Hand,
                ToolTip = loc["UI_CloseTab"]
            };
            closeButton.PreviewMouseLeftButtonDown += TabCloseButton_PreviewMouseLeftButtonDown;
            panel.Children.Add(closeButton);
        }

        var host = new Border
        {
            Child = panel,
            Tag = tab,
            Margin = new Thickness(0, 0, 4, 0),
            CornerRadius = new CornerRadius(4),
            Background = isActive
                ? Application.Current.FindResource("SelectionFillBrush") as Brush
                : Brushes.Transparent,
            BorderBrush = isActive
                ? Application.Current.FindResource("SelectionBorderBrush") as Brush
                : Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        host.PreviewMouseLeftButtonDown += (_, e) => TabHost_PreviewMouseLeftButtonDown(e, tab, host);
        host.PreviewMouseMove += TabHost_PreviewMouseMove;
        host.PreviewMouseLeftButtonUp += TabHost_PreviewMouseLeftButtonUp;
        host.LostMouseCapture += TabHost_LostMouseCapture;

        host.AllowDrop = true;
        host.DragOver += (_, e) => TabHost_DragOver(e, tab, host);
        host.DragLeave += TabHost_DragLeave;
        host.Drop += (_, e) => TabHost_Drop(e, tab);

        return host;
    }

    private void TabBarHost_DragOver(object sender, DragEventArgs e)
    {
        if (TryGetTabDragPayload(e.Data) is not null)
        {
            if (FindTabHostUnderPoint(e) is { Tab: var tab, Host: var host })
                PrepareTabMoveDrop(e, tab, host);
            else
                PrepareTabMoveDrop(e, null, null);

            return;
        }

        if (FindTabHostUnderPoint(e) is { Tab: var fileTab, Host: var fileHost })
        {
            PrepareFileTabDrop(e, fileTab, fileHost);
            return;
        }

        if (!PrepareFileTabDrop(e, PaneState?.ActiveTab, null))
            return;

        e.Handled = true;
    }

    private void TabBarHost_Drop(object sender, DragEventArgs e)
    {
        if (TryGetTabDragPayload(e.Data) is not null)
        {
            var insertIndex = FindTabHostUnderPoint(e) is { Tab: var hoveredTab }
                ? ResolveTabInsertIndex(hoveredTab, e)
                : ResolveTabInsertIndexAtPoint(e);

            CompleteTabMoveDrop(e, insertIndex);
            return;
        }

        if (FindTabHostUnderPoint(e) is { Tab: var tab })
        {
            CompleteFileTabDrop(e, tab);
            return;
        }

        var activeTab = PaneState?.ActiveTab;
        if (activeTab is null)
            return;

        CompleteFileTabDrop(e, activeTab);
    }

    private void TabHost_DragOver(DragEventArgs e, PaneTab tab, Border host)
    {
        if (TryGetTabDragPayload(e.Data) is not null)
            PrepareTabMoveDrop(e, tab, host);
        else
            PrepareFileTabDrop(e, tab, host);
    }

    private void TabHost_DragLeave(object sender, DragEventArgs e)
    {
        if (!IsPointerOverTabBar(e))
            ClearTabDragHighlight();
    }

    private void TabBarHost_DragLeave(object sender, DragEventArgs e)
    {
        if (!IsPointerOverTabBar(e))
            ClearTabDragHighlight();
    }

    private void TabHost_Drop(DragEventArgs e, PaneTab tab)
    {
        if (TryGetTabDragPayload(e.Data) is not null)
            CompleteTabMoveDrop(e, ResolveTabInsertIndex(tab, e));
        else
            CompleteFileTabDrop(e, tab);
    }

    private void TabHost_PreviewMouseLeftButtonDown(MouseButtonEventArgs e, PaneTab tab, Border host)
    {
        if (IsTabCloseButton(e.OriginalSource))
            return;

        _tabDragStartPoint = e.GetPosition(host);
        _tabDragPending = true;
        _tabDragSourceTab = tab;
        _tabDragSourceHost = host;
        host.CaptureMouse();
    }

    private void TabHost_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_tabDragPending || e.LeftButton != MouseButtonState.Pressed || _tabDragSourceTab is null)
            return;

        var host = _tabDragSourceHost;
        if (host is null)
            return;

        var position = e.GetPosition(host);
        if (Math.Abs(position.X - _tabDragStartPoint.X) < TabDragThreshold &&
            Math.Abs(position.Y - _tabDragStartPoint.Y) < TabDragThreshold)
        {
            return;
        }

        _tabDragPending = false;
        host.ReleaseMouseCapture();
        StartTabDrag(_tabDragSourceTab);
        _tabDragSourceTab = null;
        _tabDragSourceHost = null;
        e.Handled = true;
    }

    private void TabHost_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_tabDragPending || _tabDragSourceTab is null)
            return;

        TabSelected?.Invoke(this, new PaneTabEventArgs(_tabDragSourceTab));
        ResetTabDragState();
        e.Handled = true;
    }

    private void TabHost_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_tabDragPending)
            ResetTabDragState();
    }

    private void ResetTabDragState()
    {
        _tabDragPending = false;
        _tabDragSourceTab = null;
        _tabDragSourceHost?.ReleaseMouseCapture();
        _tabDragSourceHost = null;
    }

    private void StartTabDrag(PaneTab tab)
    {
        if (ReferenceEquals(PaneState?.ActiveTab, tab))
            tab.Path = PaneState.CurrentPath;

        var payload = new PaneTabDragPayload
        {
            Tab = tab,
            SourceControl = this
        };

        PaneTabDragSession.Begin(payload);

        var data = new DataObject();
        data.SetData(typeof(PaneTabDragPayload), payload);
        data.SetData("ProvixPaneTabDrag", payload);
        data.SetData(DataFormats.Text, payload.Tab.Id.ToString());

        try
        {
            DragDrop.DoDragDrop(TabBarHost, data, DragDropEffects.Move);
        }
        finally
        {
            PaneTabDragSession.End();
            RefreshTabBar();
        }
    }

    private void DirectoryPaneControl_PreviewDragOver(object sender, DragEventArgs e)
    {
        if (TryGetTabDragPayload(e.Data) is null)
            return;

        if (FindTabHostUnderPoint(e) is { Tab: var tab, Host: var host })
            PrepareTabMoveDrop(e, tab, host);
        else
            PrepareTabMoveDrop(e, null, null);
    }

    private void DirectoryPaneControl_PreviewDrop(object sender, DragEventArgs e)
    {
        if (TryGetTabDragPayload(e.Data) is null || e.Handled)
            return;

        var insertIndex = FindTabHostUnderPoint(e) is { Tab: var hoveredTab }
            ? ResolveTabInsertIndex(hoveredTab, e)
            : ResolveTabInsertIndexAtPoint(e);

        CompleteTabMoveDrop(e, insertIndex);
    }

    private static PaneTabDragPayload? TryGetTabDragPayload(IDataObject data)
    {
        if (PaneTabDragSession.Active is not null)
            return PaneTabDragSession.Active;

        if (data.GetDataPresent(typeof(PaneTabDragPayload)))
            return data.GetData(typeof(PaneTabDragPayload)) as PaneTabDragPayload;

        if (data.GetDataPresent("ProvixPaneTabDrag"))
            return data.GetData("ProvixPaneTabDrag") as PaneTabDragPayload;

        return null;
    }

    private void PaneSurface_DragOver(object sender, DragEventArgs e)
    {
        if (TryGetTabDragPayload(e.Data) is null)
            return;

        PrepareTabMoveDrop(e, null, null);
    }

    private void PaneSurface_Drop(object sender, DragEventArgs e)
    {
        if (TryGetTabDragPayload(e.Data) is null)
            return;

        var insertIndex = FindTabHostUnderPoint(e) is { Tab: var hoveredTab }
            ? ResolveTabInsertIndex(hoveredTab, e)
            : ResolveTabInsertIndexAtPoint(e);

        CompleteTabMoveDrop(e, insertIndex);
    }

    private bool PrepareFileTabDrop(DragEventArgs e, PaneTab? tab, Border? host)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return false;

        var sourcePaths = ExtractDroppedPaths(e.Data);
        if (sourcePaths.Length == 0)
            return false;

        var targetDirectory = ResolveTabDropTarget(tab);
        if (string.IsNullOrEmpty(targetDirectory))
        {
            e.Effects = DragDropEffects.None;
            ClearTabDragHighlight();
            return true;
        }

        e.Effects = DragDropEffects.Move;
        SetTabDragHighlight(host);
        e.Handled = true;
        return true;
    }

    private void PrepareTabMoveDrop(DragEventArgs e, PaneTab? tab, Border? host)
    {
        var payload = TryGetTabDragPayload(e.Data);
        if (payload is null)
            return;

        if (ReferenceEquals(payload.SourceControl, this) &&
            ReferenceEquals(payload.Tab, tab) &&
            PaneState?.Tabs.Count == 1)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        SetTabDragHighlight(host);
        e.Handled = true;
    }

    private void CompleteFileTabDrop(DragEventArgs e, PaneTab tab)
    {
        ClearTabDragHighlight();

        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return;

        var sourcePaths = ExtractDroppedPaths(e.Data);
        if (sourcePaths.Length == 0)
            return;

        var targetDirectory = ResolveTabDropTarget(tab);
        if (string.IsNullOrEmpty(targetDirectory))
            return;

        if (!ReferenceEquals(PaneState?.ActiveTab, tab))
            TabSelected?.Invoke(this, new PaneTabEventArgs(tab));

        FileDropRequested?.Invoke(this, new FileDropEventArgs
        {
            SourcePaths = sourcePaths,
            TargetDirectory = targetDirectory
        });

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void CompleteTabMoveDrop(DragEventArgs e, int insertIndex)
    {
        if (e.Handled || PaneTabDragSession.MoveCompleted)
            return;

        ClearTabDragHighlight();

        var payload = TryGetTabDragPayload(e.Data);
        if (payload is null)
            return;

        TabMoveRequested?.Invoke(this, new PaneTabMoveEventArgs
        {
            Tab = payload.Tab,
            SourceControl = payload.SourceControl,
            InsertIndex = insertIndex
        });

        e.Effects = DragDropEffects.Move;
        e.Handled = true;
    }

    private int ResolveTabInsertIndex(PaneTab hoveredTab, DragEventArgs e)
    {
        if (PaneState is null)
            return 0;

        var index = PaneState.Tabs.IndexOf(hoveredTab);
        if (index < 0)
            return PaneState.Tabs.Count;

        if (TabBarPanel.Children.Count <= index ||
            TabBarPanel.Children[index] is not FrameworkElement element)
        {
            return index;
        }

        var topLeft = element.TranslatePoint(new Point(0, 0), TabBarPanel);
        var point = e.GetPosition(TabBarPanel);
        var centerX = topLeft.X + element.ActualWidth / 2;
        return point.X > centerX ? index + 1 : index;
    }

    private int ResolveTabInsertIndexAtPoint(DragEventArgs e)
    {
        if (PaneState is null || PaneState.Tabs.Count == 0)
            return 0;

        var point = e.GetPosition(TabBarPanel);

        for (var i = 0; i < TabBarPanel.Children.Count; i++)
        {
            if (TabBarPanel.Children[i] is not FrameworkElement element)
                continue;

            var topLeft = element.TranslatePoint(new Point(0, 0), TabBarPanel);
            var centerX = topLeft.X + element.ActualWidth / 2;
            if (point.X < centerX)
                return i;
        }

        return PaneState.Tabs.Count;
    }

    private string? ResolveTabDropTarget(PaneTab? tab)
    {
        var path = tab?.Path;
        if (string.IsNullOrWhiteSpace(path))
            path = PaneState?.ActiveTab?.Path ?? PaneState?.CurrentPath;

        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var fullPath = Path.GetFullPath(path);
            return Directory.Exists(fullPath) ? fullPath : null;
        }
        catch
        {
            return null;
        }
    }

    private void SetTabDragHighlight(Border? host)
    {
        if (ReferenceEquals(_tabDragHighlightHost, host))
            return;

        ClearTabDragHighlight();
        _tabDragHighlightHost = host;

        if (host is null)
            return;

        host.BorderBrush = Application.Current.FindResource("AccentBrush") as Brush;
        host.Background = Application.Current.FindResource("HoverBrush") as Brush;
    }

    private void ClearTabDragHighlight()
    {
        if (_tabDragHighlightHost is null)
            return;

        _tabDragHighlightHost = null;
        RefreshTabBar();
    }

    private bool IsPointerOverTabBar(DragEventArgs e)
    {
        var position = e.GetPosition(TabBarHost);
        return position.X >= 0 && position.Y >= 0 &&
               position.X <= TabBarHost.ActualWidth &&
               position.Y <= TabBarHost.ActualHeight;
    }

    private (PaneTab Tab, Border Host)? FindTabHostUnderPoint(DragEventArgs e)
    {
        var point = e.GetPosition(TabBarPanel);
        var hit = VisualTreeHelper.HitTest(TabBarPanel, point);
        var border = FindTabHostBorder(hit?.VisualHit);
        if (border?.Tag is PaneTab tab)
            return (tab, border);

        return null;
    }

    private static Border? FindTabHostBorder(DependencyObject? source)
    {
        var current = source;
        while (current is not null)
        {
            if (current is Border { Tag: PaneTab } border)
                return border;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void TabCloseButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: PaneTab tab })
            return;

        ResetTabDragState();
        TabCloseRequested?.Invoke(this, new PaneTabEventArgs(tab));
        e.Handled = true;
    }

    private static bool IsTabCloseButton(object? source)
    {
        var current = source as DependencyObject;
        while (current is not null)
        {
            if (current is Button { Tag: PaneTab })
                return true;

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void NewTabButton_Click(object sender, RoutedEventArgs e) =>
        NewTabRequested?.Invoke(this, e);

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        FileListSelectionChanged?.Invoke(sender, e);

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
        if (TryGetTabDragPayload(e.Data) is not null)
        {
            e.Effects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

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

    private void RenameMenuItem_Click(object sender, RoutedEventArgs e) =>
        RenameRequested?.Invoke(sender, e);

    private void DeleteMenuItem_Click(object sender, RoutedEventArgs e) =>
        DeleteRequested?.Invoke(sender, e);

    private void FileList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            e.Handled = true;
            RenameRequested?.Invoke(sender, e);
        }
    }

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

    private bool _gitFeaturesAvailable = true;
    private bool _aiFeaturesAvailable = true;

    public void SetAiFeaturesAvailable(bool available)
    {
        _aiFeaturesAvailable = available;

        var visibility = available ? Visibility.Visible : Visibility.Collapsed;
        AiMenuSeparator.Visibility = visibility;
        AiExecuteQueryMenuItem.Visibility = visibility;
    }

    public void SetGitFeaturesAvailable(bool available)
    {
        _gitFeaturesAvailable = available;

        var visibility = available ? Visibility.Visible : Visibility.Collapsed;
        GitMenuSeparator.Visibility = visibility;
        GitInitMenuItem.Visibility = visibility;
        GitCommitMenuItem.Visibility = visibility;
        GitAmendMenuItem.Visibility = visibility;
        GitHistoryMenuItem.Visibility = visibility;

        if (!available)
            GitStatusBar.Visibility = Visibility.Collapsed;
    }

    public void UpdateGitStatus(bool isRepo, string branch, int changeCount)
    {
        if (!_gitFeaturesAvailable)
        {
            GitStatusBar.Visibility = Visibility.Collapsed;
            return;
        }

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

    private void OpenInOtherPaneMenuItem_Click(object sender, RoutedEventArgs e) =>
        OpenInOtherPaneRequested?.Invoke(this, e);

    private void CompareFoldersMenuItem_Click(object sender, RoutedEventArgs e) =>
        CompareFoldersRequested?.Invoke(this, e);

    private void AddBookmarkMenuItem_Click(object sender, RoutedEventArgs e) =>
        AddBookmarkRequested?.Invoke(this, e);

    private void RemoveBookmarkMenuItem_Click(object sender, RoutedEventArgs e) =>
        RemoveBookmarkRequested?.Invoke(this, e);

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
