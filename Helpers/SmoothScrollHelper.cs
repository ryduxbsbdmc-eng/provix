using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FileExplorer.Services;

namespace FileExplorer.Helpers;

public static class SmoothScrollHelper
{
    private const double BaseWheelDivisor = 3.0;
    private const double ScrollDurationMs = 150;

    private static readonly List<ScrollAnimation> ActiveAnimations = [];
    private static readonly ConditionalWeakTable<DependencyObject, ScrollViewer?> ScrollViewerCache = new();
    private static bool _renderHooked;

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(SmoothScrollHelper),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        if (e.NewValue is true)
        {
            element.Loaded += Element_Loaded;
            element.PreviewMouseWheel += Element_PreviewMouseWheel;
        }
        else
        {
            element.Loaded -= Element_Loaded;
            element.PreviewMouseWheel -= Element_PreviewMouseWheel;
            ScrollViewerCache.Remove(element);
        }
    }

    private static void Element_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject element)
            ScrollViewerCache.Remove(element);
    }

    private static void Element_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            return;

        if (sender is not DependencyObject owner)
            return;

        var scrollViewer = ResolveScrollViewer(owner);
        if (scrollViewer is null)
            return;

        var useHorizontal = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
                            && scrollViewer.ScrollableWidth > 0;

        if (useHorizontal)
        {
            if (scrollViewer.ScrollableWidth <= 0)
                return;

            var target = Clamp(
                scrollViewer.HorizontalOffset - e.Delta / GetWheelDivisor(),
                0,
                scrollViewer.ScrollableWidth);

            if (Math.Abs(target - scrollViewer.HorizontalOffset) < 0.5)
                return;

            e.Handled = true;
            StartAnimation(scrollViewer, target, horizontal: true);
            return;
        }

        if (scrollViewer.ScrollableHeight <= 0)
            return;

        var verticalTarget = Clamp(
            scrollViewer.VerticalOffset - e.Delta / GetWheelDivisor(),
            0,
            scrollViewer.ScrollableHeight);

        if (Math.Abs(verticalTarget - scrollViewer.VerticalOffset) < 0.5)
            return;

        e.Handled = true;
        StartAnimation(scrollViewer, verticalTarget, horizontal: false);
    }

    private static ScrollViewer? ResolveScrollViewer(DependencyObject owner)
    {
        if (owner is ScrollViewer scrollViewer)
            return scrollViewer;

        if (ScrollViewerCache.TryGetValue(owner, out var cached))
            return cached;

        var found = FindScrollViewer(owner);
        if (found is not null)
            ScrollViewerCache.Add(owner, found);

        return found;
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject? owner)
    {
        if (owner is null)
            return null;

        if (owner is ScrollViewer scrollViewer)
            return scrollViewer;

        if (owner is Control { Template: not null } control)
        {
            if (control.Template.FindName("PART_ContentHost", control) is ScrollViewer templateViewer)
                return templateViewer;
        }

        return FindScrollViewerInVisualTree(owner);
    }

    private static ScrollViewer? FindScrollViewerInVisualTree(DependencyObject? source)
    {
        if (source is null)
            return null;

        if (source is ScrollViewer viewer)
            return viewer;

        var count = VisualTreeHelper.GetChildrenCount(source);
        for (var i = 0; i < count; i++)
        {
            var found = FindScrollViewerInVisualTree(VisualTreeHelper.GetChild(source, i));
            if (found is not null)
                return found;
        }

        return null;
    }

    private static void StartAnimation(ScrollViewer scrollViewer, double targetOffset, bool horizontal)
    {
        for (var i = ActiveAnimations.Count - 1; i >= 0; i--)
        {
            var animation = ActiveAnimations[i];
            if (animation.Viewer == scrollViewer && animation.IsHorizontal == horizontal)
                ActiveAnimations.RemoveAt(i);
        }

        var from = horizontal ? scrollViewer.HorizontalOffset : scrollViewer.VerticalOffset;
        ActiveAnimations.Add(new ScrollAnimation(scrollViewer, from, targetOffset, horizontal));
        EnsureRenderHook();
    }

    private static void EnsureRenderHook()
    {
        if (_renderHooked)
            return;

        CompositionTarget.Rendering += OnRendering;
        _renderHooked = true;
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        if (ActiveAnimations.Count == 0)
        {
            CompositionTarget.Rendering -= OnRendering;
            _renderHooked = false;
            return;
        }

        var now = Environment.TickCount64;

        for (var i = ActiveAnimations.Count - 1; i >= 0; i--)
        {
            var animation = ActiveAnimations[i];
            var progress = Math.Min(1.0, (now - animation.StartTicks) / ScrollDurationMs);
            var eased = EaseOut(progress);
            var value = animation.From + (animation.To - animation.From) * eased;

            if (animation.IsHorizontal)
                animation.Viewer.ScrollToHorizontalOffset(value);
            else
                animation.Viewer.ScrollToVerticalOffset(value);

            if (progress < 1.0)
                continue;

            if (animation.IsHorizontal)
                animation.Viewer.ScrollToHorizontalOffset(animation.To);
            else
                animation.Viewer.ScrollToVerticalOffset(animation.To);

            ActiveAnimations.RemoveAt(i);
        }
    }

    private static double EaseOut(double progress) =>
        1.0 - Math.Pow(1.0 - progress, 2.0);

    private static double GetWheelDivisor()
    {
        var sensitivity = SettingsManager.Instance.Current.ScrollSensitivity;
        sensitivity = Math.Clamp(
            sensitivity,
            SettingsManager.MinScrollSensitivity,
            SettingsManager.MaxScrollSensitivity);

        return BaseWheelDivisor / sensitivity;
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Max(min, Math.Min(max, value));

    private sealed class ScrollAnimation(
        ScrollViewer viewer,
        double from,
        double to,
        bool isHorizontal)
    {
        public ScrollViewer Viewer { get; } = viewer;
        public double From { get; } = from;
        public double To { get; } = to;
        public bool IsHorizontal { get; } = isHorizontal;
        public long StartTicks { get; } = Environment.TickCount64;
    }
}
