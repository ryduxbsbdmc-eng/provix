using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace FileExplorer.Services;

public static class FontManager
{
    private static readonly FontFamily DefaultUIFont = new("Segoe UI");

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".ttf", ".otf", ".ttc"
    };

    public static string FontsDirectory
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FileExplorer",
                "Fonts");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    public static FontFamily CurrentUIFont { get; private set; } = DefaultUIFont;

    public static bool TryImportFont(string sourcePath, out string storedPath)
    {
        storedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            return false;

        var extension = Path.GetExtension(sourcePath);
        if (!SupportedExtensions.Contains(extension))
            return false;

        try
        {
            var fileName = SanitizeFileName(Path.GetFileName(sourcePath));
            storedPath = Path.Combine(FontsDirectory, fileName);
            File.Copy(sourcePath, storedPath, overwrite: true);
            return TryLoadFont(storedPath, out _);
        }
        catch
        {
            storedPath = string.Empty;
            return false;
        }
    }

    public static bool TryLoadFont(string? fontPath, out FontFamily fontFamily)
    {
        fontFamily = DefaultUIFont;

        if (string.IsNullOrWhiteSpace(fontPath) || !File.Exists(fontPath))
            return false;

        try
        {
            var absolutePath = Path.GetFullPath(fontPath);
            var uri = new Uri(absolutePath, UriKind.Absolute);
            var glyph = new GlyphTypeface(uri);
            var familyName = GetPreferredFamilyName(glyph);
            fontFamily = new FontFamily(uri, $"./#{familyName}");
            return true;
        }
        catch
        {
            fontFamily = DefaultUIFont;
            return false;
        }
    }

    public static void Apply(Window window, string? fontId, string? customFontPath)
    {
        CurrentUIFont = BuiltInFontCatalog.Resolve(fontId, customFontPath);
        window.FontFamily = CurrentUIFont;
    }

    public static string GetPreferredFamilyName(GlyphTypeface glyph)
    {
        if (glyph.Win32FamilyNames.TryGetValue(CultureInfo.CurrentUICulture, out var localizedName) &&
            !string.IsNullOrWhiteSpace(localizedName))
        {
            return localizedName;
        }

        if (glyph.Win32FamilyNames.TryGetValue(CultureInfo.GetCultureInfo("en-us"), out var englishName) &&
            !string.IsNullOrWhiteSpace(englishName))
        {
            return englishName;
        }

        return glyph.Win32FamilyNames.Values.First();
    }

    private static string SanitizeFileName(string value)
    {
        var name = Path.GetFileName(value);
        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        return string.IsNullOrWhiteSpace(name) ? "custom-font.ttf" : name;
    }
}
