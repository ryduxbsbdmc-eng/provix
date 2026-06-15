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

    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiAddOverlays = 0x000000020;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;

    private readonly ConcurrentDictionary<string, ImageSource> _cache = new();
    private ImageSource? _defaultFolderIcon;
    private ImageSource? _defaultFileIcon;

    public ImageSource GetFolderIcon(string? path = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return _defaultFolderIcon ??= CreateShellIconForFolder(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        return _cache.GetOrAdd($"folder:{path.ToLowerInvariant()}", _ => CreateShellIconForFolder(path));
    }

    public ImageSource GetSharedFolderIcon() => GetFolderIcon();

    public ImageSource GetFileIcon(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return _defaultFileIcon ??= CreateFallbackIcon(System.Windows.Media.Brushes.LightGray);

        return _cache.GetOrAdd($"file:{filePath.ToLowerInvariant()}", _ => CreateShellIconForFile(filePath));
    }

    public ImageSource GetFileIconByExtension(string extension, string? samplePath = null)
    {
        if (!string.IsNullOrWhiteSpace(samplePath))
            return GetFileIcon(samplePath);

        if (string.IsNullOrEmpty(extension))
            return _defaultFileIcon ??= CreateFallbackIcon(System.Windows.Media.Brushes.LightGray);

        return _cache.GetOrAdd($"ext:{extension.ToLowerInvariant()}", _ => CreateShellIconForExtension(extension));
    }

    public ImageSource GetDriveIcon(string driveRoot) => GetFolderIcon(driveRoot);

    private ImageSource CreateShellIconForFolder(string path)
    {
        if (Directory.Exists(path))
            return CreateShellIcon(path, isDirectory: true) ?? GetDefaultFolderIcon();

        return GetDefaultFolderIcon();
    }

    private ImageSource CreateShellIconForFile(string path)
    {
        if (File.Exists(path))
            return CreateShellIcon(path, isDirectory: false) ?? GetDefaultFileIcon();

        return GetDefaultFileIcon();
    }

    private ImageSource CreateShellIconForExtension(string extension)
    {
        var shellIcon = CreateShellIcon($"placeholder{extension}", isDirectory: false, useFileAttributes: true);
        return shellIcon ?? GetDefaultFileIcon();
    }

    private ImageSource? CreateShellIcon(string path, bool isDirectory, bool useFileAttributes = false)
    {
        var shinfo = new SHFILEINFO();
        var flags = ShgfiIcon | ShgfiSmallIcon | ShgfiAddOverlays;
        if (useFileAttributes)
            flags |= ShgfiUseFileAttributes;

        var attributes = isDirectory ? FileAttributeDirectory : FileAttributeNormal;
        var result = SHGetFileInfo(path, attributes, ref shinfo, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (result == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero)
            return null;

        try
        {
            using var icon = (Icon)Icon.FromHandle(shinfo.hIcon).Clone();
            return IconToImageSource(icon);
        }
        catch
        {
            return null;
        }
        finally
        {
            DestroyIcon(shinfo.hIcon);
        }
    }

    private ImageSource GetDefaultFolderIcon() =>
        _defaultFolderIcon ??= CreateFallbackIcon(System.Windows.Media.Brushes.Gold);

    private ImageSource GetDefaultFileIcon() =>
        _defaultFileIcon ??= CreateFallbackIcon(System.Windows.Media.Brushes.LightGray);

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
            context.DrawRoundedRectangle(fill, null, new Rect(0, 0, IconSize, IconSize), 2, 2);

        var bitmap = new RenderTargetBitmap(IconSize, IconSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        if (bitmap.CanFreeze)
            bitmap.Freeze();

        return bitmap;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool DeleteObject(IntPtr hObject);
}
