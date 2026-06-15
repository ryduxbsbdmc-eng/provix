using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FileExplorer.Helpers;
using FileExplorer.Models;
using FileExplorer.Services;
using Microsoft.Win32;

namespace FileExplorer;

public partial class MainWindow
{
    private readonly IconPackEditorService _iconPackEditor = new();

    private void SettingsEditIconPackButton_Click(object sender, RoutedEventArgs e)
    {
        ShowIconPackEditorOverlay();
    }

    private void ShowIconPackEditorOverlay()
    {
        var loc = LocalizationManager.Instance;
        ApplyIconPackEditorLocalizedStrings(loc);

        var currentPath = SettingsManager.Instance.Current.CustomIconPackPath;
        if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
            _iconPackEditor.Load(currentPath);
        else
            _iconPackEditor.CreateNew(loc["UI_IconPackDefaultName"]);

        BindIconPackEditorFields();
        HideIconPackEditorError();

        IconPackEditorOverlay.Visibility = Visibility.Visible;
        PushChromeDimOverlay();
        UiAnimationHelper.ShowOverlay(IconPackEditorPanel);

        Dispatcher.BeginInvoke(() =>
        {
            IconPackNameTextBox.Focus();
            IconPackNameTextBox.CaretIndex = IconPackNameTextBox.Text.Length;
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void BindIconPackEditorFields()
    {
        IconPackNameTextBox.Text = _iconPackEditor.PackName;
        IconPackFolderTextBox.Text = _iconPackEditor.PackFolder;
        IconPackNewExtensionTextBox.Text = string.Empty;
        IconPackMappingsList.ItemsSource = _iconPackEditor.ExtensionMappings;
        RefreshIconPackSpecialPreviews();
    }

    private void RefreshIconPackSpecialPreviews()
    {
        IconPackFolderPreview.Source = _iconPackEditor.GetPreview(_iconPackEditor.FolderIconFile);
        IconPackFilePreview.Source = _iconPackEditor.GetPreview(_iconPackEditor.FileIconFile);
        IconPackDrivePreview.Source = _iconPackEditor.GetPreview(_iconPackEditor.DriveIconFile);
    }

    private void ApplyIconPackEditorLocalizedStrings(LocalizationManager loc)
    {
        IconPackEditorTitleText.Text = loc["UI_IconPackEditorTitle"];
        IconPackNameLabel.Text = loc["UI_IconPackName"];
        IconPackFolderLabel.Text = loc["UI_IconPackFolder"];
        IconPackNewButton.Content = loc["UI_IconPackNew"];
        IconPackOpenButton.Content = loc["UI_IconPackOpen"];
        IconPackSystemIconsLabel.Text = loc["UI_IconPackSystemIcons"];
        IconPackFolderIconLabel.Text = loc["UI_IconPackFolderIcon"];
        IconPackFileIconLabel.Text = loc["UI_IconPackFileIcon"];
        IconPackDriveIconLabel.Text = loc["UI_IconPackDriveIcon"];
        IconPackChooseFolderIconButton.Content = loc["UI_IconPackChooseImage"];
        IconPackChooseFileIconButton.Content = loc["UI_IconPackChooseImage"];
        IconPackChooseDriveIconButton.Content = loc["UI_IconPackChooseImage"];
        IconPackExtensionsLabel.Text = loc["UI_IconPackExtensions"];
        IconPackExtensionColumn.Header = loc["UI_IconPackExtension"];
        IconPackImageColumn.Header = loc["UI_IconPackImageFile"];
        IconPackAddExtensionButton.Content = loc["UI_IconPackAddExtension"];
        IconPackRemoveExtensionButton.Content = loc["UI_IconPackRemoveExtension"];
        IconPackSaveButton.Content = loc["UI_IconPackSave"];
        IconPackApplyButton.Content = loc["UI_IconPackApply"];
        IconPackCancelButton.Content = loc["UI_IconPackCancel"];
        SettingsEditIconPackButton.Content = loc["UI_EditIconPack"];
    }

    private void HideIconPackEditorOverlay()
    {
        if (IconPackEditorOverlay.Visibility != Visibility.Visible)
            return;

        void FinishHide()
        {
            IconPackEditorOverlay.Visibility = Visibility.Collapsed;
            IconPackEditorPanel.Visibility = Visibility.Visible;
            HideIconPackEditorError();
            PopChromeDimOverlay();
        }

        if (IconPackEditorPanel.Visibility != Visibility.Visible)
        {
            FinishHide();
            return;
        }

        UiAnimationHelper.HideOverlay(IconPackEditorPanel, FinishHide);
    }

    private void ShowIconPackEditorError(string message)
    {
        IconPackEditorErrorText.Text = message;
        IconPackEditorErrorText.Visibility = Visibility.Visible;
    }

    private void HideIconPackEditorError()
    {
        IconPackEditorErrorText.Text = string.Empty;
        IconPackEditorErrorText.Visibility = Visibility.Collapsed;
    }

    private void IconPackEditorOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source &&
            !IsDescendantOf(source, IconPackEditorPanel))
        {
            HideIconPackEditorOverlay();
            e.Handled = true;
        }
    }

    private void IconPackEditorPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void IconPackCancelButton_Click(object sender, RoutedEventArgs e)
    {
        HideIconPackEditorOverlay();
    }

    private void IconPackNameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _iconPackEditor.PackName = IconPackNameTextBox.Text;
    }

