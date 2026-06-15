using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FileExplorer.Helpers;
using FileExplorer.Services;
using Microsoft.Web.WebView2.Core;

namespace FileExplorer;

public partial class MainWindow
{
    private string? _mediaViewerPath;
    private bool _isMediaViewerOpen;
    private bool _isMediaSliderDragging;
    private bool _mediaIsPlaying;
    private bool _usesWebViewVideo;
    private int _mediaViewerMediaWidth;
    private int _mediaViewerMediaHeight;
    private DispatcherTimer? _mediaPositionTimer;

    private const double MediaViewerHeaderHeight = 48;
    private const double MediaViewerBorderChrome = 2;
    private const double WebViewControlsHeight = 92;
    private const double Mp4ControlsHeight = 76;
    private const double MediaViewerMinContentWidth = 320;
    private const double MediaViewerMinContentHeight = 180;

    private async Task OpenMediaViewerAsync(string filePath)
    {
        if (!MediaViewerHelper.TryGetMediaKind(filePath, out var kind))
            return;

        if (!MediaViewerHelper.IsWithinSizeLimit(filePath, kind, out var fileSize))
        {
            OpenFileWithDefaultApplication(filePath);
            StatusText.Text =
                $"Opened with default app ({TextFileHelper.FormatByteSize(fileSize)} exceeds built-in media limit).";
            return;
        }

        CloseEditor(animate: false);
        CloseArchiveViewer(animate: false);
        CloseMediaViewer(animate: false);

        _mediaViewerPath = filePath;
        _isMediaViewerOpen = true;

        var loc = LocalizationManager.Instance;
        MediaViewerFileNameText.Text = Path.GetFileName(filePath);
        HideMediaViewerError();
        ResetMediaViewerContent();

        MediaViewerOverlay.Visibility = Visibility.Visible;
        PushChromeDimOverlay();
        UiAnimationHelper.ShowOverlay(MediaViewerPanel);

        try
        {
            if (kind == MediaViewerKind.Image)
                await ShowMediaImageAsync(filePath);
            else
                await ShowMediaVideoAsync(filePath);

            _navigationHistory.RecordFile(filePath);
            StatusText.Text = $"{loc["UI_MediaViewerTitle"]}: {Path.GetFileName(filePath)}";
        }
        catch (Exception ex)
        {
            ShowMediaViewerError(ex.Message);
            StatusText.Text = loc["UI_MediaViewerOpenFailed"];
        }
    }

    private async Task ShowMediaImageAsync(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var bitmap = await Dispatcher.InvokeAsync(() =>
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(fullPath, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.EndInit();
            if (image.CanFreeze)
                image.Freeze();
            return image;
        });

        MediaViewerImageHost.Visibility = Visibility.Visible;
        MediaViewerImage.Source = bitmap;
        MediaViewerVideoHost.Visibility = Visibility.Collapsed;
        MediaViewerControlsPanel.Visibility = Visibility.Collapsed;

        if (bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
            FitMediaViewerPanel(bitmap.PixelWidth, bitmap.PixelHeight);
    }

    private async Task ShowMediaVideoAsync(string filePath)
    {
        MediaViewerImageHost.Visibility = Visibility.Collapsed;
        MediaViewerVideoHost.Visibility = Visibility.Visible;

        if (WebViewVideoPlayer.UsesWebViewPlayback(filePath) &&
            ExternalToolsService.IsAvailable(ExternalTool.WebView2))
            await ShowWebViewVideoAsync(filePath);
        else
            ShowMediaElementVideo(filePath);
    }

    private async Task ShowWebViewVideoAsync(string filePath)
    {
        _usesWebViewVideo = true;
        _mediaIsPlaying = false;

        MediaViewerPlayer.Visibility = Visibility.Collapsed;
        MediaViewerWebPlayer.Visibility = Visibility.Visible;
        MediaViewerControlsPanel.Visibility = Visibility.Collapsed;

        await WebViewVideoPlayer.PlayLocalFileAsync(MediaViewerWebPlayer, filePath);

        if (MediaViewerWebPlayer.CoreWebView2 is not null)
        {
            MediaViewerWebPlayer.CoreWebView2.WebMessageReceived -= MediaViewerWebPlayer_WebMessageReceived;
            MediaViewerWebPlayer.CoreWebView2.WebMessageReceived += MediaViewerWebPlayer_WebMessageReceived;
        }
    }

    private void ShowMediaElementVideo(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        _usesWebViewVideo = false;
        _mediaIsPlaying = false;

        MediaViewerWebPlayer.Visibility = Visibility.Collapsed;
        MediaViewerPlayer.Visibility = Visibility.Visible;
        MediaViewerControlsPanel.Visibility = Visibility.Visible;

        MediaViewerPositionSlider.Value = 0;
        MediaViewerPositionSlider.Maximum = 1;
        UpdateMediaTimeText(TimeSpan.Zero, TimeSpan.Zero);

        MediaViewerPlayer.Source = new Uri(fullPath, UriKind.Absolute);
        MediaViewerPlayer.Position = TimeSpan.Zero;
        MediaViewerPlayer.Play();
        StartMediaPositionTimer();
    }

    private void MediaViewerWebPlayer_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var message = e.TryGetWebMessageAsString();
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (message.StartsWith("size:", StringComparison.Ordinal))
        {
            var payload = message[5..];
            var parts = payload.Split('x', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var width) &&
                int.TryParse(parts[1], out var height) &&
                width > 0 &&
                height > 0)
            {
                Dispatcher.BeginInvoke(() => FitMediaViewerPanel(width, height));
            }

            return;
        }

