using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileExplorer.Controls;

public partial class DirectoryPaneHost : UserControl
{
    private const double MinPaneColumnWidth = 300;
    private const double SplitterColumnWidth = 2;

    private readonly Dictionary<DirectoryPaneControl, FrameworkElement> _paneWrappers = new();
    private int _paneColumnCount;

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

        if (!addSplitterAfter)
        {
            RefreshLayout();
            return wrapper;
        }

        PaneHostGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(SplitterColumnWidth) });

        var splitter = new GridSplitter
        {
            Style = (Style)Application.Current.FindResource("PaneSplitterStyle"),
            ResizeDirection = GridResizeDirection.Columns
        };
        Grid.SetColumn(splitter, columnIndex + 1);
        Grid.SetRow(splitter, 0);
        PaneHostGrid.Children.Add(splitter);

        RefreshLayout();
        return wrapper;
    }

    private void DirectoryPaneHost_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RefreshLayout();

    private void PaneScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RefreshLayout();

    private void RefreshLayout()
    {
        if (_paneColumnCount == 0)
            return;

        var available = PaneScrollViewer.ViewportWidth;
        if (available <= 0 || double.IsNaN(available))
            available = ActualWidth;

        if (available <= 0)
            return;

        var splitterCount = Math.Max(0, _paneColumnCount - 1);
        var totalMin = _paneColumnCount * MinPaneColumnWidth + splitterCount * SplitterColumnWidth;

        if (totalMin > available)
        {
            PaneHostGrid.Width = totalMin;
            PaneHostGrid.HorizontalAlignment = HorizontalAlignment.Left;
            ApplyPaneColumnWidths(new GridLength(MinPaneColumnWidth, GridUnitType.Pixel));
            return;
        }

        PaneHostGrid.Width = available;
        PaneHostGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
        ApplyPaneColumnWidths(new GridLength(1, GridUnitType.Star));
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
