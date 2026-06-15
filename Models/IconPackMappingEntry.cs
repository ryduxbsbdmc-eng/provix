using System.Windows.Media;

namespace FileExplorer.Models;

public sealed class IconPackMappingEntry
{
    public required string Extension { get; init; }

    public required string ImageFileName { get; init; }

    public ImageSource? Preview { get; init; }
}
