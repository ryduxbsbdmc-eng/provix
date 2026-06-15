using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileExplorer.Models;

namespace FileExplorer.Services;

public enum FilePreviewKind
{
    None,
    Folder,
    Image,
    Text,
    Markdown,
    Pdf,
    Unsupported
}

public sealed class FilePreviewData
{
    public FilePreviewKind Kind { get; init; }

    public string? Title { get; init; }

    public string? Subtitle { get; init; }

    public ImageSource? Image { get; init; }

    public string? TextContent { get; init; }

    public string? HtmlContent { get; init; }

    public string? FilePath { get; init; }
}

public static class FilePreviewService
{
    private const long MaxPreviewBytes = 256 * 1024;

    private const int MaxPreviewLines = 200;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico"
    };

    public static FilePreviewKind GetPreviewKind(string path, bool isDirectory)
    {
        if (isDirectory)
            return FilePreviewKind.Folder;

        var extension = Path.GetExtension(path);
        if (string.IsNullOrEmpty(extension))
            return FilePreviewKind.Unsupported;

        if (ImageExtensions.Contains(extension))
            return FilePreviewKind.Image;

        if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
            return FilePreviewKind.Pdf;

        if (string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase))
            return FilePreviewKind.Markdown;

        if (TextFileHelper.IsEditableTextFile(path))
            return FilePreviewKind.Text;

        return FilePreviewKind.Unsupported;
    }

    public static async Task<FilePreviewData> LoadAsync(
        string path,
        bool isDirectory,
        CancellationToken cancellationToken = default)
    {
        var kind = GetPreviewKind(path, isDirectory);
        var title = isDirectory
            ? Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : Path.GetFileName(path);

        if (kind == FilePreviewKind.Folder)
        {
            return new FilePreviewData
            {
                Kind = kind,
                Title = title,
                Subtitle = path,
                TextContent = await BuildFolderSummaryAsync(path, cancellationToken).ConfigureAwait(false)
            };
        }

        if (kind == FilePreviewKind.Image)
        {
            return new FilePreviewData
            {
                Kind = kind,
                Title = title,
                Subtitle = BuildFileSubtitle(path),
                Image = LoadImage(path),
                FilePath = path
            };
        }

        if (kind == FilePreviewKind.Pdf)
        {
            return new FilePreviewData
            {
                Kind = kind,
                Title = title,
                Subtitle = BuildFileSubtitle(path),
                FilePath = path
            };
        }

        if (kind is FilePreviewKind.Text or FilePreviewKind.Markdown)
        {
            var text = await ReadPreviewTextAsync(path, cancellationToken).ConfigureAwait(false);
            return new FilePreviewData
            {
                Kind = kind,
                Title = title,
                Subtitle = BuildFileSubtitle(path),
                TextContent = kind == FilePreviewKind.Markdown ? null : text,
                HtmlContent = kind == FilePreviewKind.Markdown ? BuildMarkdownHtml(text, title) : null,
                FilePath = path
            };
        }

        return new FilePreviewData
        {
            Kind = FilePreviewKind.Unsupported,
            Title = title,
            Subtitle = BuildFileSubtitle(path),
            TextContent = LocalizationManager.Instance["UI_PreviewUnsupported"]
        };
    }

    private static string BuildFileSubtitle(string path)
    {
        if (!TextFileHelper.TryGetFileSize(path, out var size))
            return path;

        return $"{TextFileHelper.FormatByteSize(size)} · {path}";
    }

    private static ImageSource? LoadImage(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.DecodePixelWidth = 480;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> ReadPreviewTextAsync(string path, CancellationToken cancellationToken)
    {
        if (!TextFileHelper.TryGetFileSize(path, out var size))
            return string.Empty;

        if (size > MaxPreviewBytes)
        {
            return LocalizationManager.Instance["UI_PreviewTooLarge"];
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var builder = new StringBuilder();
        var lineCount = 0;

        while (lineCount < MaxPreviewLines && await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            builder.AppendLine(line);
            lineCount++;
        }

        if (lineCount >= MaxPreviewLines)
            builder.AppendLine().Append(LocalizationManager.Instance["UI_PreviewTruncated"]);

        return builder.ToString();
    }

    private static Task<string> BuildFolderSummaryAsync(string path, CancellationToken cancellationToken)
    {
        var loc = LocalizationManager.Instance;
        int fileCount = 0;
        int folderCount = 0;

        try
        {
            foreach (var _ in Directory.EnumerateFiles(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                fileCount++;
            }

            foreach (var _ in Directory.EnumerateDirectories(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                folderCount++;
            }
        }
        catch
        {
            return Task.FromResult(loc["UI_PreviewFolderError"]);
        }

        return Task.FromResult(string.Format(loc["UI_PreviewFolderSummary"], folderCount, fileCount));
    }

    private static string BuildMarkdownHtml(string markdown, string title)
    {
        var encoded = System.Net.WebUtility.HtmlEncode(markdown)
            .Replace("\r\n", "<br/>", StringComparison.Ordinal)
            .Replace("\n", "<br/>", StringComparison.Ordinal);

        return "<!DOCTYPE html><html><head><meta charset=\"utf-8\" /><style>"
               + "body{font-family:Segoe UI,sans-serif;font-size:13px;line-height:1.5;color:#e8e8e8;background:#1e1e1e;margin:12px;word-break:break-word;}"
               + "h1{font-size:18px;margin:0 0 12px 0;}pre{white-space:pre-wrap;}</style></head><body>"
               + "<h1>" + System.Net.WebUtility.HtmlEncode(title) + "</h1><pre>"
               + encoded
               + "</pre></body></html>";
    }
}
