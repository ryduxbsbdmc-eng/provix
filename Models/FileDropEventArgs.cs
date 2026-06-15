namespace FileExplorer.Models;

public sealed class FileDropEventArgs : EventArgs
{
    public required string[] SourcePaths { get; init; }
    public required string TargetDirectory { get; init; }
}
