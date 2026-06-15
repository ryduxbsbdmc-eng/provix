using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileExplorer.Services;

internal static class StyledIconRenderer
{
    private const int IconSize = 16;

    public static ImageSource CreateFlatFolderIcon() =>
        CreateFlatIcon("#F4B400", string.Empty, isFolder: true);

    public static ImageSource CreateFlatFileIcon(string? extension) =>
        CreateFlatIcon(ResolveFlatColor(extension), FormatExtensionLabel(extension), isFolder: false);

    public static ImageSource CreateMinimalFolderIcon() =>
        CreateMinimalIcon(string.Empty, isFolder: true);

    public static ImageSource CreateMinimalFileIcon(string? extension) =>
        CreateMinimalIcon(FormatExtensionLabel(extension), isFolder: false);

    private static ImageSource CreateFlatIcon(string colorHex, string label, bool isFolder)
    {
        var fill = (Color)ColorConverter.ConvertFromString(colorHex)!;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRoundedRectangle(new SolidColorBrush(fill), null, new Rect(1, 2, 14, 12), 3, 3);
            if (isFolder)
            {
                context.DrawRoundedRectangle(new SolidColorBrush(fill), null, new Rect(2, 1, 8, 4), 1.5, 1.5);
            }
            else if (!string.IsNullOrEmpty(label))
            {
                DrawCenteredLabel(context, label, Brushes.White);
            }
        }

        return Render(visual);
    }

    private static ImageSource CreateMinimalIcon(string label, bool isFolder)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var border = new SolidColorBrush(Color.FromRgb(120, 130, 145));
            var fill = new SolidColorBrush(Color.FromArgb(40, 180, 190, 205));
            context.DrawRoundedRectangle(fill, new Pen(border, 1), new Rect(1.5, 1.5, 13, 13), 2.5, 2.5);
            if (isFolder)
            {
                context.DrawRectangle(fill, new Pen(border, 1), new Rect(3, 3, 7, 3));
            }
            else if (!string.IsNullOrEmpty(label))
            {
                DrawCenteredLabel(context, label, border);
            }
        }

        return Render(visual);
    }

    private static void DrawCenteredLabel(DrawingContext context, string label, Brush brush)
    {
        var dpi = 1.0;
        if (Application.Current?.MainWindow is not null)
            dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow).PixelsPerDip;

        var formatted = new FormattedText(
            label,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            6.5,
            brush,
            dpi);

        var x = (IconSize - formatted.Width) / 2;
        var y = (IconSize - formatted.Height) / 2;
        context.DrawText(formatted, new Point(x, y));
    }

    private static string FormatExtensionLabel(string? extension)
    {
        var normalized = string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : extension.Trim().TrimStart('.').ToUpperInvariant();

        if (normalized.Length == 0)
            return "FILE";

        return normalized.Length <= 3 ? normalized : normalized[..3];
    }

    private static string ResolveFlatColor(string? extension)
    {
        var ext = string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : extension.Trim().TrimStart('.').ToLowerInvariant();

        return ext switch
        {
            "png" or "jpg" or "jpeg" or "gif" or "bmp" or "webp" or "svg" or "ico" => "#34A853",
            "mp4" or "mkv" or "avi" or "mov" or "webm" => "#EA4335",
            "mp3" or "wav" or "flac" or "ogg" => "#A142F4",
            "zip" or "rar" or "7z" or "tar" or "gz" => "#FBBC04",
            "cs" or "js" or "ts" or "py" or "java" or "cpp" or "c" or "h" or "rs" or "go" => "#4285F4",
            "txt" or "md" or "pdf" or "doc" or "docx" or "rtf" => "#5C6BC0",
            "xls" or "xlsx" or "csv" => "#0F9D58",
            "exe" or "msi" or "bat" or "cmd" => "#7B1FA2",
            _ => "#9AA0A6"
        };
    }

    private static ImageSource Render(DrawingVisual visual)
    {
        var bitmap = new RenderTargetBitmap(IconSize, IconSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        if (bitmap.CanFreeze)
            bitmap.Freeze();
        return bitmap;
    }
}
