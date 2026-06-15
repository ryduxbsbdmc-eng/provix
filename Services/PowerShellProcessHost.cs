using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace FileExplorer.Services;

public sealed partial class PowerShellProcessHost : IDisposable
{
    private static readonly Regex CdCommandRegex = CdCommandPattern();
    private static readonly Regex SetLocationRegex = SetLocationPattern();

    private Process? _process;
    private StreamWriter? _stdin;
    private string _workingDirectory = string.Empty;
    private bool _disposed;

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<string>? ErrorReceived;
    public event EventHandler<int>? ProcessExited;
    public event EventHandler<string>? WorkingDirectoryChanged;

    public bool IsRunning => _process is { HasExited: false };

    public string WorkingDirectory => _workingDirectory;

    public void Start(string workingDirectory)
    {
        Stop();

        _workingDirectory = ResolveExistingDirectory(workingDirectory)
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -NoExit",
            WorkingDirectory = _workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            StandardInputEncoding = Encoding.UTF8
        };

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += OnOutputDataReceived;
        _process.ErrorDataReceived += OnErrorDataReceived;
        _process.Exited += OnProcessExited;

        if (!_process.Start())
            throw new InvalidOperationException("Failed to start PowerShell.");

        _stdin = _process.StandardInput;
        _stdin.AutoFlush = true;

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public void Stop()
    {
        if (_process is null)
            return;

        try
        {
            _process.OutputDataReceived -= OnOutputDataReceived;
            _process.ErrorDataReceived -= OnErrorDataReceived;
            _process.Exited -= OnProcessExited;

            if (!_process.HasExited)
            {
                try
                {
                    _stdin?.WriteLine("exit");
                    _stdin?.Flush();
                    if (!_process.WaitForExit(1500))
                        _process.Kill(entireProcessTree: true);
                }
                catch
                {
                    try { _process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                }
            }
        }
        finally
        {
            _stdin?.Dispose();
            _stdin = null;
            _process.Dispose();
            _process = null;
        }
    }

    public void SendCommand(string command)
    {
        if (_stdin is null || _process is { HasExited: true })
            throw new InvalidOperationException("PowerShell is not running.");

        var trimmed = command.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return;

        if (TryParseDirectoryChange(trimmed, out var newDirectory))
            UpdateWorkingDirectory(newDirectory, notify: true, sendToShell: false);

        _stdin.WriteLine(trimmed);
        _stdin.Flush();
    }

    public void SetWorkingDirectory(string directory, bool sendToShell = true)
    {
        var resolved = ResolveExistingDirectory(directory);
        if (resolved is null)
            return;

        UpdateWorkingDirectory(resolved, notify: false, sendToShell);
    }

    private void UpdateWorkingDirectory(string directory, bool notify, bool sendToShell)
    {
        _workingDirectory = directory;

        if (sendToShell && _stdin is not null && _process is { HasExited: false })
        {
            var escaped = directory.Replace("'", "''");
            _stdin.WriteLine($"Set-Location -LiteralPath '{escaped}'");
            _stdin.Flush();
        }

        if (notify)
            WorkingDirectoryChanged?.Invoke(this, directory);
    }

    private bool TryParseDirectoryChange(string command, out string resolvedDirectory)
    {
        resolvedDirectory = string.Empty;

        string? rawPath = null;

        var cdMatch = CdCommandRegex.Match(command);
        if (cdMatch.Success)
            rawPath = cdMatch.Groups[1].Value.Trim();

        var setLocationMatch = SetLocationRegex.Match(command);
        if (setLocationMatch.Success)
            rawPath = setLocationMatch.Groups[1].Value.Trim().Trim('"', '\'');

        if (string.IsNullOrWhiteSpace(rawPath))
            return false;

        if (string.Equals(rawPath, "~", StringComparison.Ordinal))
            rawPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        try
        {
            var combined = Path.IsPathRooted(rawPath)
                ? rawPath
                : Path.Combine(_workingDirectory, rawPath);

            resolvedDirectory = Path.GetFullPath(combined);

            if (!Directory.Exists(resolvedDirectory))
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolveExistingDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return null;

        try
        {
            var fullPath = Path.GetFullPath(directory);
            return Directory.Exists(fullPath) ? fullPath : null;
        }
        catch
        {
            return null;
        }
    }

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
            return;

        OutputReceived?.Invoke(this, e.Data);
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
            return;

        ErrorReceived?.Invoke(this, e.Data);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        var exitCode = _process?.ExitCode ?? -1;
        ProcessExited?.Invoke(this, exitCode);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
    }

    [GeneratedRegex(@"^\s*(?:cd|chdir)\s+(.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex CdCommandPattern();

    [GeneratedRegex(@"^\s*(?:Set-Location|sl)\s+(?:-LiteralPath\s+)?['""]?([^'""]+)['""]?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex SetLocationPattern();
}
