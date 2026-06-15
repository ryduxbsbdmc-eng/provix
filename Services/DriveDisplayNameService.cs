using System.IO;
using Vanara.PInvoke;

namespace FileExplorer.Services;

public static class DriveDisplayNameService
{
    public static string GetDisplayName(DriveInfo drive)
    {
        var root = drive.Name;
        if (TryGetShellDisplayName(root, out var shellName))
            return shellName;

        return GetFallbackDisplayName(drive);
    }

    public static string GetDisplayName(string driveRoot, DriveType driveType, bool isReady)
    {
        var normalizedRoot = NormalizeDriveRoot(driveRoot);
        if (TryGetShellDisplayName(normalizedRoot, out var shellName))
            return shellName;

        try
        {
            if (isReady)
                return GetDisplayName(new DriveInfo(normalizedRoot));
        }
        catch
        {
            // Fall back to letter-only label below.
        }

        var letter = normalizedRoot.TrimEnd('\\');
        if (!isReady && driveType == DriveType.Removable)
        {
            var loc = LocalizationManager.Instance;
            return $"{loc["UI_DriveRemovable"]} ({letter})";
        }

        return letter;
    }

    private static bool TryGetShellDisplayName(string path, out string displayName)
    {
        displayName = string.Empty;

        try
        {
            var hr = Shell32.SHParseDisplayName(path, null, out var pidl, 0, out _);
            if (hr.Failed || pidl is null)
                return false;

            using (pidl)
            {
                hr = Shell32.SHGetNameFromIDList(pidl, (Shell32.SIGDN)0x80000000, out var name);
                if (hr.Failed || string.IsNullOrWhiteSpace(name))
                    return false;

                displayName = name;
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    private static string GetFallbackDisplayName(DriveInfo drive)
    {
        var letter = drive.Name.TrimEnd('\\');
        if (!drive.IsReady)
            return letter;

        var label = drive.VolumeLabel?.Trim();
        if (!string.IsNullOrWhiteSpace(label))
            return $"{label} ({letter})";

        var loc = LocalizationManager.Instance;
        var typeName = drive.DriveType switch
        {
            DriveType.Fixed => loc["UI_DriveLocalDisk"],
            DriveType.Removable => loc["UI_DriveRemovable"],
            DriveType.Network => loc["UI_DriveNetwork"],
            DriveType.CDRom => loc["UI_DriveCdRom"],
            _ => loc["UI_DriveGeneric"]
        };

        return $"{typeName} ({letter})";
    }

    private static string NormalizeDriveRoot(string driveRoot)
    {
        if (string.IsNullOrWhiteSpace(driveRoot))
            return driveRoot;

        var root = Path.GetPathRoot(driveRoot) ?? driveRoot;
        return root.EndsWith('\\') ? root : root + '\\';
    }
}
