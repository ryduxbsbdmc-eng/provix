using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileExplorer.Models;

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
    private FileIconStyle _style = FileIconStyle.Windows;
    private IconPackLoader? _iconPack;

    public void ApplySettings(FileIconStyle style, string? customIconPackPath)
    {
        _style = style;
        _iconPack = null;

        if (style == FileIconStyle.Custom && !string.IsNullOrWhiteSpace(customIconPackPath))
        {
            var loader = new IconPackLoader();
            if (loader.Load(customIconPackPath))
                _iconPack = loader;
        }

        ClearCache();
    }

    public void ClearCache()
    {
        _cache.Clear();
        _defaultFolderIcon = null;
        _defaultFileIcon = null;
    }

    public ImageSource GetFolderIcon(string? path = null)
    {
        var cacheKey = BuildCacheKey("folder", path);
        return _cache.GetOrAdd(cacheKey, _ => CreateFolderIcon(path));
    }

    public ImageSource GetSharedFolderIcon() => GetFolderIcon();

    public ImageSource GetFileIcon(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return GetDefaultFileIcon();

        return _cache.GetOrAdd(BuildCacheKey("file", filePath), _ => CreateFileIcon(filePath));
    }

    public ImageSource GetFileIconByExtension(string extension, string? samplePath = null)
    {
        if (!string.IsNullOrWhiteSpace(samplePath))
            return GetFileIcon(samplePath);

        return _cache.GetOrAdd(BuildCacheKey("ext", extension), _ => CreateFileIconByExtension(extension));
    }

    public ImageSource GetDriveIcon(string driveRoot) =>
        _cache.GetOrAdd(BuildCacheKey("drive", driveRoot), _ => CreateDriveIcon(driveRoot));

    private string BuildCacheKey(string kind, string? value) =>
        $"{_style}:{kind}:{value?.ToLowerInvariant() ?? string.Empty}";

    private ImageSource CreateFolderIcon(string? path)
    {
        if (_style == FileIconStyle.Custom)
            return _iconPack?.GetFolderIcon() ??
                   StyledIconRenderer.CreateFlatFolderIcon() ??
                   GetDefaultFolderIcon();

        if (_style != FileIconStyle.Windows)
            return CreateStyledFolderIcon() ?? GetDefaultFolderIcon();

        if (string.IsNullOrWhiteSpace(path))
            return CreateShellIconForFolder(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        if (Directory.Exists(path))
            return CreateShellIcon(path, isDirectory: true) ?? GetDefaultFolderIcon();

        return GetDefaultFolderIcon();
    }

    private ImageSource CreateDriveIcon(string driveRoot)
    {
        if (_style == FileIconStyle.Custom)
            return _iconPack?.GetDriveIcon() ??
                   StyledIconRenderer.CreateFlatFolderIcon() ??
                   GetDefaultFolderIcon();

        if (_style != FileIconStyle.Windows)
            return CreateStyledFolderIcon() ?? GetDefaultFolderIcon();

        return CreateShellIconForFolder(driveRoot);
    }

    private ImageSource CreateFileIcon(string filePath)
    {
        if (_style == FileIconStyle.Custom)
        {
            var extension = Path.GetExtension(filePath);
            return _iconPack?.GetFileIcon(extension) ??
                   StyledIconRenderer.CreateFlatFileIcon(extension) ??
                   GetDefaultFileIcon();
        }

        if (_style != FileIconStyle.Windows)
            return CreateStyledFileIcon(Path.GetExtension(filePath)) ?? GetDefaultFileIcon();

        if (File.Exists(filePath))
            return CreateShellIcon(filePath, isDirectory: false) ?? GetDefaultFileIcon();

        return GetDefaultFileIcon();
    }

    private ImageSource CreateFileIconByExtension(string extension)
    {
        if (_style == FileIconStyle.Custom)
            return _iconPack?.GetFileIcon(extension) ??
                   StyledIconRenderer.CreateFlatFileIcon(extension) ??
                   GetDefaultFileIcon();

        if (_style != FileIconStyle.Windows)
            return CreateStyledFileIcon(extension) ?? GetDefaultFileIcon();

        return CreateShellIcon($"placeholder{extension}", isDirectory: false, useFileAttributes: true) ??
               GetDefaultFileIcon();
    }

    private ImageSource? CreateStyledFolderIcon() =>
        _style switch
        {
            FileIconStyle.Flat => StyledIconRenderer.CreateFlatFolderIcon(),
            FileIconStyle.Minimal => StyledIconRenderer.CreateMinimalFolderIcon(),
            _ => null
        };

    private ImageSource? CreateStyledFileIcon(string extension) =>
        _style switch
        {
            FileIconStyle.Flat => StyledIconRenderer.CreateFlatFileIcon(extension),
            FileIconStyle.Minimal => StyledIconRenderer.CreateMinimalFileIcon(extension),
            _ => null
        };

    private ImageSource CreateShellIconForFolder(string path)
    {
        if (Directory.Exists(path))
            return CreateShellIcon(path, isDirectory: true) ?? GetDefaultFolderIcon();

        return GetDefaultFolderIcon();
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
        _defaultFolderIcon ??= CreateStyledFolderIcon() ??
                               CreateFallbackIcon(System.Windows.Media.Brushes.Gold);

    private ImageSource GetDefaultFileIcon() =>
        _defaultFileIcon ??= CreateStyledFileIcon(string.Empty) ??
                             CreateFallbackIcon(System.Windows.Media.Brushes.LightGray);

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
        // DrawingVisual/RenderTargetBitmap have UI-thread affinity. Directory listings build icons
        // on a background thread, so marshal the rendering onto the Dispatcher to avoid a
        // cross-thread access exception. The frozen result is safe to use from any thread.
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            return dispatcher.Invoke(() => CreateFallbackIcon(fill));

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
