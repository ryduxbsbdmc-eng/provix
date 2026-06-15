using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Vanara.PInvoke;

namespace FileExplorer.Services;

public static class NativeShellContextMenuService
{
    private const uint CommandOffset = 1;
    private const uint CommandLast = 0x7FFF;

    public static void ShowContextMenu(
        IReadOnlyList<string> filePaths,
        int screenX,
        int screenY,
        IntPtr ownerHwnd,
        Window ownerWindow)
    {
        if (ownerHwnd == IntPtr.Zero)
            throw new InvalidOperationException("Owner HWND is zero. Ensure MainWindow handle is created before showing the native menu.");

        if (filePaths.Count == 0)
            throw new InvalidOperationException("No items available for the Windows context menu.");

        var sanitizedPaths = filePaths
            .Select(SanitizeShellPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (sanitizedPaths.Length == 0)
            throw new InvalidOperationException("No valid file paths were provided.");

        if (!AllPathsShareParent(sanitizedPaths, out var parentError))
            throw new InvalidOperationException(parentError);

        var absolutePidls = new List<Shell32.PIDL>();
        var relativeChildPidls = new List<IntPtr>();
        HMENU menuHandle = HMENU.NULL;
        Shell32.IContextMenu? contextMenu = null;
        Shell32.IShellFolder? parentFolder = null;
        ShellMenuMessageHook? messageHook = null;

        try
        {
            foreach (var path in sanitizedPaths)
            {
                var parseResult = Shell32.SHParseDisplayName(
                    path,
                    null,
                    out var absolutePidl);

                if (parseResult.Failed || absolutePidl is null)
                    throw new InvalidOperationException($"SHParseDisplayName failed with HRESULT 0x{parseResult.Code:X8} for '{path}'.");

                absolutePidls.Add(absolutePidl);

                var bindResult = Shell32.SHBindToParent(
                    absolutePidl,
                    typeof(Shell32.IShellFolder).GUID,
                    out var parentObject,
                    out var relativeChildPidl);

                if (bindResult.Failed || parentObject is not Shell32.IShellFolder boundParent || relativeChildPidl == IntPtr.Zero)
                    throw new InvalidOperationException($"SHBindToParent failed with HRESULT 0x{bindResult.Code:X8} for '{path}'.");

                parentFolder ??= boundParent;
                relativeChildPidls.Add(relativeChildPidl);
            }

            if (parentFolder is null || relativeChildPidls.Count == 0)
                throw new InvalidOperationException("Unable to resolve shell items for the selected paths.");

            contextMenu = parentFolder.GetUIObjectOf<Shell32.IContextMenu>(
                (HWND)ownerHwnd,
                relativeChildPidls.ToArray());

            if (contextMenu is null)
                throw new InvalidOperationException("IShellFolder.GetUIObjectOf did not return IContextMenu.");

            menuHandle = User32.CreatePopupMenu();
            if (menuHandle.IsNull)
                throw new InvalidOperationException("CreatePopupMenu returned a null handle.");

            var queryResult = contextMenu.QueryContextMenu(
                menuHandle,
                0,
                CommandOffset,
                CommandLast,
                Shell32.CMF.CMF_NORMAL | Shell32.CMF.CMF_EXPLORE);

            if (queryResult.Failed)
                throw new InvalidOperationException($"IContextMenu.QueryContextMenu failed with HRESULT 0x{queryResult.Code:X8}.");

            var menuItemCount = User32.GetMenuItemCount(menuHandle);
            if (menuItemCount <= 0)
                throw new InvalidOperationException("Native shell context menu was created but contains no items.");

            messageHook = new ShellMenuMessageHook(ownerWindow, contextMenu);

            User32.SetForegroundWindow((HWND)ownerHwnd);

            var selectedCommand = User32.TrackPopupMenuEx(
                menuHandle,
                User32.TrackPopupMenuFlags.TPM_LEFTALIGN |
                User32.TrackPopupMenuFlags.TPM_RETURNCMD |
                User32.TrackPopupMenuFlags.TPM_RIGHTBUTTON,
                screenX,
                screenY,
                (HWND)ownerHwnd);

            if (selectedCommand == 0)
                return;

            var invokeInfo = new Shell32.CMINVOKECOMMANDINFOP((int)(selectedCommand - CommandOffset))
            {
                hwnd = (HWND)ownerHwnd
            };

            contextMenu.InvokeCommand(in invokeInfo);
        }
        finally
        {
            messageHook?.Dispose();

            if (!menuHandle.IsNull)
                User32.DestroyMenu(menuHandle);

            foreach (var absolutePidl in absolutePidls)
                absolutePidl.Dispose();

            if (contextMenu is not null)
                Marshal.ReleaseComObject(contextMenu);

            if (parentFolder is not null)
                Marshal.ReleaseComObject(parentFolder);
        }
    }

    internal static string SanitizeShellPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is empty.", nameof(path));

        var fullPath = Path.GetFullPath(path);

        if (fullPath.Length >= 2 && fullPath[1] == ':')
        {
            var root = $"{fullPath[0]}:\\";
            if (string.Equals(fullPath.TrimEnd('\\', '/'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }
        }

        return fullPath.TrimEnd('\\', '/');
    }

    private static bool AllPathsShareParent(string[] sanitizedPaths, out string errorMessage)
    {
        errorMessage = string.Empty;

        if (sanitizedPaths.Length <= 1)
            return true;

        string? expectedParent = null;

        foreach (var path in sanitizedPaths)
        {
            var parent = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(parent))
                parent = path;

            expectedParent ??= parent;

            if (!string.Equals(parent, expectedParent, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Selected items must be in the same folder.";
                return false;
            }
        }

        return true;
    }

    private sealed class ShellMenuMessageHook : IDisposable
    {
        private const int WmInitMenuPopup = 0x0117;
        private const int WmDrawItem = 0x002B;
        private const int WmMeasureItem = 0x002C;
        private const int WmMenuchar = 0x0123;
        private const int WmNextMenu = 0x0213;

        private readonly HwndSource? _hwndSource;
        private readonly Shell32.IContextMenu3? _contextMenu3;
        private readonly Shell32.IContextMenu2? _contextMenu2;

        public ShellMenuMessageHook(Window ownerWindow, Shell32.IContextMenu contextMenu)
        {
            _contextMenu3 = contextMenu as Shell32.IContextMenu3;
            _contextMenu2 = _contextMenu3 is null ? contextMenu as Shell32.IContextMenu2 : null;

            if (_contextMenu3 is null && _contextMenu2 is null)
                return;

            var ownerHandle = new WindowInteropHelper(ownerWindow).Handle;
            if (ownerHandle == IntPtr.Zero)
                return;

            _hwndSource = HwndSource.FromHwnd(ownerHandle);
            _hwndSource?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WmInitMenuPopup:
                case WmDrawItem:
                case WmMeasureItem:
                case WmMenuchar:
                case WmNextMenu:
                    if (_contextMenu3 is not null)
                    {
                        _contextMenu3.HandleMenuMsg2((uint)msg, wParam, lParam, out _);
                        handled = true;
                    }
                    else if (_contextMenu2 is not null)
                    {
                        _contextMenu2.HandleMenuMsg((uint)msg, wParam, lParam);
                        handled = true;
                    }

                    break;
            }

            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_hwndSource is not null)
                _hwndSource.RemoveHook(WndProc);
        }
    }
}
