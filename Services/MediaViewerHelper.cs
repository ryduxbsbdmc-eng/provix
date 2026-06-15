using System.IO;

namespace FileExplorer.Services;

public enum MediaViewerKind
{
    Image,
    Video
}

public static class MediaViewerHelper
{
    public const long MaxImageBytes = 64L * 1024 * 1024;

    public const long MaxVideoBytes = 8L * 1024 * 1024 * 1024;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".webm"
    };

    public static bool TryGetMediaKind(string filePath, out MediaViewerKind kind)
    {
        kind = default;
        var extension = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(extension))
            return false;

        if (ImageExtensions.Contains(extension))
        {
            kind = MediaViewerKind.Image;
            return true;
        }

        if (VideoExtensions.Contains(extension))
        {
            kind = MediaViewerKind.Video;
            return true;
        }

        return false;
    }

    public static bool IsMediaFile(string filePath) =>
        TryGetMediaKind(filePath, out _);

    public static bool IsWithinSizeLimit(string filePath, MediaViewerKind kind, out long fileSize)
    {
        fileSize = 0;
        if (!TextFileHelper.TryGetFileSize(filePath, out fileSize))
            return false;

        var maxBytes = kind == MediaViewerKind.Image ? MaxImageBytes : MaxVideoBytes;
        return fileSize <= maxBytes;
    }
}
