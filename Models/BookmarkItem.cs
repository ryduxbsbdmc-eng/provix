using System.Windows.Media;

namespace FileExplorer.Models;

public sealed class BookmarkItem
{
    public required string Path { get; init; }

    public required string DisplayName { get; init; }

    public required ImageSource Icon { get; init; }

    public bool Exists { get; init; }
}
