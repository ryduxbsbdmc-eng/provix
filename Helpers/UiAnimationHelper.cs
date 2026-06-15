using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FileExplorer.Helpers;

public static class UiAnimationHelper
{
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
        var storyboard = CloneStoryboard("PaneExitStoryboard", element);
        storyboard.Completed += (_, _) => onComplete();
        element.BeginStoryboard(storyboard, HandoffBehavior.SnapshotAndReplace, true);
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
        var storyboard = CloneStoryboard("OverlayHideStoryboard", overlay);
        storyboard.Completed += (_, _) =>
        {
            overlay.Visibility = Visibility.Collapsed;
            overlay.ClearValue(UIElement.OpacityProperty);
            overlay.RenderTransform = Transform.Identity;
            onComplete?.Invoke();
        };
        overlay.BeginStoryboard(storyboard, HandoffBehavior.SnapshotAndReplace, true);
    }

    public static DoubleAnimation CreateDimFadeAnimation(double from, double to, bool fadeIn)
    {
        var durationKey = fadeIn ? "DimFadeInDuration" : "DimFadeOutDuration";
        var duration = (Duration)Application.Current.FindResource(durationKey);
        return new DoubleAnimation(from, to, duration)
        {
            EasingFunction = (IEasingFunction)Application.Current.FindResource("EaseOutQuadratic")
        };
    }

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

    private static void RunStoryboard(FrameworkElement target, string resourceKey)
    {
        var storyboard = CloneStoryboard(resourceKey, target);
        target.BeginStoryboard(storyboard, HandoffBehavior.SnapshotAndReplace, true);
    }

    private static Storyboard CloneStoryboard(string resourceKey, FrameworkElement target)
    {
        var template = (Storyboard)Application.Current.FindResource(resourceKey);
        var storyboard = template.Clone();
        storyboard.SetTarget(target);
        return storyboard;
    }

    private static void SetTarget(this Storyboard storyboard, FrameworkElement target)
    {
        foreach (Timeline timeline in storyboard.Children)
            Storyboard.SetTarget(timeline, target);
    }
}
