using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FileExplorer.Services;

public static class RemovableDriveEjectService
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FsctlLockVolume = 0x00090018;
    private const uint FsctlDismountVolume = 0x00090020;
    private const uint IoctlStorageEjectMedia = 0x002D4808;

    public static bool CanEject(string driveRoot)
    {
        try
        {
            var root = Path.GetPathRoot(driveRoot);
            return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Removable;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryEject(string driveRoot, out string? errorMessage)
    {
        errorMessage = null;

        var root = Path.GetPathRoot(driveRoot);
        if (string.IsNullOrEmpty(root))
        {
            errorMessage = LocalizationManager.Instance["UI_TreeEjectInvalidDrive"];
            return false;
        }

        DriveInfo drive;
        try
        {
            drive = new DriveInfo(root);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }

        if (drive.DriveType != DriveType.Removable)
        {
            errorMessage = LocalizationManager.Instance["UI_TreeEjectNotRemovable"];
            return false;
        }

        var volumePath = $@"\\.\{root.TrimEnd('\\')}";
        using var handle = CreateFile(
            volumePath,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            errorMessage = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return false;
        }

        DeviceIoControl(handle, FsctlLockVolume, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);
        DeviceIoControl(handle, FsctlDismountVolume, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);

        if (DeviceIoControl(handle, IoctlStorageEjectMedia, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
            return true;

        errorMessage = new Win32Exception(Marshal.GetLastWin32Error()).Message;
        return false;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);
}
