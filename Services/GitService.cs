using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using FileExplorer.Models;

namespace FileExplorer.Services;

public sealed class GitResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public bool RebaseConflictAborted { get; init; }
    public bool NoChangesToCommit { get; init; }
    public bool NothingToAmend { get; init; }
}

public static partial class GitService
{
    public static Task<GitResult> InitializeRepositoryAsync(string directory)
    {
        if (!TryNormalizeDirectory(directory, out var targetDirectory, out var error))
            return Task.FromResult(Failure(error));

        return Task.Run(() => InitializeRepositoryCore(targetDirectory));
    }

    private static GitResult InitializeRepositoryCore(string targetDirectory)
    {
        try
        {
            var result = RunGitSync(targetDirectory, "init");
            if (result.ExitCode != 0)
                return Failure(FormatGitError("git init failed.", result));

            if (!GitRepositoryHelper.ContainsGitRepository(targetDirectory))
            {
                var details = result.StandardError.Trim();
                if (string.IsNullOrWhiteSpace(details))
                    details = result.StandardOutput.Trim();

                return Failure(string.IsNullOrWhiteSpace(details)
                    ? "git init completed but the .git folder was not created."
                    : $"git init completed but the .git folder was not created. {details}");
            }

            return Success("Git repository initialized successfully.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return Failure("Git is not installed or not on PATH.");
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
    }

    public static Task<GitResult> CommitAllAsync(string directory, string commitMessage)
    {
        if (!TryResolveRepositoryRoot(directory, out var repositoryRoot, out var error))
            return Task.FromResult(Failure(error));

        if (string.IsNullOrWhiteSpace(commitMessage))
            return Task.FromResult(Failure("Commit message cannot be empty."));

        return Task.Run(() => ExecuteStageAndCommit(repositoryRoot, commitMessage.Trim(), allowEmpty: true));
    }

    public static Task<GitResult> AmendLastCommitAsync(string directory, string commitMessage)
    {
        if (!TryResolveRepositoryRoot(directory, out var repositoryRoot, out var error))
            return Task.FromResult(Failure(error));

        if (string.IsNullOrWhiteSpace(commitMessage))
            return Task.FromResult(Failure("Commit message cannot be empty."));

        return Task.Run(() => ExecuteAmendLastCommit(repositoryRoot, commitMessage.Trim()));
    }

    private static GitResult ExecuteAmendLastCommit(string repositoryRoot, string commitMessage)
    {
        try
        {
            // Step 1: stage all changes before amending.
            var addResult = RunGitSync(repositoryRoot, "add .");
            if (addResult.ExitCode != 0)
                return Failure(FormatGitError("git add failed.", addResult));

            // Step 2: amend only after add has fully exited.
            var amendResult = RunGitSync(
                repositoryRoot,
                $"commit --amend -m {QuoteProcessArgument(commitMessage)}");

            if (amendResult.ExitCode == 0)
                return Success("Last commit updated.");

            if (IsNothingToAmendOutput(amendResult))
            {
                return new GitResult
                {
                    Success = false,
                    NothingToAmend = true,
                    Message = "Nothing new to amend. The working tree is clean."
                };
            }

            return Failure(FormatGitError("git commit --amend failed.", amendResult));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return Failure("Git is not installed or not on PATH.");
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
    }

    public static Task<GitWorkingTreeStatus> GetWorkingTreeStatusAsync(string directory)
    {
        if (!TryResolveRepositoryRoot(directory, out var repositoryRoot, out var error))
            return Task.FromResult(GitWorkingTreeStatus.Failed(error));

        return Task.Run(() =>
        {
            try
            {
                var branchResult = RunGitSync(repositoryRoot, "branch --show-current");
                var branchName = branchResult.ExitCode == 0 ? branchResult.StandardOutput.Trim() : string.Empty;

                var result = RunGitSync(repositoryRoot, "status --porcelain");
                if (result.ExitCode != 0)
                    return GitWorkingTreeStatus.Failed(FormatGitError("git status failed.", result));

                var changes = ParsePorcelainStatus(result.StandardOutput);
                return GitWorkingTreeStatus.Succeeded(changes.Count, branchName, changes);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
            {
                return GitWorkingTreeStatus.Failed("Git is not installed or not on PATH.");
            }
            catch (Exception ex)
            {
                return GitWorkingTreeStatus.Failed(ex.Message);
            }
        });
    }

    public static async Task<List<GitBranch>> GetBranchesAsync(string directory)
    {
        if (!TryResolveRepositoryRoot(directory, out var repositoryRoot, out _))
            return [];

        var result = await RunGitAsync(repositoryRoot, "branch --list").ConfigureAwait(false);
        if (result.ExitCode != 0)
            return [];

        var branches = new List<GitBranch>();
        foreach (var line in result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmedLine = line.Trim();
            if (string.IsNullOrEmpty(trimmedLine))
                continue;

            var isCurrent = trimmedLine.StartsWith('*');
            var name = isCurrent ? trimmedLine[1..].Trim() : trimmedLine;

            branches.Add(new GitBranch { Name = name, IsCurrent = isCurrent });
        }

        return branches;
    }

    public static async Task<GitResult> SwitchBranchAsync(string directory, string branchName)
    {
        if (!TryResolveRepositoryRoot(directory, out var repositoryRoot, out var error))
            return Failure(error);

        var result = await RunGitAsync(repositoryRoot, $"checkout {QuoteProcessArgument(branchName)}").ConfigureAwait(false);
        return result.ExitCode == 0 ? Success($"Switched to branch {branchName}") : Failure(FormatGitError($"Failed to switch to branch {branchName}", result));
    }

    public static async Task<GitResult> CreateBranchAsync(string directory, string branchName)
    {
        if (!TryResolveRepositoryRoot(directory, out var repositoryRoot, out var error))
            return Failure(error);

        var result = await RunGitAsync(repositoryRoot, $"checkout -b {QuoteProcessArgument(branchName)}").ConfigureAwait(false);
        return result.ExitCode == 0 ? Success($"Created and switched to branch {branchName}") : Failure(FormatGitError($"Failed to create branch {branchName}", result));
    }

    public static async Task<(string Name, string Email)> GetUserIdentityAsync(string directory)
    {
        if (!TryResolveRepositoryRoot(directory, out var repositoryRoot, out _))
            return (string.Empty, string.Empty);

        var nameResult = await RunGitAsync(repositoryRoot, "config user.name").ConfigureAwait(false);
        var emailResult = await RunGitAsync(repositoryRoot, "config user.email").ConfigureAwait(false);

        return (nameResult.StandardOutput.Trim(), emailResult.StandardOutput.Trim());
    }

    public static async Task<GitResult> SetUserIdentityAsync(string directory, string name, string email)
    {
        if (!TryResolveRepositoryRoot(directory, out var repositoryRoot, out var error))
            return Failure(error);

        var nameResult = await RunGitAsync(repositoryRoot, $"config user.name {QuoteProcessArgument(name)}").ConfigureAwait(false);
        if (nameResult.ExitCode != 0)
            return Failure(FormatGitError("Failed to set user.name", nameResult));

        var emailResult = await RunGitAsync(repositoryRoot, $"config user.email {QuoteProcessArgument(email)}").ConfigureAwait(false);
        if (emailResult.ExitCode != 0)
            return Failure(FormatGitError("Failed to set user.email", emailResult));

        return Success("User identity updated.");
    }

    private static List<GitFileStatus> ParsePorcelainStatus(string porcelainOutput)
    {
        var changes = new List<GitFileStatus>();
        if (string.IsNullOrWhiteSpace(porcelainOutput))
            return changes;

        foreach (var line in porcelainOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length < 3)
                continue;

            var statusChars = line[..2];
            var filePath = line[3..].Trim();

            // Handle quoted paths
            if (filePath.StartsWith('"') && filePath.EndsWith('"'))
                filePath = filePath[1..^1];

            var status = statusChars switch
            {
                "??" => GitFileStatusType.Untracked,
                " M" or "M " or "MM" => GitFileStatusType.Modified,
                " A" or "A " => GitFileStatusType.Added,
                " D" or "D " => GitFileStatusType.Deleted,
                " R" or "R " => GitFileStatusType.Renamed,
                " C" or "C " => GitFileStatusType.Copied,
                " U" or "U " or "AU" or "UD" or "UA" or "DU" or "AA" or "UU" => GitFileStatusType.UpdatedButUnmerged,
                "!!" => GitFileStatusType.Ignored,
                _ => GitFileStatusType.Unchanged
            };

            changes.Add(new GitFileStatus { FilePath = filePath, Status = status });
        }

        return changes;
    }

    private static int CountPorcelainChangedFiles(string porcelainOutput)
    {
        return ParsePorcelainStatus(porcelainOutput).Count;
    }

    private static GitResult ExecuteStageAndCommit(
        string targetDirectory,
        string commitMessage,
        bool allowEmpty)
    {
        try
        {
            if (!GitRepositoryHelper.ContainsGitRepository(targetDirectory))
                return Failure("Not a git repository. Use Git Init first.");

            // Step 1: stage all changes — must fully exit before commit starts.
            var addResult = RunGitSync(targetDirectory, "add .");
            if (addResult.ExitCode != 0)
                return Failure(FormatGitError("git add failed.", addResult));

            // Step 2: commit only after add has completed.
            var commitArguments = allowEmpty
                ? $"commit --allow-empty -m {QuoteProcessArgument(commitMessage)}"
                : $"commit -m {QuoteProcessArgument(commitMessage)}";

            var commitResult = RunGitSync(targetDirectory, commitArguments);

            if (commitResult.ExitCode == 0)
                return Success("Changes committed.");

            if (!allowEmpty && IsNothingToCommitOutput(commitResult))
            {
                return new GitResult
                {
                    Success = false,
                    NoChangesToCommit = true,
                    Message = "Nothing to commit."
                };
            }

            return Failure(FormatGitError("git commit failed.", commitResult));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return Failure("Git is not installed or not on PATH.");
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
    }

    public static Task<bool> IsGitRepositoryAsync(string directory)
    {
        if (!TryNormalizeDirectory(directory, out var targetDirectory, out _))
            return Task.FromResult(false);

        return Task.FromResult(GitRepositoryHelper.ContainsGitRepository(targetDirectory));
    }

    public static async Task<GitCommitHistoryResult> GetCommitHistoryAsync(string directory, int limit = 30)
    {
        if (!TryResolveRepositoryRoot(directory, out var repositoryRoot, out var error))
            return GitCommitHistoryResult.Failed(error);

        try
        {
            var result = await RunGitAsync(
                repositoryRoot,
                $"log --pretty=format:%h%x1f%ar%x1f%s -n {limit}").ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                var stderr = result.StandardError + result.StandardOutput;
                if (stderr.Contains("does not have any commits yet", StringComparison.OrdinalIgnoreCase) ||
                    stderr.Contains("bad default revision", StringComparison.OrdinalIgnoreCase))
                    return GitCommitHistoryResult.Succeeded([]);

                return GitCommitHistoryResult.Failed(FormatGitError("git log failed.", result));
            }

            var commits = ParseCommitLog(result.StandardOutput);

            return GitCommitHistoryResult.Succeeded(commits);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return GitCommitHistoryResult.Failed("Git is not installed or not on PATH.");
        }
        catch (Exception ex)
        {
            return GitCommitHistoryResult.Failed(ex.Message);
        }
    }

    public static async Task<string> GetHeadShortHashAsync(string directory)
    {
        if (!TryResolveRepositoryRoot(directory, out var repositoryRoot, out _))
            return string.Empty;

        try
        {
            var result = await RunGitAsync(repositoryRoot, "rev-parse --short HEAD").ConfigureAwait(false);
            return result.ExitCode == 0 ? result.StandardOutput.Trim() : string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static async Task<GitResult> RestoreToCommitAsync(string directory, string commitHash)
    {
        if (!TryResolveRepositoryRoot(directory, out var repositoryRoot, out var error))
            return Failure(error);

        if (!IsValidCommitHash(commitHash))
            return Failure("Invalid commit hash.");

        try
        {
            var resetResult = await RunGitAsync(repositoryRoot, $"reset --hard {commitHash}")
                .ConfigureAwait(false);
            if (resetResult.ExitCode != 0)
                return Failure(FormatGitError("git reset failed.", resetResult));

            var cleanResult = await RunGitAsync(repositoryRoot, "clean -fd").ConfigureAwait(false);
            if (cleanResult.ExitCode != 0)
                return Failure(FormatGitError("git clean failed.", cleanResult));

            return Success("Repository restored to selected commit.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return Failure("Git is not installed or not on PATH.");
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
    }

    public static async Task<GitResult> DeleteCommitAsync(string directory, string commitHash)
    {
        if (!TryResolveRepositoryRoot(directory, out var repositoryRoot, out var error))
            return Failure(error);

        if (!IsValidCommitHash(commitHash))
            return Failure("Invalid commit hash.");

        try
        {
            var rebaseResult = await RunGitAsync(
                repositoryRoot,
                $"rebase --onto {commitHash}^ {commitHash} HEAD").ConfigureAwait(false);

            if (rebaseResult.ExitCode == 0)
                return Success("Commit deleted from history.");

            await RunGitAsync(repositoryRoot, "rebase --abort").ConfigureAwait(false);

            return new GitResult
            {
                Success = false,
                RebaseConflictAborted = true,
                Message = "Could not delete commit due to file conflicts. Rebase aborted to keep your files safe."
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return Failure("Git is not installed or not on PATH.");
        }
        catch (Exception ex)
        {
            return Failure(ex.Message);
        }
    }

    private static List<GitCommit> ParseCommitLog(string output)
    {
        const char fieldSeparator = '\x1f';
        var commits = new List<GitCommit>();

        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(fieldSeparator, 3);
            if (parts.Length < 3)
                continue;

            var hash = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(hash))
                continue;

            commits.Add(new GitCommit
            {
                Hash = hash,
                Date = parts[1].Trim(),
                Message = parts[2].Trim()
            });
        }

        return commits;
    }

    private static bool IsValidCommitHash(string hash) =>
        !string.IsNullOrWhiteSpace(hash) && CommitHashRegex().IsMatch(hash);

    [GeneratedRegex("^[0-9a-fA-F]+$")]
    private static partial Regex CommitHashRegex();

    private static bool TryNormalizeDirectory(string directory, out string normalizedDirectory, out string error)
    {
        normalizedDirectory = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(directory))
        {
            error = "No target directory is available.";
            return false;
        }

        try
        {
            normalizedDirectory = Path.GetFullPath(directory);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        if (!Directory.Exists(normalizedDirectory))
        {
            error = "Target directory does not exist.";
            return false;
        }

        return true;
    }

    private static bool TryResolveRepositoryRoot(string directory, out string repositoryRoot, out string error)
    {
        repositoryRoot = string.Empty;
        error = string.Empty;

        if (!TryNormalizeDirectory(directory, out var normalizedDirectory, out error))
            return false;

        repositoryRoot = GitRepositoryHelper.GetGitRepositoryRoot(normalizedDirectory) ?? string.Empty;
        if (string.IsNullOrEmpty(repositoryRoot))
        {
            error = "Not a git repository. Use Git Init first.";
            return false;
        }

        return true;
    }

    private static async Task<GitProcessResult> RunGitAsync(string workingDirectory, string arguments) =>
        await Task.Run(() => RunGitSync(workingDirectory, arguments)).ConfigureAwait(false);

    private static GitProcessResult RunGitSync(string workingDirectory, string arguments)
    {
        var nativeWorkingDirectory = Path.GetFullPath(workingDirectory);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = nativeWorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };

        process.Start();

        // Read streams on worker threads before WaitForExit to avoid pipe buffer deadlocks.
        var stdoutTask = Task.Run(() => process.StandardOutput.ReadToEnd());
        var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
        process.WaitForExit();

        return new GitProcessResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = stdoutTask.GetAwaiter().GetResult(),
            StandardError = stderrTask.GetAwaiter().GetResult()
        };
    }

    private static string QuoteProcessArgument(string value) =>
        $"\"{value.Replace("\"", "\\\"")}\"";

    private static string GetCombinedProcessOutput(GitProcessResult result) =>
        $"{result.StandardOutput}{result.StandardError}";

    private static bool IsNothingToAmendOutput(GitProcessResult result)
    {
        var fullOutput = GetCombinedProcessOutput(result);
        return fullOutput.Contains("nothing to amend", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNothingToCommitOutput(GitProcessResult result)
    {
        var fullOutput = GetCombinedProcessOutput(result);
        return fullOutput.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase) ||
               fullOutput.Contains("no changes added to commit", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatGitError(string prefix, GitProcessResult result)
    {
        var details = result.StandardError.Trim();
        if (string.IsNullOrWhiteSpace(details))
            details = result.StandardOutput.Trim();

        return string.IsNullOrWhiteSpace(details) ? prefix : $"{prefix} {details}";
    }

    private static GitResult Success(string message) =>
        new() { Success = true, Message = message };

    private static GitResult Failure(string message) =>
        new() { Success = false, Message = message };

    private sealed class GitProcessResult
    {
        public int ExitCode { get; init; }
        public string StandardOutput { get; init; } = string.Empty;
        public string StandardError { get; init; } = string.Empty;
    }
}
