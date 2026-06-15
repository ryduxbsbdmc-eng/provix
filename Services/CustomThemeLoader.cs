using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using FileExplorer.Models;

namespace FileExplorer.Services;

public static class CustomThemeLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static ResourceDictionary? Load(string themePath)
    {
        if (string.IsNullOrWhiteSpace(themePath) || !File.Exists(themePath))
            return null;

        try
        {
            var json = File.ReadAllText(themePath);
            var manifest = JsonSerializer.Deserialize<ThemeManifest>(json, JsonOptions);
            if (manifest is null)
                return null;

            return BuildDictionary(manifest);
        }
        catch
        {
            return null;
        }
    }

    public static ThemeManifest? ReadManifest(string themePath)
    {
        if (string.IsNullOrWhiteSpace(themePath) || !File.Exists(themePath))
            return null;

        try
        {
            var json = File.ReadAllText(themePath);
            return JsonSerializer.Deserialize<ThemeManifest>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void ExportCurrent(string destinationPath, string name)
    {
        var manifest = CaptureCurrent(name);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        File.WriteAllText(destinationPath, json);
    }

    public static ThemeManifest CaptureCurrent(string name)
    {
        var manifest = new ThemeManifest
        {
            Name = name,
            Version = "1.0.0",
            Author = "Provix",
            UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Description = "Exported from Provix"
        };

        foreach (var pair in ThemeColorDefaults.Dark)
        {
            var value = TryReadResource(pair.Key);
            manifest.Colors[pair.Key] = value ?? pair.Value;
        }

        return manifest;
    }

    private static ResourceDictionary BuildDictionary(ThemeManifest manifest)
    {
        var dictionary = new ResourceDictionary();
        var merged = new Dictionary<string, string>(ThemeColorDefaults.Dark, StringComparer.OrdinalIgnoreCase);

        foreach (var pair in manifest.Colors)
        {
            if (!string.IsNullOrWhiteSpace(pair.Value))
                merged[pair.Key] = pair.Value.Trim();
        }

        ApplySystemSelectionOverrides(dictionary, merged);

        foreach (var pair in merged)
        {
            if (pair.Key.EndsWith("Color", StringComparison.Ordinal))
            {
                dictionary[pair.Key] = ParseColor(pair.Value);
                continue;
            }

            dictionary[pair.Key] = new SolidColorBrush(ParseColor(pair.Value));
        }

        return dictionary;
    }

    private static void ApplySystemSelectionOverrides(ResourceDictionary dictionary, IReadOnlyDictionary<string, string> colors)
    {
        dictionary[SystemColors.HighlightBrushKey] = new SolidColorBrush(ParseColor(colors["SelectionFillBrush"]));
        dictionary[SystemColors.HighlightTextBrushKey] = new SolidColorBrush(ParseColor(colors["SelectionForegroundBrush"]));
        dictionary[SystemColors.InactiveSelectionHighlightBrushKey] = new SolidColorBrush(ParseColor(colors["SelectionFillInactiveBrush"]));
        dictionary[SystemColors.InactiveSelectionHighlightTextBrushKey] = new SolidColorBrush(ParseColor(colors["SelectionForegroundBrush"]));
        dictionary[SystemColors.ControlBrushKey] = Brushes.Transparent;
        dictionary[SystemColors.ControlTextBrushKey] = new SolidColorBrush(ParseColor(colors["TextPrimaryBrush"]));
    }

    private static string? TryReadResource(string key)
    {
        if (Application.Current?.Resources[key] is SolidColorBrush brush)
            return brush.Color.ToString();

        if (Application.Current?.Resources[key] is Color color)
            return color.ToString();

        return null;
    }

    private static Color ParseColor(string value)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value)!;
        }
        catch
        {
            return Colors.Transparent;
        }
    }
}
