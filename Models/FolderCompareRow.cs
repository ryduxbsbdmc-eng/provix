using FileExplorer.Models;
using FileExplorer.Services;

namespace FileExplorer.Models;

public sealed class FolderCompareRow
{
    public required string Name { get; init; }

    public required string LeftDisplay { get; init; }

    public required string RightDisplay { get; init; }

    public required string StatusDisplay { get; init; }

    public static FolderCompareRow FromEntry(FolderCompareEntry entry)
    {
        var loc = LocalizationManager.Instance;
        var status = entry.Kind switch
        {
            FolderCompareKind.OnlyLeft => loc["UI_CompareOnlyLeft"],
            FolderCompareKind.OnlyRight => loc["UI_CompareOnlyRight"],
            FolderCompareKind.BothSame => loc["UI_CompareSame"],
            FolderCompareKind.BothDifferent => loc["UI_CompareDifferent"],
            _ => string.Empty
        };

        return new FolderCompareRow
        {
            Name = entry.Name,
            LeftDisplay = FormatSize(entry.LeftSize, entry.IsDirectory),
            RightDisplay = FormatSize(entry.RightSize, entry.IsDirectory),
            StatusDisplay = status
        };
    }

    private static string FormatSize(long? size, bool isDirectory)
    {
        if (isDirectory)
            return "—";

        return size.HasValue ? TextFileHelper.FormatByteSize(size.Value) : "—";
    }
}
