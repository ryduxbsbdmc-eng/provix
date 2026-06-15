using System.Windows;
using System.Windows.Threading;
using FileExplorer.Services;

namespace FileExplorer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        SettingsManager.Instance.Load();
        AppCacheService.EnsureDirectories();
        ExternalToolsService.Initialize();
        PackSyncService.SyncBuiltInIconPacks();
        PackSyncService.SyncBuiltInThemes();
        var settings = SettingsManager.Instance.Current;
        ThemeManager.ApplyTheme(settings.Theme, settings.CustomThemePath);
        LocalizationManager.Instance.LoadLanguage(SettingsManager.Instance.Current.Language);

        SessionEnding += App_SessionEnding;

        base.OnStartup(e);
    }

    private void App_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        if (Current.MainWindow is MainWindow window)
            window.PersistSessionSettings();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.ToString(),
            "Fatal Startup Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var message = e.ExceptionObject switch
        {
            Exception ex => ex.ToString(),
            _ => e.ExceptionObject?.ToString() ?? "Unknown fatal error."
        };

        MessageBox.Show(
            message,
            "Fatal Startup Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
