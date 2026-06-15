using System.IO;
using System.Windows;
using FileExplorer.Services;

namespace FileExplorer;

public partial class MainWindow
{
    private readonly CustomizationProfileService _customizationProfileService = new();

    private void SettingsExportProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        var defaultName = $"provix-customization-{DateTime.Now:yyyyMMdd-HHmm}.zip";
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = loc["UI_ExportCustomizationProfile"],
            Filter = "Provix profile|*.zip|All files|*.*",
            FileName = defaultName,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog() != true)
            return;

        var result = _customizationProfileService.ExportToZip(dialog.FileName);
        if (!result.Success)
        {
            MessageBox.Show(
                result.Message,
                loc["UI_ExportCustomizationProfile"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        MessageBox.Show(
            string.Format(loc["UI_ExportCustomizationProfileSuccess"], dialog.FileName),
            loc["UI_ExportCustomizationProfile"],
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void SettingsImportProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = loc["UI_ImportCustomizationProfile"],
            Filter = "Provix profile|*.zip|All files|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
            return;

        var result = _customizationProfileService.ImportFromZip(dialog.FileName);
        if (!result.Success)
        {
            MessageBox.Show(
                result.Message,
                loc["UI_ImportCustomizationProfile"],
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        PopulateSettingsControls();
        ApplyCustomFont();
        ApplyFileIconSettings();
        RefreshAllFileIcons();

        MessageBox.Show(
            string.Format(loc["UI_ImportCustomizationProfileSuccess"], result.Message),
            loc["UI_ImportCustomizationProfile"],
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void SettingsClearCacheButton_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        var confirm = MessageBox.Show(
            loc["UI_ClearCacheConfirm"],
            loc["UI_ClearCache"],
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
            return;

        var diskResult = AppCacheService.ClearDiskCache();
        _iconService.ClearCache();
        RefreshAllFileIcons();

        MessageBox.Show(
            string.Format(
                loc["UI_ClearCacheSuccess"],
                diskResult.FilesDeleted,
                FormatCacheSize(diskResult.BytesFreed)),
            loc["UI_ClearCache"],
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static string FormatCacheSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";

        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:0.#} KB";

        return $"{bytes / (1024.0 * 1024.0):0.#} MB";
    }
}
