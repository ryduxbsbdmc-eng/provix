namespace FileExplorer.Models;

public sealed class GitBranch
{
    public required string Name { get; init; }
    public bool IsCurrent { get; init; }
    public bool IsRemote { get; init; }

    public override string ToString() => Name;
}
