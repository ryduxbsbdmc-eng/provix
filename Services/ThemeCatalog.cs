using FileExplorer.Models;

namespace FileExplorer.Services;

public static class ThemeCatalog
{
    private static readonly IReadOnlyList<AppTheme> BuiltInThemes =
    [
        AppTheme.Dark,
        AppTheme.Light,
        AppTheme.Explorer,
        AppTheme.Midnight,
        AppTheme.Nord,
        AppTheme.Forest,
        AppTheme.Rose,
        AppTheme.Amoled,
        AppTheme.Custom
    ];

    public static IReadOnlyList<AppTheme> GetBuiltInThemes() => BuiltInThemes;

    public static string GetLabelKey(AppTheme theme) => theme switch
    {
        AppTheme.Light => "UI_ThemeLight",
        AppTheme.Explorer => "UI_ThemeExplorer",
        AppTheme.Midnight => "UI_ThemeMidnight",
        AppTheme.Nord => "UI_ThemeNord",
        AppTheme.Forest => "UI_ThemeForest",
        AppTheme.Rose => "UI_ThemeRose",
        AppTheme.Amoled => "UI_ThemeAmoled",
        AppTheme.Custom => "UI_ThemeCustom",
        _ => "UI_ThemeDark"
    };

    public static string GetResourcePath(AppTheme theme) => theme switch
    {
        AppTheme.Light => "Themes/LightTheme.xaml",
        AppTheme.Explorer => "Themes/ExplorerPaletteTheme.xaml",
        AppTheme.Midnight => "Themes/MidnightTheme.xaml",
        AppTheme.Nord => "Themes/NordTheme.xaml",
        AppTheme.Forest => "Themes/ForestTheme.xaml",
        AppTheme.Rose => "Themes/RoseTheme.xaml",
        AppTheme.Amoled => "Themes/AmoledTheme.xaml",
        _ => "Themes/DarkTheme.xaml"
    };

    public static AppTheme Normalize(AppTheme theme) =>
        BuiltInThemes.Contains(theme) ? theme : AppTheme.Dark;
}
