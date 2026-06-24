using System.IO;
using System.Runtime.InteropServices;
using Vanara.PInvoke;

namespace FileExplorer.Services;

public static class DrivePropertiesService
{
    private const uint ShopFilePath = 2;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SHObjectProperties(IntPtr hwnd, uint dwFlags, string pszObject, string? pszPage);

    public static void Show(string driveRoot, IntPtr ownerHwnd)
    {
        if (ownerHwnd == IntPtr.Zero)
            throw new InvalidOperationException("Owner HWND is zero.");

        var path = NormalizeDriveRoot(driveRoot);

        if (TryInvokePropertiesVerb(path, ownerHwnd))
            return;

        if (SHObjectProperties(ownerHwnd, ShopFilePath, path, null))
            return;

        throw new InvalidOperationException($"Unable to open properties for '{path}'.");
    }

    private static bool TryInvokePropertiesVerb(string path, IntPtr ownerHwnd)
    {
        Shell32.PIDL? absolutePidl = null;
        Shell32.IShellFolder? parentFolder = null;
        Shell32.IContextMenu? contextMenu = null;

        try
        {
            var parseResult = Shell32.SHParseDisplayName(path, null, out absolutePidl);
            if (parseResult.Failed || absolutePidl is null)
                return false;

            var bindResult = Shell32.SHBindToParent(
                absolutePidl,
                typeof(Shell32.IShellFolder).GUID,
                out var parentObject,
                out var relativeChildPidl);

            if (bindResult.Failed ||
                parentObject is not Shell32.IShellFolder boundParent ||
                relativeChildPidl == IntPtr.Zero)
            {
                return false;
            }

            parentFolder = boundParent;

            contextMenu = parentFolder.GetUIObjectOf<Shell32.IContextMenu>(
                (HWND)ownerHwnd,
                new[] { relativeChildPidl });

            if (contextMenu is null)
                return false;

            var invokeInfo = new Shell32.CMINVOKECOMMANDINFO("properties")
            {
                hwnd = (HWND)ownerHwnd
            };

            var invokeResult = contextMenu.InvokeCommand(in invokeInfo);
            return invokeResult.Succeeded;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (contextMenu is not null)
                Marshal.ReleaseComObject(contextMenu);

            if (parentFolder is not null)
                Marshal.ReleaseComObject(parentFolder);

            absolutePidl?.Dispose();
        }
    }

    private static string NormalizeDriveRoot(string driveRoot)
    {
        if (string.IsNullOrWhiteSpace(driveRoot))
            throw new ArgumentException("Drive path is empty.", nameof(driveRoot));

        var root = Path.GetPathRoot(driveRoot) ?? driveRoot;
        return root.EndsWith('\\') ? root : root + '\\';
    }
}
