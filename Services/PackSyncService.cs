using System.IO;
using System.Text.Json;
using FileExplorer.Models;

namespace FileExplorer.Services;

public sealed class PackSyncResult
{
    public int Updated { get; set; }

    public int Skipped { get; set; }

    public List<string> Messages { get; } = [];
}

public static class PackSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string UserIconPacksRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileExplorer",
            "IconPacks");

    public static string UserThemesRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileExplorer",
            "Themes");

    public static string AppIconPacksRoot =>
        Path.Combine(AppContext.BaseDirectory, "IconPacks");

    public static string AppThemesRoot =>
        Path.Combine(AppContext.BaseDirectory, "Themes", "Packs");

    public static PackSyncResult SyncBuiltInIconPacks()
    {
        var result = new PackSyncResult();
        Directory.CreateDirectory(UserIconPacksRoot);

        if (!Directory.Exists(AppIconPacksRoot))
            return result;

        foreach (var sourceDir in Directory.EnumerateDirectories(AppIconPacksRoot))
        {
            var packName = Path.GetFileName(sourceDir);
            var destinationDir = Path.Combine(UserIconPacksRoot, packName);
            var sourceManifest = ReadIconPackManifest(sourceDir);
            var destinationManifest = Directory.Exists(destinationDir)
                ? ReadIconPackManifest(destinationDir)
                : null;

            if (!ShouldUpdate(sourceManifest?.Version, destinationManifest?.Version))
            {
                result.Skipped++;
                continue;
            }

            CopyDirectory(sourceDir, destinationDir);
            result.Updated++;
            result.Messages.Add($"{packName} → v{sourceManifest?.Version ?? "1.0.0"}");
        }

        return result;
    }

    public static PackSyncResult SyncBuiltInThemes()
    {
        var result = new PackSyncResult();
        Directory.CreateDirectory(UserThemesRoot);

        if (!Directory.Exists(AppThemesRoot))
            return result;

        foreach (var sourceFile in Directory.EnumerateFiles(AppThemesRoot, "*.json"))
        {
            var fileName = Path.GetFileName(sourceFile);
            var destinationFile = Path.Combine(UserThemesRoot, fileName);
            var sourceManifest = ReadThemeManifest(sourceFile);
            var destinationManifest = File.Exists(destinationFile)
                ? ReadThemeManifest(destinationFile)
                : null;

            if (!ShouldUpdate(sourceManifest?.Version, destinationManifest?.Version))
            {
                result.Skipped++;
                continue;
            }

            Directory.CreateDirectory(UserThemesRoot);
            File.Copy(sourceFile, destinationFile, overwrite: true);
            result.Updated++;
            result.Messages.Add($"{sourceManifest?.Name ?? fileName} → v{sourceManifest?.Version ?? "1.0.0"}");
        }

        return result;
    }

    private static bool ShouldUpdate(string? sourceVersion, string? destinationVersion)
    {
        if (string.IsNullOrWhiteSpace(destinationVersion))
            return true;

        if (string.IsNullOrWhiteSpace(sourceVersion))
            return false;

        if (!Version.TryParse(sourceVersion, out var source))
            return !string.Equals(sourceVersion, destinationVersion, StringComparison.OrdinalIgnoreCase);

        if (!Version.TryParse(destinationVersion, out var destination))
            return true;

        return source > destination;
    }

    private static IconPackManifest? ReadIconPackManifest(string folderPath)
    {
        foreach (var fileName in new[] { "iconpack.json", "manifest.json", "pack.json" })
        {
            var path = Path.Combine(folderPath, fileName);
            if (!File.Exists(path))
                continue;

            try
            {
                return JsonSerializer.Deserialize<IconPackManifest>(File.ReadAllText(path), JsonOptions);
            }
            catch
            {
                return null;
            }
        }

        return null;
    }

    private static ThemeManifest? ReadThemeManifest(string filePath)
    {
        try
        {
            return JsonSerializer.Deserialize<ThemeManifest>(File.ReadAllText(filePath), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var destination = Path.Combine(destinationDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }
}
