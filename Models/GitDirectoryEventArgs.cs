namespace FileExplorer.Models;

public sealed class GitDirectoryEventArgs : EventArgs
{
    public required string TargetDirectory { get; init; }
}
