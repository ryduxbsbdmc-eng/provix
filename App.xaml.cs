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
        ThemeManager.ApplyTheme(SettingsManager.Instance.Current.Theme);
        LocalizationManager.Instance.LoadLanguage(SettingsManager.Instance.Current.Language);

        base.OnStartup(e);
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
