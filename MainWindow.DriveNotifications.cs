using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using FileExplorer.Models;

namespace FileExplorer;

public partial class MainWindow
{
    private HwndSource? _driveNotificationSource;
    private DispatcherTimer? _driveRefreshTimer;
    private int _driveRefreshGeneration;

    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVNODES_CHANGED = 0x0007;
    private const int DBT_DEVTYP_VOLUME = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct DevBroadcastHdr
    {
        public int DbchSize;
        public int DbchDeviceType;
        public int DbchReserved;
    }

    private void InitializeDriveChangeNotifications()
    {
        var helper = new WindowInteropHelper(this);
        if (helper.Handle == IntPtr.Zero)
            return;

        _driveNotificationSource = HwndSource.FromHwnd(helper.Handle);
        _driveNotificationSource?.AddHook(DriveNotificationWndProc);
    }

    private void DisposeDriveChangeNotifications()
    {
        _driveRefreshTimer?.Stop();
        _driveRefreshTimer = null;

        if (_driveNotificationSource is not null)
        {
            _driveNotificationSource.RemoveHook(DriveNotificationWndProc);
            _driveNotificationSource = null;
        }
    }

    private IntPtr DriveNotificationWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_DEVICECHANGE)
            return IntPtr.Zero;

        switch (wParam.ToInt32())
        {
            case DBT_DEVICEARRIVAL:
            case DBT_DEVICEREMOVECOMPLETE:
                if (IsVolumeDeviceChange(lParam))
                    ScheduleDriveTreeRefresh(includeDelayedRetry: true);
                break;

            case DBT_DEVNODES_CHANGED:
                ScheduleDriveTreeRefresh(includeDelayedRetry: false);
                break;
        }

        return IntPtr.Zero;
    }

    private static bool IsVolumeDeviceChange(IntPtr lParam)
    {
        if (lParam == IntPtr.Zero)
            return true;

        var header = Marshal.PtrToStructure<DevBroadcastHdr>(lParam);
        return header.DbchDeviceType == DBT_DEVTYP_VOLUME;
    }

    private void ScheduleDriveTreeRefresh(bool includeDelayedRetry)
    {
        var generation = ++_driveRefreshGeneration;

        _driveRefreshTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _driveRefreshTimer.Stop();
        _driveRefreshTimer.Tick -= DriveRefreshTimer_Tick;
        _driveRefreshTimer.Tick += DriveRefreshTimer_Tick;
        _driveRefreshTimer.Start();

        if (!includeDelayedRetry)
            return;

        _ = Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(2000);
            if (generation != _driveRefreshGeneration)
                return;

            RefreshDriveTreeAfterDeviceChange();
        }, DispatcherPriority.Background);
    }

    private void DriveRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _driveRefreshTimer?.Stop();
        RefreshDriveTreeAfterDeviceChange();
    }

    private void RefreshDriveTreeAfterDeviceChange()
    {
        var roots = _fileSystemService.GetRootNodes();

        _driveNodes.Clear();
        foreach (var node in roots)
            _driveNodes.Add(node);

        foreach (var pane in _panes)
        {
            var path = pane.CurrentPath;
            if (string.IsNullOrEmpty(path))
                continue;

            if (!IsPathAccessible(path))
            {
                NavigatePaneFromUnavailableDrive(pane);
                continue;
            }

            if (pane == _activePane)
                SyncTreeToPath(path);
        }
    }

    private static bool IsPathAccessible(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root))
                return Directory.Exists(path);

            if (!Directory.Exists(root))
                return false;

            var drive = new DriveInfo(root);
            return drive.IsReady && Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private void NavigatePaneFromUnavailableDrive(DirectoryPaneState pane)
    {
        var syncTree = pane == _activePane;
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        if (!string.IsNullOrWhiteSpace(desktopPath) && Directory.Exists(desktopPath))
        {
            NavigateToDirectory(pane, desktopPath, syncTree: syncTree);
            return;
        }

        var fallback = _driveNodes.FirstOrDefault(node =>
            !string.IsNullOrEmpty(node.FullPath) && Directory.Exists(node.FullPath));

        if (fallback is not null)
            NavigateToDirectory(pane, fallback.FullPath, syncTree: syncTree);
    }
}
