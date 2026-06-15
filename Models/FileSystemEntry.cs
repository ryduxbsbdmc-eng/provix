using System.Windows.Media;

namespace FileExplorer.Models;

public sealed class FileSystemEntry
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required bool IsDirectory { get; init; }
    public DateTime DateModified { get; init; }
    public string Type { get; init; } = string.Empty;
    public long Size { get; init; }
    public ImageSource? Icon { get; init; }

    public string SizeDisplay => IsDirectory ? string.Empty : FormatSize(Size);

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
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
