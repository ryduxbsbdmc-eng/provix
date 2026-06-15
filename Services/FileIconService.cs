using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FileExplorer.Services;

public sealed class FileIconService
{
    private const int IconSize = 16;

    private readonly ConcurrentDictionary<string, ImageSource> _cache = new();
    private ImageSource? _defaultFolderIcon;
    private ImageSource? _defaultFileIcon;

    public ImageSource GetFolderIcon(string? path = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return _defaultFolderIcon ??= CreateFolderIcon(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        return _cache.GetOrAdd($"folder:{path.ToLowerInvariant()}", _ => CreateFolderIcon(path));
    }

    public ImageSource GetSharedFolderIcon() => GetFolderIcon();

    public ImageSource GetFileIcon(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return GetFileIconByExtension(extension, filePath);
    }

    public ImageSource GetFileIconByExtension(string extension, string? samplePath = null)
    {
        if (string.IsNullOrEmpty(extension))
            return _defaultFileIcon ??= CreateFileIcon(samplePath ?? string.Empty);

        return _cache.GetOrAdd($"ext:{extension.ToLowerInvariant()}", _ => CreateFileIcon(samplePath ?? $"dummy{extension}"));
    }

    public ImageSource GetDriveIcon(string driveRoot) =>
        GetFolderIcon(driveRoot);

    private ImageSource CreateFolderIcon(string path)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is not null)
                return IconToImageSource(icon);
        }
        catch
        {
            // Fall back to default folder icon.
        }

        return _defaultFolderIcon ??= CreateFallbackIcon(System.Windows.Media.Brushes.Gold);
    }

    private ImageSource CreateFileIcon(string filePath)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(filePath);
            if (icon is not null)
                return IconToImageSource(icon);
        }
        catch
        {
            // Fall back to default file icon.
        }

        return _defaultFileIcon ??= CreateFallbackIcon(System.Windows.Media.Brushes.LightGray);
    }

    private static ImageSource IconToImageSource(Icon icon)
    {
        using var bitmap = icon.ToBitmap();
        var hBitmap = bitmap.GetHbitmap();

        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(IconSize, IconSize));

            if (source.CanFreeze)
                source.Freeze();

            return source;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    private static ImageSource CreateFallbackIcon(System.Windows.Media.Brush fill)
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRoundedRectangle(fill, null, new Rect(0, 0, IconSize, IconSize), 2, 2);
        }

        var bitmap = new RenderTargetBitmap(IconSize, IconSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        if (bitmap.CanFreeze)
            bitmap.Freeze();

        return bitmap;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);
}
