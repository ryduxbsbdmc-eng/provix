using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace FileExplorer.Helpers;

public static class ScrollBarSeekHelper
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ScrollBarSeekHelper),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollBar scrollBar)
            return;

        if (e.NewValue is true)
        {
            scrollBar.Loaded += ScrollBar_Loaded;
            scrollBar.PreviewMouseLeftButtonDown += ScrollBar_PreviewMouseLeftButtonDown;
        }
        else
        {
            scrollBar.Loaded -= ScrollBar_Loaded;
            scrollBar.PreviewMouseLeftButtonDown -= ScrollBar_PreviewMouseLeftButtonDown;
        }
    }

    private static void ScrollBar_Loaded(object sender, RoutedEventArgs e)
    {
        // nothing needed on load, template is ready after this
    }

    private static void ScrollBar_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollBar scrollBar)
            return;

        if (scrollBar.Template is null)
            return;

        var track = scrollBar.Template.FindName("PART_Track", scrollBar) as Track;
        if (track is null)
            return;

        // Let normal thumb dragging proceed.
        if (track.Thumb is not null && track.Thumb.IsMouseOver)
            return;

        var pos = e.GetPosition(track);

        double ratio;
        if (scrollBar.Orientation == Orientation.Vertical)
        {
            if (track.ActualHeight <= 0)
                return;
            ratio = pos.Y / track.ActualHeight;
        }
        else
        {
            if (track.ActualWidth <= 0)
                return;
            ratio = pos.X / track.ActualWidth;
        }

        ratio = Math.Clamp(ratio, 0.0, 1.0);

        var targetValue = scrollBar.Minimum + ratio * (scrollBar.Maximum - scrollBar.Minimum);
        targetValue = Math.Clamp(targetValue, scrollBar.Minimum, scrollBar.Maximum);

        SeekScrollViewer(scrollBar, targetValue);

        e.Handled = true;
    }

    private static void SeekScrollViewer(ScrollBar scrollBar, double targetValue)
    {
        // Walk up the visual tree to find the ScrollViewer that owns this scrollbar.
        var parent = scrollBar.TemplatedParent as ScrollViewer
                     ?? FindScrollViewerParent(scrollBar);

        if (parent is null)
        {
            scrollBar.Value = targetValue;
            return;
        }

        if (scrollBar.Orientation == Orientation.Vertical)
            SmoothScrollHelper.SeekTo(parent, targetValue, horizontal: false);
        else
            SmoothScrollHelper.SeekTo(parent, targetValue, horizontal: true);
    }

    private static ScrollViewer? FindScrollViewerParent(DependencyObject? element)
    {
        if (element is null)
            return null;

        var parent = System.Windows.Media.VisualTreeHelper.GetParent(element);
        if (parent is ScrollViewer sv)
            return sv;

        return FindScrollViewerParent(parent);
    }
}
