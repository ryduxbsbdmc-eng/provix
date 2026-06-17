using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace FileExplorer.Controls;

public partial class DirectoryPaneHost : UserControl
{
    /// <summary>
    /// Minimum pane width. Panes stretch equally to fill the whole viewport; once equal widths
    /// would drop below this value the host switches to horizontal scrolling instead.
    /// </summary>
    private const double MinPaneColumnWidth = 300;

    private const double SplitterColumnWidth = 2;

    private readonly Dictionary<DirectoryPaneControl, FrameworkElement> _paneWrappers = new();
    private int _paneColumnCount;
    private bool _deferredRefreshQueued;

    public DirectoryPaneHost()
    {
        InitializeComponent();
        PaneHostGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
    }

    public Grid PaneHostGridInternal => PaneHostGrid;

    public FrameworkElement? GetPaneWrapper(DirectoryPaneControl pane) =>
        _paneWrappers.GetValueOrDefault(pane);

    public void ClearPanes()
    {
        foreach (var (pane, wrapper) in _paneWrappers)
        {
            if (wrapper is Panel panel && panel.Children.Contains(pane))
                panel.Children.Remove(pane);
        }

        _paneWrappers.Clear();
        _paneColumnCount = 0;
        PaneHostGrid.Children.Clear();
        PaneHostGrid.ColumnDefinitions.Clear();
        PaneHostGrid.Width = double.NaN;
    }

    public FrameworkElement AddPaneColumn(DirectoryPaneControl pane, bool addSplitterAfter, bool prepareEntrance = false)
    {
        var wrapper = AttachPaneWrapper(pane, prepareEntrance);

        if (addSplitterAfter)
            AppendSplitterColumn();

        RefreshLayout();
        QueueDeferredRefresh();
        return wrapper;
    }

    /// <summary>
    /// Adds a single pane to the right of the existing panes without rebuilding the whole host.
    /// Much cheaper than <see cref="ClearPanes"/> + re-adding every pane, so the "+" button feels instant.
    /// </summary>
    public FrameworkElement AppendPaneColumn(DirectoryPaneControl pane, bool prepareEntrance = false)
    {
        // The previous last pane has no trailing splitter, so insert one before the new pane.
        if (_paneColumnCount > 0)
            AppendSplitterColumn();

        var wrapper = AttachPaneWrapper(pane, prepareEntrance);

        RefreshLayout();
        QueueDeferredRefresh();
        return wrapper;
    }

    private FrameworkElement AttachPaneWrapper(DirectoryPaneControl pane, bool prepareEntrance)
    {
        DetachFromParent(pane);

        var columnIndex = PaneHostGrid.ColumnDefinitions.Count;

        PaneHostGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
            MinWidth = MinPaneColumnWidth
        });

        var wrapper = new Grid
        {
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            MinWidth = MinPaneColumnWidth
        };

        if (prepareEntrance)
        {
            wrapper.Opacity = 0;
            wrapper.LayoutTransform = new ScaleTransform(0, 1);
        }

        wrapper.Children.Add(pane);
        _paneWrappers[pane] = wrapper;

        Grid.SetColumn(wrapper, columnIndex);
        Grid.SetRow(wrapper, 0);
        PaneHostGrid.Children.Add(wrapper);
        _paneColumnCount++;

        return wrapper;
    }

    private void AppendSplitterColumn()
    {
        var columnIndex = PaneHostGrid.ColumnDefinitions.Count;
        PaneHostGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SplitterColumnWidth) });

        var splitter = new GridSplitter
        {
            Style = (Style)Application.Current.FindResource("PaneSplitterStyle"),
            ResizeDirection = GridResizeDirection.Columns
        };
        Grid.SetColumn(splitter, columnIndex);
        Grid.SetRow(splitter, 0);
        PaneHostGrid.Children.Add(splitter);
    }

    private void DirectoryPaneHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RefreshLayout();

    private void PaneScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RefreshLayout();

    /// <summary>
    /// Runs the layout now and schedules one more pass once the visual tree has settled. Adding or
    /// removing a pane does not change the host size, so no size-changed event fires to correct a
    /// layout that was computed against a stale viewport width; this guarantees the final fit.
    /// </summary>
    private void QueueDeferredRefresh()
    {
        if (_deferredRefreshQueued)
            return;

        _deferredRefreshQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _deferredRefreshQueued = false;
            RefreshLayout();
        }));
    }

    private void RefreshLayout()
    {
        if (_paneColumnCount == 0)
            return;

        var available = PaneScrollViewer.ViewportWidth;
        if (available <= 0 || double.IsNaN(available))
            available = PaneScrollViewer.ActualWidth;
        if (available <= 0 || double.IsNaN(available))
            available = ActualWidth;

        if (available <= 0)
            return;

        var splitterCount = Math.Max(0, _paneColumnCount - 1);
        var totalMin = _paneColumnCount * MinPaneColumnWidth + splitterCount * SplitterColumnWidth;

        if (totalMin <= available)
        {
            // Panes fit at or above the minimum width: stretch them equally to fill the entire
            // viewport edge-to-edge, with no leftover gap on the right and no clipping.
            PaneHostGrid.Width = available;
            PaneHostGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
            ApplyPaneColumnWidths(new GridLength(1, GridUnitType.Star));
            return;
        }

        // Equal widths would drop below the minimum, so pin each pane to MinPaneColumnWidth and let
        // the horizontal scrollbar reach the last pane fully (grid width matches content exactly).
        PaneHostGrid.Width = totalMin;
        PaneHostGrid.HorizontalAlignment = HorizontalAlignment.Left;
        ApplyPaneColumnWidths(new GridLength(MinPaneColumnWidth, GridUnitType.Pixel));
    }

    private void ApplyPaneColumnWidths(GridLength paneWidth)
    {
        for (var i = 0; i < PaneHostGrid.ColumnDefinitions.Count; i++)
        {
            var column = PaneHostGrid.ColumnDefinitions[i];
            if (IsSplitterColumn(i))
            {
                column.Width = new GridLength(SplitterColumnWidth);
                column.MinWidth = SplitterColumnWidth;
                continue;
            }

            column.Width = paneWidth;
            column.MinWidth = MinPaneColumnWidth;
        }
    }

    private bool IsSplitterColumn(int columnIndex) =>
        columnIndex % 2 == 1;

    private static void DetachFromParent(UIElement element)
    {
        switch (LogicalTreeHelper.GetParent(element))
        {
            case Panel panel when panel.Children.Contains(element):
                panel.Children.Remove(element);
                break;
            case Decorator decorator when ReferenceEquals(decorator.Child, element):
                decorator.Child = null;
                break;
            case ContentControl contentControl when ReferenceEquals(contentControl.Content, element):
                contentControl.Content = null;
                break;
        }
    }
}
