namespace FileExplorer.Models;

public sealed class GitCommit
{
    public required string Hash { get; init; }
    public required string Date { get; init; }
    public required string Message { get; init; }

    public string Summary => $"{Hash}  ·  {Date}  ·  {Message}";
}
