using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using FileExplorer.Models;

namespace FileExplorer.Services;

public sealed class CustomizationProfileResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    public static CustomizationProfileResult Ok(string message) =>
        new() { Success = true, Message = message };

    public static CustomizationProfileResult Fail(string message) =>
        new() { Success = false, Message = message };
}

public sealed class CustomizationProfileService
{
    private const string ManifestFileName = "profile.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public CustomizationProfileResult ExportToZip(string destinationZipPath)
    {
        var settings = SettingsManager.Instance.Current;
        var tempDir = Path.Combine(Path.GetTempPath(), $"provix-profile-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempDir);

            var manifest = new CustomizationProfileManifest
            {
                Version = 1,
                Theme = settings.Theme.ToString(),
                IconStyle = settings.FileIconStyle.ToString(),
                UiFontId = settings.UiFontId,
                ExportedAt = DateTime.UtcNow.ToString("O"),
                AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? string.Empty
            };

            if (settings.Theme == AppTheme.Custom &&
                !string.IsNullOrWhiteSpace(settings.CustomThemePath) &&
                File.Exists(settings.CustomThemePath))
            {
                manifest.ThemeFile = "theme.json";
                File.Copy(settings.CustomThemePath, Path.Combine(tempDir, manifest.ThemeFile), overwrite: true);
            }

            if (settings.FileIconStyle == FileIconStyle.Custom &&
                !string.IsNullOrWhiteSpace(settings.CustomIconPackPath) &&
                Directory.Exists(settings.CustomIconPackPath))
            {
                manifest.IconPackFolder = "iconpack";
                CopyDirectory(settings.CustomIconPackPath, Path.Combine(tempDir, manifest.IconPackFolder));
            }

            if (!string.IsNullOrWhiteSpace(settings.CustomFontPath) && File.Exists(settings.CustomFontPath))
            {
                var extension = Path.GetExtension(settings.CustomFontPath);
                if (string.IsNullOrWhiteSpace(extension))
                    extension = ".ttf";

                manifest.FontFile = "font" + extension;
                File.Copy(settings.CustomFontPath, Path.Combine(tempDir, manifest.FontFile), overwrite: true);
            }

            var manifestPath = Path.Combine(tempDir, ManifestFileName);
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));

            var destinationDirectory = Path.GetDirectoryName(destinationZipPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            if (File.Exists(destinationZipPath))
                File.Delete(destinationZipPath);

            ZipFile.CreateFromDirectory(tempDir, destinationZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            return CustomizationProfileResult.Ok(destinationZipPath);
        }
        catch (Exception ex)
        {
            return CustomizationProfileResult.Fail(ex.Message);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Best effort cleanup.
                }
            }
        }
    }

    public CustomizationProfileResult ImportFromZip(string zipPath)
    {
        if (!File.Exists(zipPath))
            return CustomizationProfileResult.Fail("Profile file not found.");

        AppCacheService.EnsureDirectories();

        var profileName = Path.GetFileNameWithoutExtension(zipPath);
        var extractRoot = Path.Combine(
            AppCacheService.ImportedProfilesRoot,
            $"{SanitizeFileName(profileName)}-{DateTime.Now:yyyyMMdd-HHmmss}");

        try
        {
            Directory.CreateDirectory(extractRoot);
            ZipFile.ExtractToDirectory(zipPath, extractRoot, overwriteFiles: true);

            var manifestPath = Path.Combine(extractRoot, ManifestFileName);
            if (!File.Exists(manifestPath))
                return CustomizationProfileResult.Fail("Invalid profile: profile.json is missing.");

            var manifest = JsonSerializer.Deserialize<CustomizationProfileManifest>(
                File.ReadAllText(manifestPath),
                JsonOptions);

            if (manifest is null)
                return CustomizationProfileResult.Fail("Invalid profile: profile.json is empty.");

            ApplyManifest(manifest, extractRoot, profileName);
            SettingsManager.Instance.Save();
            return CustomizationProfileResult.Ok(profileName);
        }
        catch (Exception ex)
        {
            return CustomizationProfileResult.Fail(ex.Message);
        }
    }

    private static void ApplyManifest(CustomizationProfileManifest manifest, string extractRoot, string profileName)
    {
        var theme = ParseTheme(manifest.Theme);
        var themePath = string.Empty;

        if (!string.IsNullOrWhiteSpace(manifest.ThemeFile))
        {
            var sourceTheme = Path.Combine(extractRoot, manifest.ThemeFile.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(sourceTheme))
            {
                Directory.CreateDirectory(PackSyncService.UserThemesRoot);
                var themeFileName = $"{SanitizeFileName(profileName)}.json";
                themePath = Path.Combine(PackSyncService.UserThemesRoot, themeFileName);
                File.Copy(sourceTheme, themePath, overwrite: true);
                theme = AppTheme.Custom;
            }
        }

        SettingsManager.Instance.ApplyThemeSelection(theme, themePath);

        var iconStyle = ParseIconStyle(manifest.IconStyle);
        var iconPackPath = string.Empty;

        if (!string.IsNullOrWhiteSpace(manifest.IconPackFolder))
        {
            var sourcePack = Path.Combine(extractRoot, manifest.IconPackFolder.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(sourcePack))
            {
                var destinationPack = Path.Combine(
                    PackSyncService.UserIconPacksRoot,
                    SanitizeFileName(profileName));

                if (Directory.Exists(destinationPack))
                    Directory.Delete(destinationPack, recursive: true);

                CopyDirectory(sourcePack, destinationPack);
                iconPackPath = destinationPack;
                iconStyle = FileIconStyle.Custom;
            }
        }

        SettingsManager.Instance.UpdateCustomIconPackPath(iconPackPath);
        SettingsManager.Instance.UpdateFileIconStyle(iconStyle);

        var fontId = BuiltInFontCatalog.NormalizeFontId(manifest.UiFontId, string.Empty);
        var fontPath = string.Empty;

        if (!string.IsNullOrWhiteSpace(manifest.FontFile))
        {
            var sourceFont = Path.Combine(extractRoot, manifest.FontFile.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(sourceFont))
            {
                Directory.CreateDirectory(AppCacheService.ImportedFontsRoot);
                var fontFileName = $"{SanitizeFileName(profileName)}{Path.GetExtension(sourceFont)}";
                fontPath = Path.Combine(AppCacheService.ImportedFontsRoot, fontFileName);
                File.Copy(sourceFont, fontPath, overwrite: true);
                fontId = UiFontIds.Custom;
            }
        }

        SettingsManager.Instance.UpdateCustomFontPath(fontPath);
        SettingsManager.Instance.UpdateUiFontId(fontId);
    }

    private static AppTheme ParseTheme(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return AppTheme.Dark;

        return Enum.TryParse<AppTheme>(value, ignoreCase: true, out var theme)
            ? ThemeCatalog.Normalize(theme)
            : AppTheme.Dark;
    }

    private static FileIconStyle ParseIconStyle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return FileIconStyle.Windows;

        return Enum.TryParse<FileIconStyle>(value, ignoreCase: true, out var style)
            ? style
            : FileIconStyle.Windows;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim('.', ' ');
        return string.IsNullOrWhiteSpace(sanitized) ? "profile" : sanitized;
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
