using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Web.WebView2.Core;

namespace FileExplorer.Services;

public enum ExternalTool
{
    Git,
    PowerShell,
    Python,
    Cursor,
    VsCode,
    WebView2
}

public static class ExternalToolsService
{
    private static bool _initialized;
    private static readonly Dictionary<ExternalTool, bool> Availability = new();

    public static void Initialize()
    {
        if (_initialized)
            return;

        Availability[ExternalTool.Git] = IsGitAvailable();
        Availability[ExternalTool.PowerShell] = IsPowerShellAvailable();
        Availability[ExternalTool.Python] = IsCommandOnPath("python");
        Availability[ExternalTool.Cursor] = IsCommandOnPath("cursor");
        Availability[ExternalTool.VsCode] = IsCommandOnPath("code");
        Availability[ExternalTool.WebView2] = IsWebView2Available();

        _initialized = true;
    }

    public static bool IsAvailable(ExternalTool tool)
    {
        if (!_initialized)
            Initialize();

        return Availability.TryGetValue(tool, out var available) && available;
    }

    public static bool IsIdeAvailable(IdeKind ide) =>
        ide switch
        {
            IdeKind.Cursor => IsAvailable(ExternalTool.Cursor),
            IdeKind.VsCode => IsAvailable(ExternalTool.VsCode),
            IdeKind.RunWithPython => IsAvailable(ExternalTool.Python),
            _ => false
        };

    private static bool IsGitAvailable()
    {
        if (!IsCommandOnPath("git"))
            return false;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process is null)
                return false;

            process.WaitForExit(2000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPowerShellAvailable()
    {
        var systemPowerShell = Path.Combine(
            Environment.SystemDirectory,
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        if (File.Exists(systemPowerShell))
            return true;

        return IsCommandOnPath("powershell");
    }

    private static bool IsWebView2Available()
    {
        try
        {
            var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return !string.IsNullOrWhiteSpace(version);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsCommandOnPath(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = command,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (process is null)
                return false;

            if (!process.WaitForExit(2000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore kill failures.
                }

                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }
}
