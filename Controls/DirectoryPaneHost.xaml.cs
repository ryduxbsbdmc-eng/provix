using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FileExplorer.Controls;

public partial class DirectoryPaneHost : UserControl
{
    private readonly Dictionary<DirectoryPaneControl, FrameworkElement> _paneWrappers = new();

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
        _paneWrappers.Clear();
        PaneHostGrid.Children.Clear();
        PaneHostGrid.ColumnDefinitions.Clear();
    }

    public FrameworkElement AddPaneColumn(DirectoryPaneControl pane, bool addSplitterAfter, bool prepareEntrance = false)
    {
        var columnIndex = PaneHostGrid.ColumnDefinitions.Count;

        PaneHostGrid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
            MinWidth = 220
        });

        var wrapper = new Grid
        {
            ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
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

        if (!addSplitterAfter)
            return wrapper;

        PaneHostGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2) });

        var splitter = new GridSplitter
        {
            Style = (Style)Application.Current.FindResource("PaneSplitterStyle"),
            ResizeDirection = GridResizeDirection.Columns
        };
        Grid.SetColumn(splitter, columnIndex + 1);
        Grid.SetRow(splitter, 0);
        PaneHostGrid.Children.Add(splitter);

        return wrapper;
    }
}