        if (message.StartsWith("error:", StringComparison.Ordinal))
            Dispatcher.BeginInvoke(() => HandleMediaPlaybackFailure(LocalizationManager.Instance["UI_MediaPlaybackFailed"]));
    }

    private void FitMediaViewerPanel(int mediaWidth, int mediaHeight)
    {
        if (!_isMediaViewerOpen || mediaWidth <= 0 || mediaHeight <= 0)
            return;

        _mediaViewerMediaWidth = mediaWidth;
        _mediaViewerMediaHeight = mediaHeight;

        var maxPanelWidth = Math.Max(360, ActualWidth * 0.94);
        var maxPanelHeight = Math.Max(280, ActualHeight * 0.92);

        var controlsHeight = _usesWebViewVideo
            ? WebViewControlsHeight
            : MediaViewerControlsPanel.Visibility == Visibility.Visible
                ? Mp4ControlsHeight
                : 0;

        var chromeVertical = MediaViewerHeaderHeight + MediaViewerBorderChrome + controlsHeight;
        var chromeHorizontal = MediaViewerBorderChrome;

        var maxContentWidth = maxPanelWidth - chromeHorizontal;
        var maxContentHeight = maxPanelHeight - chromeVertical;
        var aspect = (double)mediaWidth / mediaHeight;

        double contentWidth;
        double contentHeight;
        if (maxContentWidth / maxContentHeight > aspect)
        {
            contentHeight = maxContentHeight;
            contentWidth = contentHeight * aspect;
        }
        else
        {
            contentWidth = maxContentWidth;
            contentHeight = contentWidth / aspect;
        }

        contentWidth = Math.Max(MediaViewerMinContentWidth, contentWidth);
        contentHeight = Math.Max(MediaViewerMinContentHeight, contentHeight);

        var hostHeight = _usesWebViewVideo ? contentHeight + WebViewControlsHeight : contentHeight;

        MediaViewerContentGrid.Width = contentWidth;
        MediaViewerContentGrid.Height = hostHeight;
        MediaViewerContentGrid.MinHeight = hostHeight;

        MediaViewerVideoHost.Width = contentWidth;
        MediaViewerVideoHost.Height = hostHeight;
        MediaViewerImageHost.Width = contentWidth;
        MediaViewerImageHost.Height = contentHeight;

        if (_usesWebViewVideo)
        {
            MediaViewerWebPlayer.Width = contentWidth;
            MediaViewerWebPlayer.Height = hostHeight;
        }
        else
        {
            MediaViewerPlayer.Width = contentWidth;
            MediaViewerPlayer.Height = contentHeight;
        }

        MediaViewerPanel.Width = contentWidth + chromeHorizontal;
        MediaViewerPanel.Height = contentHeight + chromeVertical;
    }

    private void ResetMediaViewerLayout()
    {
        _mediaViewerMediaWidth = 0;
        _mediaViewerMediaHeight = 0;

        MediaViewerPanel.ClearValue(FrameworkElement.WidthProperty);
        MediaViewerPanel.ClearValue(FrameworkElement.HeightProperty);
        MediaViewerContentGrid.ClearValue(FrameworkElement.WidthProperty);
        MediaViewerContentGrid.ClearValue(FrameworkElement.HeightProperty);
        MediaViewerContentGrid.ClearValue(FrameworkElement.MinHeightProperty);
        MediaViewerVideoHost.ClearValue(FrameworkElement.WidthProperty);
        MediaViewerVideoHost.ClearValue(FrameworkElement.HeightProperty);
        MediaViewerImageHost.ClearValue(FrameworkElement.WidthProperty);
        MediaViewerImageHost.ClearValue(FrameworkElement.HeightProperty);
        MediaViewerWebPlayer.ClearValue(FrameworkElement.WidthProperty);
        MediaViewerWebPlayer.ClearValue(FrameworkElement.HeightProperty);
        MediaViewerPlayer.ClearValue(FrameworkElement.WidthProperty);
        MediaViewerPlayer.ClearValue(FrameworkElement.HeightProperty);
    }

    private void CloseMediaViewer(bool animate = true)
    {
        if (!_isMediaViewerOpen && MediaViewerOverlay.Visibility != Visibility.Visible)
            return;

        StopMediaPlayback();
        ResetMediaViewerContent();

        _mediaViewerPath = null;
        _isMediaViewerOpen = false;
        HideMediaViewerError();

        void FinishHide()
        {
            MediaViewerOverlay.Visibility = Visibility.Collapsed;
            MediaViewerPanel.Visibility = Visibility.Visible;
            PopChromeDimOverlay();
        }

        if (!animate)
        {
            FinishHide();
            return;
        }

        if (MediaViewerPanel.Visibility != Visibility.Visible)
        {
            FinishHide();
            return;
        }

        UiAnimationHelper.HideOverlay(MediaViewerPanel, FinishHide);
    }

    private void ResetMediaViewerContent()
    {
        StopMediaPlayback();
        ResetMediaViewerLayout();
        MediaViewerImage.Source = null;
        MediaViewerImageHost.Visibility = Visibility.Collapsed;
        MediaViewerVideoHost.Visibility = Visibility.Collapsed;
        MediaViewerControlsPanel.Visibility = Visibility.Collapsed;
        MediaViewerPlayer.Visibility = Visibility.Visible;
        MediaViewerWebPlayer.Visibility = Visibility.Collapsed;
    }

    private void StopMediaPlayback()
    {
        _mediaPositionTimer?.Stop();
        _mediaPositionTimer = null;
        _isMediaSliderDragging = false;
        _mediaIsPlaying = false;

        if (_usesWebViewVideo)
        {
            if (MediaViewerWebPlayer.CoreWebView2 is not null)
                MediaViewerWebPlayer.CoreWebView2.WebMessageReceived -= MediaViewerWebPlayer_WebMessageReceived;

            WebViewVideoPlayer.Stop(MediaViewerWebPlayer);
        }

        _usesWebViewVideo = false;

        if (MediaViewerPlayer.Source is not null)
            MediaViewerPlayer.Stop();

        MediaViewerPlayer.Source = null;
    }

    private void StartMediaPositionTimer()
    {
        _mediaPositionTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _mediaPositionTimer.Tick -= MediaPositionTimer_Tick;
        _mediaPositionTimer.Tick += MediaPositionTimer_Tick;
        _mediaPositionTimer.Start();
    }

    private void MediaPositionTimer_Tick(object? sender, EventArgs e)
    {
        if (!_isMediaViewerOpen || _usesWebViewVideo || MediaViewerPlayer.Source is null)
            return;

        if (_isMediaSliderDragging)
            return;

        if (MediaViewerPlayer.NaturalDuration.HasTimeSpan)
        {
            var duration = MediaViewerPlayer.NaturalDuration.TimeSpan;
            MediaViewerPositionSlider.Maximum = Math.Max(1, duration.TotalSeconds);
            MediaViewerPositionSlider.Value = Math.Min(
                MediaViewerPlayer.Position.TotalSeconds,
                MediaViewerPositionSlider.Maximum);
            UpdateMediaTimeText(MediaViewerPlayer.Position, duration);
        }
    }

    private void UpdateMediaTimeText(TimeSpan position, TimeSpan duration)
    {
        MediaViewerTimeText.Text = $"{FormatMediaTime(position)} / {FormatMediaTime(duration)}";
    }

    private static string FormatMediaTime(TimeSpan value) =>
        value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss")
            : value.ToString(@"m\:ss");

    private void ShowMediaViewerError(string message)
    {
        MediaViewerErrorText.Text = message;
        MediaViewerErrorText.Visibility = Visibility.Visible;
    }

    private void HideMediaViewerError()
    {
        MediaViewerErrorText.Text = string.Empty;
        MediaViewerErrorText.Visibility = Visibility.Collapsed;
    }

    private void MediaViewerCloseButton_Click(object sender, RoutedEventArgs e) =>
        CloseMediaViewer();

    private void MediaViewerPlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_usesWebViewVideo || MediaViewerPlayer.Source is null)
            return;

        var loc = LocalizationManager.Instance;

        if (_mediaIsPlaying)
        {
            MediaViewerPlayer.Pause();
            _mediaIsPlaying = false;
            MediaViewerPlayPauseButton.Content = loc["UI_MediaPlay"];
            return;
        }

        if (MediaViewerPlayer.NaturalDuration.HasTimeSpan &&
            MediaViewerPlayer.Position >= MediaViewerPlayer.NaturalDuration.TimeSpan)
        {
            MediaViewerPlayer.Position = TimeSpan.Zero;
        }

        MediaViewerPlayer.Play();
        _mediaIsPlaying = true;
        MediaViewerPlayPauseButton.Content = loc["UI_MediaPause"];
    }

    private void MediaViewerPlayer_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (_usesWebViewVideo)
            return;

        if (MediaViewerPlayer.NaturalVideoWidth > 0 && MediaViewerPlayer.NaturalVideoHeight > 0)
            FitMediaViewerPanel(MediaViewerPlayer.NaturalVideoWidth, MediaViewerPlayer.NaturalVideoHeight);

        if (!MediaViewerPlayer.NaturalDuration.HasTimeSpan)
            return;

        var duration = MediaViewerPlayer.NaturalDuration.TimeSpan;
        MediaViewerPositionSlider.Maximum = Math.Max(1, duration.TotalSeconds);
        UpdateMediaTimeText(MediaViewerPlayer.Position, duration);
        _mediaIsPlaying = true;
        MediaViewerPlayPauseButton.Content = LocalizationManager.Instance["UI_MediaPause"];
    }

    private void MediaViewerPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (_usesWebViewVideo)
            return;

        _mediaIsPlaying = false;
        MediaViewerPlayPauseButton.Content = LocalizationManager.Instance["UI_MediaPlay"];
        if (MediaViewerPlayer.NaturalDuration.HasTimeSpan)
            UpdateMediaTimeText(MediaViewerPlayer.NaturalDuration.TimeSpan, MediaViewerPlayer.NaturalDuration.TimeSpan);
    }

    private void MediaViewerPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        if (_usesWebViewVideo)
            return;

        var message = string.IsNullOrWhiteSpace(e.ErrorException?.Message)
            ? LocalizationManager.Instance["UI_MediaPlaybackFailed"]
            : e.ErrorException.Message;

        HandleMediaPlaybackFailure(message);
    }

    private void HandleMediaPlaybackFailure(string message)
    {
        var loc = LocalizationManager.Instance;
        ShowMediaViewerError(message);
        StatusText.Text = loc["UI_MediaPlaybackFailed"];

        if (string.IsNullOrWhiteSpace(_mediaViewerPath) || !File.Exists(_mediaViewerPath))
            return;

        var result = MessageBox.Show(
            loc["UI_MediaOpenExternallyPrompt"],
            loc["UI_MediaViewerTitle"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
            OpenFileWithDefaultApplication(_mediaViewerPath);
    }

    private void MediaViewerPositionSlider_DragStarted(object sender, DragStartedEventArgs e) =>
        _isMediaSliderDragging = true;

    private void MediaViewerPositionSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isMediaSliderDragging = false;
        if (_usesWebViewVideo || MediaViewerPlayer.Source is null)
            return;

        MediaViewerPlayer.Position = TimeSpan.FromSeconds(MediaViewerPositionSlider.Value);
    }

    private void MediaViewerOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            !IsDescendantOf(source, MediaViewerPanel))
        {
            CloseMediaViewer();
            e.Handled = true;
        }
    }

    private void MediaViewerPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        e.Handled = true;

    private void MainWindow_MediaViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isMediaViewerOpen || _mediaViewerMediaWidth <= 0 || _mediaViewerMediaHeight <= 0)
            return;

        FitMediaViewerPanel(_mediaViewerMediaWidth, _mediaViewerMediaHeight);
    }
}
