using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FileExplorer.Helpers;

public static class UiAnimationHelper
{
    private static readonly Dictionary<string, Storyboard> StoryboardCache = new(StringComparer.Ordinal);
    private static readonly List<RowHeightAnimation> ActiveRowAnimations = [];
    private static readonly Dictionary<FrameworkElement, Storyboard> ActivePulseStoryboards = new();
    private static bool _rowAnimationHooked;

    public static void AnimatePaneEntrance(FrameworkElement element)
    {
        EnsurePaneLayoutTransform(element);
        element.Opacity = 0;
        if (element.LayoutTransform is ScaleTransform scale)
            scale.ScaleX = 0;

        RunStoryboard(element, "PaneEntranceStoryboard");
    }

    public static void AnimatePaneExit(FrameworkElement element, Action onComplete)
    {
        EnsurePaneLayoutTransform(element);
        RunStoryboard(element, "PaneExitStoryboard", onComplete);
    }

    public static void AnimateTerminalEntrance(FrameworkElement element, Action? onComplete = null)
    {
        element.Opacity = 0;
        RunStoryboard(element, "TerminalEntranceStoryboard", onComplete);
    }

    public static void AnimateTerminalExit(FrameworkElement element, Action onComplete)
    {
        RunStoryboard(element, "TerminalExitStoryboard", () =>
        {
            element.ClearValue(UIElement.OpacityProperty);
            onComplete();
        });
    }

    public static void AnimateTerminalLayout(
        RowDefinition splitterRow,
        double splitterFrom,
        double splitterTo,
        RowDefinition panelRow,
        double panelFrom,
        double panelTo,
        bool fadeIn,
        Action? onComplete = null)
    {
        var pending = 2;

        void RowDone()
        {
            if (--pending > 0)
                return;

            onComplete?.Invoke();
        }

        AnimateRowHeight(splitterRow, splitterFrom, splitterTo, fadeIn, RowDone);
        AnimateRowHeight(panelRow, panelFrom, panelTo, fadeIn, RowDone);
    }

    public static void FadeIn(FrameworkElement element, Action? onComplete = null) =>
        RunStoryboard(element, "FadeInStoryboard", onComplete);

    public static void FadeOut(FrameworkElement element, Action? onComplete = null) =>
        RunStoryboard(element, "FadeOutStoryboard", () =>
        {
            element.ClearValue(UIElement.OpacityProperty);
            onComplete?.Invoke();
        });

    public static void AnimateRowHeight(
        RowDefinition row,
        double fromPixels,
        double toPixels,
        bool fadeIn,
        Action? onComplete = null)
    {
        if (Math.Abs(fromPixels - toPixels) < 0.5)
        {
            row.Height = new GridLength(toPixels);
            onComplete?.Invoke();
            return;
        }

        ActiveRowAnimations.Add(new RowHeightAnimation(
            row,
            fromPixels,
            toPixels,
            GetDuration("TerminalTransitionDuration"),
            GetEasing(fadeIn),
            onComplete));

        EnsureRowAnimationLoop();
    }

    public static void StartPulse(FrameworkElement element)
    {
        StopPulse(element);

        var storyboard = CloneStoryboard("PulseLoopStoryboard", element);
        ActivePulseStoryboards[element] = storyboard;
        element.BeginStoryboard(storyboard, HandoffBehavior.SnapshotAndReplace, true);
    }

    public static void StopPulse(FrameworkElement element)
    {
        if (!ActivePulseStoryboards.Remove(element, out var storyboard))
            return;

        storyboard.Stop(element);
        element.ClearValue(UIElement.OpacityProperty);
    }

    public static void ShowOverlay(FrameworkElement overlay)
    {
        overlay.Visibility = Visibility.Visible;
        PrepareOverlayTransform(overlay);
        overlay.Opacity = 0;
        RunStoryboard(overlay, "OverlayShowStoryboard");
    }

    public static void HideOverlay(FrameworkElement overlay, Action? onComplete = null)
    {
        if (overlay.Visibility != Visibility.Visible)
        {
            onComplete?.Invoke();
            return;
        }

        PrepareOverlayTransform(overlay);
        RunStoryboard(overlay, "OverlayHideStoryboard", () =>
        {
            overlay.Visibility = Visibility.Collapsed;
            overlay.ClearValue(UIElement.OpacityProperty);
            overlay.RenderTransform = Transform.Identity;
            onComplete?.Invoke();
        });
    }

