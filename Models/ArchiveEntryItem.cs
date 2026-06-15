namespace FileExplorer.Models;



public sealed class ArchiveEntryItem

{

    public required string EntryKey { get; init; }

    public required string Name { get; init; }

    public bool IsDirectory { get; init; }

    public long OriginalSize { get; init; }

    public long CompressedSize { get; init; }



    public string OriginalSizeDisplay => IsDirectory ? string.Empty : FormatSize(OriginalSize);

    public string CompressedSizeDisplay => IsDirectory ? string.Empty : FormatSize(CompressedSize);



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


