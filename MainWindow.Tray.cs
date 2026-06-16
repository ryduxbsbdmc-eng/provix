using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using FileExplorer.Services;
using Hardcodet.Wpf.TaskbarNotification;

namespace FileExplorer;

public partial class MainWindow
{
    private TaskbarIcon? _trayIcon;

    /// <summary>Called once from MainWindow_Loaded to wire up tray + startup features.</summary>
    private void InitializeSystemIntegration()
    {
        StartupManager.Reconcile(SettingsManager.Instance.Current.RunAtStartup);
        ApplyTrayPreference(SettingsManager.Instance.Current.MinimizeToTray);
    }

    private void ApplyTrayPreference(bool enabled)
    {
        if (enabled)
        {
            EnsureTrayIcon();
        }
        else
        {
            DisposeTrayIcon();
            // If the window was hidden in the tray when the feature is turned off, bring it back.
            if (!IsVisible)
                RestoreFromTray();
        }
    }

    private void EnsureTrayIcon()
    {
        if (_trayIcon is not null)
            return;

        var loc = LocalizationManager.Instance;

        var openItem = new MenuItem { Header = loc["UI_TrayOpen"] };
        openItem.Click += (_, _) => RestoreFromTray();

        var exitItem = new MenuItem { Header = loc["UI_TrayExit"] };
        exitItem.Click += (_, _) => Close();

        var menu = new ContextMenu();
        menu.Items.Add(openItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = loc["UI_AppTitle"],
            ContextMenu = menu,
            Icon = LoadTrayIcon(),
        };
        _trayIcon.TrayMouseDoubleClick += (_, _) => RestoreFromTray();
    }

    private void RefreshTrayLocalization()
    {
        if (_trayIcon is null)
            return;

        var loc = LocalizationManager.Instance;
        _trayIcon.ToolTipText = loc["UI_AppTitle"];

        if (_trayIcon.ContextMenu is { } menu && menu.Items.Count >= 3)
        {
            if (menu.Items[0] is MenuItem open)
                open.Header = loc["UI_TrayOpen"];
            if (menu.Items[^1] is MenuItem exit)
                exit.Header = loc["UI_TrayExit"];
        }
    }

    private void HideToTray()
    {
        EnsureTrayIcon();
        Hide();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon is null)
            return;

        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private static System.Drawing.Icon? LoadTrayIcon()
    {
        try
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe))
            {
                var extracted = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                if (extracted is not null)
                    return extracted;
            }
        }
        catch
        {
            // Fall back to a generic system icon below.
        }

        return System.Drawing.SystemIcons.Application;
    }
}
