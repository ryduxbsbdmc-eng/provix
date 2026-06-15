namespace FileExplorer.Models;

public enum GitFileStatusType
{
    Untracked,
    Modified,
    Added,
    Deleted,
    Renamed,
    Copied,
    UpdatedButUnmerged,
    Ignored,
    Unchanged
}

public sealed class GitFileStatus
{
    public required string FilePath { get; init; }
    public required GitFileStatusType Status { get; init; }
}
