using System.IO;
using FileExplorer.Models;

namespace FileExplorer.Services;

public sealed class AiCommandExecutor
{
    public AiCommandResult ExecuteAll(string baseDirectory, IReadOnlyList<AiCommand> commands)
    {
        if (commands.Count == 0)
            return AiCommandResult.Failed("No operations to execute.");

        var basePath = Path.GetFullPath(baseDirectory);

        if (!Directory.Exists(basePath))
            return AiCommandResult.Failed("The target directory no longer exists.");

        var executed = 0;
        string? lastError = null;

        foreach (var command in commands)
        {
            try
            {
                if (!TryExecuteSingle(basePath, command, out var error))
                {
                    lastError = error;
                    break;
                }

                executed++;
            }
            catch (UnauthorizedAccessException)
            {
                lastError = "Access denied while executing an operation.";
                break;
            }
            catch (IOException ex)
            {
                lastError = ex.Message;
                break;
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                break;
            }
        }

        if (executed == 0)
            return AiCommandResult.Failed(lastError ?? "Unable to execute operations.");

        var message = executed == commands.Count
            ? $"Completed {executed} operation(s)."
            : $"Completed {executed} of {commands.Count} operation(s). {lastError}";

        return AiCommandResult.Succeeded(executed, message);
    }

    private bool TryExecuteSingle(string basePath, AiCommand command, out string? error)
    {
        error = null;
        var op = command.Op.Trim().ToUpperInvariant();

        return op switch
        {
            "MKDIR" => TryMkdir(basePath, command.Path, out error),
            "MOVE" => TryMove(basePath, command.Src, command.Dest, out error),
            "RENAME" => TryRename(basePath, command.Src, command.Dest, out error),
            _ => Fail($"Unsupported operation: {command.Op}", out error)
        };
    }

    private static bool TryMkdir(string basePath, string? relativePath, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(relativePath))
            return Fail("MKDIR requires a path.", out error);

        if (!TryResolveSafePath(basePath, relativePath, out var fullPath, out error))
            return false;

        if (Directory.Exists(fullPath) || File.Exists(fullPath))
            return true;

        Directory.CreateDirectory(fullPath);
        return true;
    }

    private bool TryMove(string basePath, string? src, string? dest, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dest))
            return Fail("MOVE requires src and dest.", out error);

        if (!TryResolveSafePath(basePath, src, out var sourcePath, out error))
            return false;

        if (!TryResolveSafePath(basePath, dest, out var destinationPath, out error))
            return false;

        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            return Fail($"Source not found: {src}", out error);

        var destParent = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrEmpty(destParent))
            return Fail("Invalid destination path.", out error);

        if (!Directory.Exists(destParent))
            Directory.CreateDirectory(destParent);

        if (Directory.Exists(sourcePath))
        {
            if (Directory.Exists(destinationPath) || File.Exists(destinationPath))
                return Fail($"Destination already exists: {dest}", out error);

            Directory.Move(sourcePath, destinationPath);
            return true;
        }

        if (File.Exists(destinationPath))
            return Fail($"Destination already exists: {dest}", out error);

        File.Move(sourcePath, destinationPath);
        return true;
    }

    private static bool TryRename(string basePath, string? src, string? dest, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dest))
            return Fail("RENAME requires src and dest.", out error);

        if (!TryResolveSafePath(basePath, src, out var sourcePath, out error))
            return false;

        string destinationPath;
        if (dest.Contains('/') || dest.Contains('\\'))
        {
            if (!TryResolveSafePath(basePath, dest, out destinationPath, out error))
                return false;
        }
        else
        {
            var parent = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrEmpty(parent))
                return Fail("Unable to resolve rename destination.", out error);

            destinationPath = Path.GetFullPath(Path.Combine(parent, dest));
            if (!IsPathUnderBase(basePath, destinationPath))
                return Fail("Rename destination escapes the working directory.", out error);
        }

        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            return Fail($"Source not found: {src}", out error);

        if (Directory.Exists(sourcePath))
        {
            if (Directory.Exists(destinationPath) || File.Exists(destinationPath))
                return Fail($"Destination already exists: {dest}", out error);

            Directory.Move(sourcePath, destinationPath);
            return true;
        }

        if (File.Exists(destinationPath))
            return Fail($"Destination already exists: {dest}", out error);

        File.Move(sourcePath, destinationPath);
        return true;
    }

    private static bool TryResolveSafePath(string basePath, string relativePath, out string fullPath, out string? error)
    {
        fullPath = string.Empty;
        error = null;

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).Trim();

        if (Path.IsPathRooted(normalized))
        {
            error = "Absolute paths are not allowed.";
            return false;
        }

        if (normalized.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "." or ".."))
        {
            error = "Path traversal is not allowed.";
            return false;
        }

        fullPath = Path.GetFullPath(Path.Combine(basePath, normalized));

        if (!IsPathUnderBase(basePath, fullPath))
        {
            error = "Operation escapes the working directory.";
            return false;
        }

        return true;
    }

    private static bool IsPathUnderBase(string basePath, string candidatePath)
    {
        var normalizedBase = Path.GetFullPath(basePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedCandidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(normalizedBase, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
            return true;

        var prefix = normalizedBase + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Fail(string message, out string? error)
    {
        error = message;
        return false;
    }
}
