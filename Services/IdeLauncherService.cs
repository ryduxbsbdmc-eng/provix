using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace FileExplorer.Services;

public enum IdeKind
{
    Cursor,
    VsCode,
    RunWithPython
}

public static class IdeLauncherService
{
    public static bool TryLaunch(IdeKind ide, string itemPath, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(itemPath))
        {
            errorMessage = "Item path is invalid.";
            return false;
        }

        var fullPath = Path.GetFullPath(itemPath);

        if (ide == IdeKind.RunWithPython)
            return TryRunWithPython(fullPath, out errorMessage);

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            errorMessage = "Item path is invalid.";
            return false;
        }

        var commands = GetCommandCandidates(ide);

        foreach (var command in commands)
        {
            if (TryRunCommand(command, $"\"{fullPath}\"", out var launchError))
                return true;

            errorMessage = launchError;
        }

        if (string.IsNullOrWhiteSpace(errorMessage))
            errorMessage = GetDefaultNotFoundMessage(ide);

        return false;
    }

    private static bool TryRunWithPython(string fullPath, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (fullPath.EndsWith(".py", StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
            return TryRunCommand("python", $"\"{fullPath}\"", out errorMessage);

        if (Directory.Exists(fullPath))
            return TryRunInWorkingDirectory("python", fullPath, out errorMessage);

        errorMessage = "Select a Python (.py) file or folder.";
        return false;
    }

    private static IReadOnlyList<string> GetCommandCandidates(IdeKind ide) =>
        ide switch
        {
            IdeKind.Cursor => ["cursor"],
            IdeKind.VsCode => ["code"],
            _ => []
        };

    private static bool TryRunCommand(string command, string arguments, out string errorMessage)
    {
        errorMessage = string.Empty;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command} {arguments}",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false
            });

            if (process is null)
            {
                errorMessage = $"Unable to start {command}.";
                return false;
            }

            return true;
        }
        catch (Win32Exception ex)
        {
            errorMessage = ex.NativeErrorCode == 2
                ? $"{command} was not found on PATH."
                : ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static bool TryRunInWorkingDirectory(string command, string workingDirectory, out string errorMessage)
    {
        errorMessage = string.Empty;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                WorkingDirectory = workingDirectory,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = false
            });

            if (process is null)
            {
                errorMessage = $"Unable to start {command}.";
                return false;
            }

            return true;
        }
        catch (Win32Exception ex)
        {
            errorMessage = ex.NativeErrorCode == 2
                ? $"{command} was not found on PATH."
                : ex.Message;
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static string GetDefaultNotFoundMessage(IdeKind ide) =>
        ide switch
        {
            IdeKind.Cursor => "Cursor is not installed or not on PATH.",
            IdeKind.VsCode => "VS Code is not installed or not on PATH.",
            IdeKind.RunWithPython => "Python is not installed or not on PATH.",
            _ => "IDE launcher failed."
        };
}
