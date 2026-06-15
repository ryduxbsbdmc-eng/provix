using System.IO;
using System.Windows;
using System.Windows.Controls;
using FileExplorer.Services;

namespace FileExplorer.Controls;

public partial class FilePreviewPanel : UserControl
{
    private CancellationTokenSource? _loadCancellation;
    private bool _webViewInitialized;

    public event EventHandler? CloseRequested;

    public FilePreviewPanel()
    {
        InitializeComponent();
    }

    public void ApplyLocalization()
    {
        var loc = LocalizationManager.Instance;
        PreviewTitleText.Text = loc["UI_Preview"];
        PreviewEmptyText.Text = loc["UI_PreviewEmpty"];
        ClosePreviewButton.ToolTip = loc["UI_PreviewClose"];
    }

    public void ClearPreview()
    {
        CancelLoad();
        PreviewFileNameText.Text = string.Empty;
        PreviewSubtitleText.Text = string.Empty;
        PreviewImage.Source = null;
        PreviewTextBlock.Text = string.Empty;
        SetPreviewMode(FilePreviewKind.None);
    }

    public async Task ShowPathAsync(string path, bool isDirectory)
    {
        CancelLoad();
        _loadCancellation = new CancellationTokenSource();
        var token = _loadCancellation.Token;

        try
        {
            var data = await FilePreviewService.LoadAsync(path, isDirectory, token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
                return;

            PreviewFileNameText.Text = data.Title ?? string.Empty;
            PreviewSubtitleText.Text = data.Subtitle ?? string.Empty;
            await ApplyPreviewDataAsync(data, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Superseded by newer selection.
        }
        catch
        {
            PreviewFileNameText.Text = Path.GetFileName(path);
            PreviewSubtitleText.Text = path;
            PreviewTextBlock.Text = LocalizationManager.Instance["UI_PreviewError"];
            SetPreviewMode(FilePreviewKind.Text);
        }
    }

    private async Task ApplyPreviewDataAsync(FilePreviewData data, CancellationToken token)
    {
        switch (data.Kind)
        {
            case FilePreviewKind.None:
                SetPreviewMode(FilePreviewKind.None);
                break;

            case FilePreviewKind.Image:
                PreviewImage.Source = data.Image;
                SetPreviewMode(FilePreviewKind.Image);
                break;

            case FilePreviewKind.Text:
            case FilePreviewKind.Folder:
            case FilePreviewKind.Unsupported:
                PreviewTextBlock.Text = data.TextContent ?? string.Empty;
                SetPreviewMode(FilePreviewKind.Text);
                break;

            case FilePreviewKind.Markdown:
            case FilePreviewKind.Pdf:
                if (ExternalToolsService.IsAvailable(ExternalTool.WebView2) && !string.IsNullOrEmpty(data.FilePath))
                {
                    await EnsureWebViewAsync().ConfigureAwait(true);
                    if (token.IsCancellationRequested)
                        return;

                    if (data.Kind == FilePreviewKind.Pdf)
                    {
                        PreviewWebView.Source = new Uri(data.FilePath, UriKind.Absolute);
                    }
                    else if (!string.IsNullOrEmpty(data.HtmlContent))
                    {
                        PreviewWebView.NavigateToString(data.HtmlContent);
                    }

                    SetPreviewMode(data.Kind);
                }
                else
                {
                    PreviewTextBlock.Text = data.TextContent
                                            ?? LocalizationManager.Instance["UI_PreviewWebViewRequired"];
                    SetPreviewMode(FilePreviewKind.Text);
                }

                break;
        }
    }

    private async Task EnsureWebViewAsync()
    {
        if (_webViewInitialized)
            return;

        await PreviewWebView.EnsureCoreWebView2Async().ConfigureAwait(true);
        _webViewInitialized = true;
    }

    private void SetPreviewMode(FilePreviewKind kind)
    {
        ImagePreviewScroller.Visibility = kind == FilePreviewKind.Image ? Visibility.Visible : Visibility.Collapsed;
        TextPreviewScroller.Visibility = kind is FilePreviewKind.Text or FilePreviewKind.Unsupported or FilePreviewKind.Folder
            ? Visibility.Visible
            : Visibility.Collapsed;
        PreviewWebView.Visibility = kind is FilePreviewKind.Markdown or FilePreviewKind.Pdf
            ? Visibility.Visible
            : Visibility.Collapsed;
        PreviewEmptyText.Visibility = kind == FilePreviewKind.None ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CancelLoad()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = null;
    }

    private void ClosePreviewButton_Click(object sender, RoutedEventArgs e) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);
}
