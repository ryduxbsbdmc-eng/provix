using System.IO;
using System.Security.Cryptography;

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

    public static bool IsPasswordRelatedError(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            var message = current.Message;
            if (message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("encrypted", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("decrypt", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("crc", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return ex is InvalidOperationException or CryptographicException;
    }
}
