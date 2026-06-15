using System.Windows;
using FileExplorer.Models;

namespace FileExplorer.Services;

public static class ThemeManager
{
    private static ResourceDictionary? _activeThemeDictionary;

    public static void ApplyTheme(AppTheme theme)
    {
        var source = theme == AppTheme.Light
            ? new Uri("Themes/LightTheme.xaml", UriKind.Relative)
            : new Uri("Themes/DarkTheme.xaml", UriKind.Relative);

        var newTheme = new ResourceDictionary { Source = source };
        var merged = Application.Current.Resources.MergedDictionaries;

        if (_activeThemeDictionary is not null)
            merged.Remove(_activeThemeDictionary);

        merged.Insert(0, newTheme);
        _activeThemeDictionary = newTheme;
    }
}
