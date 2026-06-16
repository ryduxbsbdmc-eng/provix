using System.Diagnostics;
using Microsoft.Win32;

namespace FileExplorer.Services;

/// <summary>
/// Registers or removes Provix from the per-user Windows startup list
/// (HKCU\Software\Microsoft\Windows\CurrentVersion\Run).
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Provix";

    private static string? ExecutablePath
    {
        get
        {
            try
            {
                var path = Process.GetCurrentProcess().MainModule?.FileName;
                return string.IsNullOrWhiteSpace(path) ? Environment.ProcessPath : path;
            }
            catch
            {
                return Environment.ProcessPath;
            }
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Returns true when the registry was updated successfully.</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
                return false;

            if (enabled)
            {
                var exe = ExecutablePath;
                if (string.IsNullOrWhiteSpace(exe))
                    return false;

                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Keeps the registry entry in sync with the saved setting (e.g. after the exe moved).</summary>
    public static void Reconcile(bool desiredEnabled)
    {
        if (desiredEnabled != IsEnabled())
        {
            SetEnabled(desiredEnabled);
        }
        else if (desiredEnabled)
        {
            // Refresh the stored path in case the executable was moved or updated.
            SetEnabled(true);
        }
    }
}
