using System.IO;
using FileExplorer.Models;

namespace FileExplorer.Services;

public static class BuiltInThemeCatalog
{
    public static IReadOnlyList<UiThemeComboItem> GetImportedThemeOptions()
    {
        var result = new List<UiThemeComboItem>();
        foreach (var path in GetThemeFiles())
        {
            var manifest = CustomThemeLoader.ReadManifest(path);
            var name = manifest?.Name;
            if (string.IsNullOrWhiteSpace(name))
                name = Path.GetFileNameWithoutExtension(path);

            var description = BuildDescription(manifest);
            result.Add(new UiThemeComboItem
            {
                Theme = AppTheme.Custom,
                JsonPath = path,
                Label = name ?? Path.GetFileNameWithoutExtension(path),
                Description = description
            });
        }

        return result
            .OrderBy(item => item.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string BuildDescription(ThemeManifest? manifest)
    {
        if (manifest is null)
            return string.Empty;

        var parts = new List<string> { $"v{manifest.Version}" };
        if (!string.IsNullOrWhiteSpace(manifest.Author))
            parts.Add(manifest.Author);
        if (!string.IsNullOrWhiteSpace(manifest.UpdatedAt))
            parts.Add(manifest.UpdatedAt);
        if (manifest.Colors.Count > 0)
            parts.Add($"{manifest.Colors.Count} colors");

        var description = string.Join(" · ", parts);
        if (!string.IsNullOrWhiteSpace(manifest.Description))
            description += Environment.NewLine + manifest.Description;

        return description;
    }

    public static IEnumerable<string> GetThemeFiles()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in new[] { PackSyncService.UserThemesRoot, PackSyncService.AppThemesRoot })
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var file in Directory.EnumerateFiles(root, "*.json"))
            {
                var fullPath = Path.GetFullPath(file);
                if (seen.Add(fullPath))
                    yield return fullPath;
            }
        }
    }
}
