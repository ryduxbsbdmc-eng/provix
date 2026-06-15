namespace FileExplorer.Models;

public sealed class ContentSearchMatch
{
    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    public int LineNumber { get; init; }

    public required string LineText { get; init; }

    public required string RelativePath { get; init; }
}
