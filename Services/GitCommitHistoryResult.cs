using FileExplorer.Models;

namespace FileExplorer.Services;

public sealed class GitCommitHistoryResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<GitCommit> Commits { get; init; } = [];

    public static GitCommitHistoryResult Succeeded(IReadOnlyList<GitCommit> commits) =>
        new() { Success = true, Commits = commits };

    public static GitCommitHistoryResult Failed(string message) =>
        new() { Success = false, Message = message };
}
