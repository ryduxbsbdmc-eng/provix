namespace FileExplorer.Models;

public enum FolderCompareKind
{
    OnlyLeft,
    OnlyRight,
    BothSame,
    BothDifferent
}

public sealed class FolderCompareEntry
{
    public required string Name { get; init; }

    public FolderCompareKind Kind { get; init; }

    public long? LeftSize { get; init; }

    public long? RightSize { get; init; }

    public bool IsDirectory { get; init; }
}
