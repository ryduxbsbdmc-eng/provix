namespace FileExplorer.Models;

public sealed class BookmarkRecord
{
    public string Path { get; set; } = string.Empty;

    public string? Label { get; set; }

    public long AddedAtTicks { get; set; }
}
