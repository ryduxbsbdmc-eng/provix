using System.IO;
using System.Text;

namespace FileExplorer.Services;

public static class TextFileHelper
{
    public const long MaxInAppEditorBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> EditableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".cs", ".xaml", ".json", ".xml", ".md", ".html", ".js", ".css", ".py",
        ".ts", ".tsx", ".jsx", ".yaml", ".yml", ".ini", ".cfg", ".log", ".rpy"
    };

    public static bool IsEditableTextFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(extension) && EditableExtensions.Contains(extension);
    }

    public static bool TryGetFileSize(string filePath, out long size)
    {
        size = 0;

        try
        {
            var info = new FileInfo(filePath);
            if (!info.Exists)
                return false;

            size = info.Length;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsWithinEditorSizeLimit(string filePath, out long fileSize) =>
        TryGetFileSize(filePath, out fileSize) && fileSize <= MaxInAppEditorBytes;

    public static async Task<string> ReadTextForEditorAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!TryGetFileSize(filePath, out var size))
            throw new FileNotFoundException("File not found.", filePath);

        if (size > MaxInAppEditorBytes)
            throw new InvalidOperationException($"File exceeds the {MaxInAppEditorBytes / (1024 * 1024)} MB in-app editor limit.");

        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    public static string FormatByteSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double size = bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{size:0} {units[unitIndex]}"
            : $"{size:0.##} {units[unitIndex]}";
    }
}
