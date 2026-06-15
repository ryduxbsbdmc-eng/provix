using System.IO;

namespace FileExplorer.Services;

public static class ArchiveHelper
{
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