    public static DoubleAnimation CreateDimFadeAnimation(double from, double to, bool fadeIn) =>
        new(from, to, GetDuration(fadeIn ? "DimFadeInDuration" : "DimFadeOutDuration"))
        {
            EasingFunction = GetEasing(fadeIn: false)
        };

    public static Action CreateParallelCallback(int stepCount, Action onAllComplete)
    {
        if (stepCount <= 0)
        {
            onAllComplete();
            return static () => { };
        }

        var remaining = stepCount;
        return () =>
        {
            if (--remaining > 0)
                return;

            onAllComplete();
        };
    }

    private static void EnsureRowAnimationLoop()
    {
        if (_rowAnimationHooked)
            return;

        CompositionTarget.Rendering += OnRowAnimationsRendering;
        _rowAnimationHooked = true;
    }

    private static void OnRowAnimationsRendering(object? sender, EventArgs e)
    {
        if (ActiveRowAnimations.Count == 0)
        {
            CompositionTarget.Rendering -= OnRowAnimationsRendering;
            _rowAnimationHooked = false;
            return;
        }

        var now = Environment.TickCount64;

        for (var i = ActiveRowAnimations.Count - 1; i >= 0; i--)
        {
            var animation = ActiveRowAnimations[i];
            var progress = Math.Min(1.0, (now - animation.StartTicks) / animation.TotalMs);
            var eased = animation.Easing.Ease(progress);
            animation.Row.Height = new GridLength(animation.From + (animation.To - animation.From) * eased);

            if (progress < 1.0)
                continue;

            animation.Row.Height = new GridLength(animation.To);
            ActiveRowAnimations.RemoveAt(i);
            animation.OnComplete?.Invoke();
        }
    }

    private static void RunStoryboard(FrameworkElement target, string resourceKey, Action? onComplete = null)
    {
        var storyboard = CloneStoryboard(resourceKey, target);
        if (onComplete is not null)
            storyboard.Completed += (_, _) => onComplete();

        target.BeginStoryboard(storyboard, HandoffBehavior.SnapshotAndReplace, true);
    }

    private static Storyboard CloneStoryboard(string resourceKey, FrameworkElement target)
    {
        if (!StoryboardCache.TryGetValue(resourceKey, out var template))
        {
            template = (Storyboard)Application.Current.FindResource(resourceKey);
            StoryboardCache[resourceKey] = template;
        }

        var storyboard = template.Clone();
        storyboard.SetTarget(target);
        return storyboard;
    }

    private static Duration GetDuration(string resourceKey) =>
        (Duration)Application.Current.FindResource(resourceKey);

    private static IEasingFunction GetEasing(bool fadeIn) =>
        (IEasingFunction)Application.Current.FindResource(fadeIn ? "EaseOutCubic" : "EaseOutQuadratic");

    private static void EnsurePaneLayoutTransform(FrameworkElement element)
    {
        if (element.LayoutTransform is not ScaleTransform)
            element.LayoutTransform = new ScaleTransform(1, 1);
    }

    private static void PrepareOverlayTransform(FrameworkElement element)
    {
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        if (element.RenderTransform is not ScaleTransform)
            element.RenderTransform = new ScaleTransform(1, 1);
    }

    private static void SetTarget(this Storyboard storyboard, FrameworkElement target)
    {
        foreach (Timeline timeline in storyboard.Children)
            Storyboard.SetTarget(timeline, target);
    }

    private sealed class RowHeightAnimation(
        RowDefinition row,
        double from,
        double to,
        Duration duration,
        IEasingFunction easing,
        Action? onComplete)
    {
        public RowDefinition Row { get; } = row;
        public double From { get; } = from;
        public double To { get; } = to;
        public IEasingFunction Easing { get; } = easing;
        public Action? OnComplete { get; } = onComplete;
        public long StartTicks { get; } = Environment.TickCount64;
        public double TotalMs { get; } = duration.TimeSpan.TotalMilliseconds;
    }
}
