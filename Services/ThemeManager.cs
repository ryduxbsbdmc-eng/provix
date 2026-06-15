using System.Windows;
using FileExplorer.Models;

namespace FileExplorer.Services;

public static class ThemeManager
{
    private static ResourceDictionary? _activeThemeDictionary;

    public static void ApplyTheme(AppTheme theme, string? customThemePath = null)
    {
        theme = ThemeCatalog.Normalize(theme);

        if (theme == AppTheme.Custom && !string.IsNullOrWhiteSpace(customThemePath))
        {
            var customDictionary = CustomThemeLoader.Load(customThemePath);
            if (customDictionary is not null)
            {
                ReplaceActiveDictionary(customDictionary);
                return;
            }
        }

        if (theme == AppTheme.Custom)
            theme = AppTheme.Dark;

        var source = new Uri(ThemeCatalog.GetResourcePath(theme), UriKind.Relative);
        ReplaceActiveDictionary(new ResourceDictionary { Source = source });
    }

    private static void ReplaceActiveDictionary(ResourceDictionary newTheme)
    {
        var merged = Application.Current.Resources.MergedDictionaries;

        if (_activeThemeDictionary is not null)
            merged.Remove(_activeThemeDictionary);

        merged.Insert(0, newTheme);
        _activeThemeDictionary = newTheme;
    }
}
