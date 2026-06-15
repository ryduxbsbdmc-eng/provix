using FileExplorer.Models;

namespace FileExplorer.Services;

public sealed class GitWorkingTreeStatus
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool IsClean { get; init; }
    public int ChangedFileCount { get; init; }
    public string BranchName { get; init; } = string.Empty;
    public List<GitFileStatus> Changes { get; init; } = [];

    public static GitWorkingTreeStatus Succeeded(int changedFileCount, string branchName = "", List<GitFileStatus>? changes = null) =>
        new()
        {
            Success = true,
            IsClean = changedFileCount == 0,
            ChangedFileCount = changedFileCount,
            BranchName = branchName,
            Changes = changes ?? []
        };

    public static GitWorkingTreeStatus Failed(string message) =>
        new() { Success = false, Message = message };
}