    private void IconPackNewButton_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        var folder = _iconPackEditor.CreateNew(IconPackNameTextBox.Text.Trim());
        _iconPackEditor.PackName = string.IsNullOrWhiteSpace(IconPackNameTextBox.Text)
            ? loc["UI_IconPackDefaultName"]
            : IconPackNameTextBox.Text.Trim();
        BindIconPackEditorFields();
        HideIconPackEditorError();
    }

    private void IconPackOpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationManager.Instance["UI_IconPackOpen"]
        };

        if (!string.IsNullOrWhiteSpace(_iconPackEditor.PackFolder) && Directory.Exists(_iconPackEditor.PackFolder))
            dialog.InitialDirectory = _iconPackEditor.PackFolder;

        if (dialog.ShowDialog() != true)
            return;

        if (!_iconPackEditor.Load(dialog.FolderName))
        {
            ShowIconPackEditorError(LocalizationManager.Instance["UI_IconPackOpenFailed"]);
            return;
        }

        BindIconPackEditorFields();
        HideIconPackEditorError();
    }

    private void IconPackChooseFolderIconButton_Click(object sender, RoutedEventArgs e)
    {
        ChooseSpecialIcon(IconPackSpecialKind.Folder);
    }

    private void IconPackChooseFileIconButton_Click(object sender, RoutedEventArgs e)
    {
        ChooseSpecialIcon(IconPackSpecialKind.File);
    }

    private void IconPackChooseDriveIconButton_Click(object sender, RoutedEventArgs e)
    {
        ChooseSpecialIcon(IconPackSpecialKind.Drive);
    }

    private void ChooseSpecialIcon(IconPackSpecialKind kind)
    {
        var imagePath = PickImageFile();
        if (imagePath is null)
            return;

        try
        {
            _iconPackEditor.ImportSpecialIcon(kind, imagePath);
            IconPackFolderTextBox.Text = _iconPackEditor.PackFolder;
            RefreshIconPackSpecialPreviews();
            HideIconPackEditorError();
        }
        catch (Exception ex)
        {
            ShowIconPackEditorError(ex.Message);
        }
    }

    private void IconPackAddExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocalizationManager.Instance;
        var extension = IconPackNewExtensionTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(extension))
        {
            ShowIconPackEditorError(loc["UI_IconPackInvalidExtension"]);
            return;
        }

        var imagePath = PickImageFile();
        if (imagePath is null)
            return;

        try
        {
            _iconPackEditor.AddExtensionFromImage(extension, imagePath);
            IconPackFolderTextBox.Text = _iconPackEditor.PackFolder;
            IconPackNewExtensionTextBox.Text = string.Empty;
            HideIconPackEditorError();
        }
        catch (Exception ex)
        {
            ShowIconPackEditorError(ex.Message);
        }
    }

    private void IconPackRemoveExtensionButton_Click(object sender, RoutedEventArgs e)
    {
        if (IconPackMappingsList.SelectedItem is not IconPackMappingEntry entry)
            return;

        _iconPackEditor.RemoveExtension(entry.Extension);
    }

    private void IconPackSaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySaveIconPack(showError: true))
            return;

        HideIconPackEditorError();
    }

    private void IconPackApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TrySaveIconPack(showError: true))
            return;

        var packFolder = _iconPackEditor.PackFolder;
        SettingsCustomIconPackPathBox.Text = packFolder;

        _suppressSettingsUiChange = true;
        try
        {
            SelectIconPackComboBoxItem(FileIconStyle.Custom, packFolder);
            UpdateCustomIconPackPanelVisibility();
            UpdateIconPackDetailsText();
        }
        finally
        {
            _suppressSettingsUiChange = false;
        }

        SettingsManager.Instance.UpdateCustomIconPackPath(packFolder);
        SettingsManager.Instance.UpdateFileIconStyle(FileIconStyle.Custom);
        HideIconPackEditorOverlay();
    }

    private bool TrySaveIconPack(bool showError)
    {
        var loc = LocalizationManager.Instance;

        if (!_iconPackEditor.HasPackFolder)
            _iconPackEditor.CreateNew(IconPackNameTextBox.Text.Trim());

        _iconPackEditor.PackName = IconPackNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(_iconPackEditor.PackName))
            _iconPackEditor.PackName = loc["UI_IconPackDefaultName"];

        try
        {
            _iconPackEditor.Save();
            IconPackFolderTextBox.Text = _iconPackEditor.PackFolder;
            return true;
        }
        catch (Exception ex)
        {
            if (showError)
                ShowIconPackEditorError(string.IsNullOrWhiteSpace(ex.Message)
                    ? loc["UI_IconPackSaveFailed"]
                    : ex.Message);
            return false;
        }
    }

    private static string? PickImageFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationManager.Instance["UI_IconPackChooseImage"],
            Filter = "Images|*.png;*.ico;*.jpg;*.jpeg;*.webp|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
