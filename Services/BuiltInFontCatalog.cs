using System.Windows.Media;
using FileExplorer.Models;

namespace FileExplorer.Services;

public static class BuiltInFontCatalog
{
    private static readonly FontFamily DefaultUIFont = new("Segoe UI");

    private static readonly IReadOnlyList<UiFontOption> Options =
    [
        new UiFontOption { Id = UiFontIds.Default, LabelKey = "UI_FontDefault" },
        new UiFontOption { Id = UiFontIds.Cascadia, LabelKey = "UI_FontCascadia" },
        new UiFontOption { Id = UiFontIds.Calibri, LabelKey = "UI_FontCalibri" },
        new UiFontOption { Id = UiFontIds.Arial, LabelKey = "UI_FontArial" },
        new UiFontOption { Id = UiFontIds.Inter, LabelKey = "UI_FontInter" },
        new UiFontOption { Id = UiFontIds.SourceSans3, LabelKey = "UI_FontSourceSans" },
        new UiFontOption { Id = UiFontIds.JetBrainsMono, LabelKey = "UI_FontJetBrainsMono" },
        new UiFontOption { Id = UiFontIds.Custom, LabelKey = "UI_FontCustom", IsCustom = true }
    ];

    public static IReadOnlyList<UiFontOption> GetOptions() => Options;

    public static FontFamily Resolve(string? fontId, string? customFontPath)
    {
        var normalizedId = NormalizeFontId(fontId, customFontPath);

        return normalizedId switch
        {
            UiFontIds.Cascadia => TrySystemFont("Cascadia UI", "Segoe UI Variable", "Segoe UI"),
            UiFontIds.Calibri => TrySystemFont("Calibri", "Segoe UI"),
            UiFontIds.Arial => TrySystemFont("Arial", "Segoe UI"),
            UiFontIds.Inter => TryEmbeddedFont("Fonts/Inter-Regular.ttf") ?? DefaultUIFont,
            UiFontIds.SourceSans3 => TryEmbeddedFont("Fonts/SourceSans3-Regular.ttf") ?? DefaultUIFont,
            UiFontIds.JetBrainsMono => TryEmbeddedFont("Fonts/JetBrainsMono-Regular.ttf") ?? DefaultUIFont,
            UiFontIds.Custom => FontManager.TryLoadFont(customFontPath, out var customFont)
                ? customFont
                : DefaultUIFont,
            _ => DefaultUIFont
        };
    }

    public static string NormalizeFontId(string? fontId, string? customFontPath)
    {
        if (!string.IsNullOrWhiteSpace(fontId))
            return fontId.Trim().ToLowerInvariant();

        return string.IsNullOrWhiteSpace(customFontPath)
            ? UiFontIds.Default
            : UiFontIds.Custom;
    }

    private static FontFamily TrySystemFont(params string[] familyNames)
    {
        foreach (var familyName in familyNames)
        {
            if (IsFontAvailable(familyName))
                return new FontFamily(familyName);
        }

        return DefaultUIFont;
    }

    private static bool IsFontAvailable(string familyName)
    {
        try
        {
            return Fonts.SystemFontFamilies.Any(font => font.Source.Equals(familyName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static FontFamily? TryEmbeddedFont(string resourcePath)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/{resourcePath}", UriKind.Absolute);
            var glyph = new GlyphTypeface(uri);
            var familyName = FontManager.GetPreferredFamilyName(glyph);
            return new FontFamily(uri, $"./#{familyName}");
        }
        catch
        {
            return null;
        }
    }
}
