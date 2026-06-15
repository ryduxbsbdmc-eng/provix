using System.IO;

namespace FileExplorer.Services;

public static class ArchiveHelper
{
    public const long MaxInAppPreviewBytes = 256L * 1024 * 1024;

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z"
    };

    public static bool IsArchiveFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(extension) && ArchiveExtensions.Contains(extension);
    }

    public static string GetExtractionFolderName(string archivePath) =>
        Path.GetFileNameWithoutExtension(archivePath);
}
