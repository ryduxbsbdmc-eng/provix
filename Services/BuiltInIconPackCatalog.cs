using System.IO;
using System.Text.Json;
using FileExplorer.Models;

namespace FileExplorer.Services;

public static class BuiltInIconPackCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyList<UiIconPackOption> GetStyleOptions(LocalizationManager loc) =>
    [
        new UiIconPackOption
        {
            Style = FileIconStyle.Windows,
            Label = loc["UI_IconStyleWindows"],
            Description = loc["UI_IconStyleWindowsHint"]
        },
        new UiIconPackOption
        {
            Style = FileIconStyle.Flat,
            Label = loc["UI_IconStyleFlat"],
            Description = loc["UI_IconStyleFlatHint"]
        },
        new UiIconPackOption
        {
            Style = FileIconStyle.Minimal,
            Label = loc["UI_IconStyleMinimal"],
            Description = loc["UI_IconStyleMinimalHint"]
        }
    ];

    public static IReadOnlyList<UiIconPackOption> GetBuiltInPackOptions()
    {
        var result = new List<UiIconPackOption>();
        foreach (var info in ScanPackFolders())
        {
            result.Add(new UiIconPackOption
            {
                Style = FileIconStyle.Custom,
                PackFolderPath = info.FolderPath,
                RequiresManualPath = false,
                Label = info.Name,
                Description = BuildPackDescription(info)
            });
        }

        return result;
    }

    public static IReadOnlyList<IconPackInfo> ScanPackFolders()
    {
        var result = new List<IconPackInfo>();
        foreach (var root in GetSearchRoots())
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                var fullPath = Path.GetFullPath(folder);
                if (result.Any(item => item.FolderPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var info = InspectPackFolder(fullPath, root.Equals(PackSyncService.AppIconPacksRoot, StringComparison.OrdinalIgnoreCase));
                if (info is not null)
                    result.Add(info);
            }
        }

        return result
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IconPackInfo? InspectFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
            return null;

        var isBuiltIn = folderPath.StartsWith(PackSyncService.UserIconPacksRoot, StringComparison.OrdinalIgnoreCase) ||
                        folderPath.StartsWith(PackSyncService.AppIconPacksRoot, StringComparison.OrdinalIgnoreCase);
        return InspectPackFolder(Path.GetFullPath(folderPath), isBuiltIn);
    }

    private static IconPackInfo? InspectPackFolder(string folderPath, bool isBuiltIn)
    {
        var manifest = ReadManifest(folderPath);
        var images = Directory.Exists(folderPath)
            ? Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                .Count(path => IsImage(path))
            : 0;

        var extensionCount = manifest?.Extensions.Count ?? 0;
        if (extensionCount == 0 && Directory.Exists(folderPath))
        {
            extensionCount = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Count(name => !string.IsNullOrWhiteSpace(name) &&
                               name is not ("folder" or "file" or "drive" or "iconpack" or "manifest" or "pack"));
        }

        var name = manifest?.Name;
        if (string.IsNullOrWhiteSpace(name))
            name = Path.GetFileName(folderPath);

        return new IconPackInfo
        {
            FolderPath = folderPath,
            Name = name ?? "Icon pack",
            Version = manifest?.Version ?? "1.0.0",
            Description = manifest?.Description ?? string.Empty,
            Author = manifest?.Author ?? string.Empty,
            UpdatedAt = manifest?.UpdatedAt ?? string.Empty,
            ExtensionCount = extensionCount,
            ImageCount = images,
            HasFolderIcon = HasNamedIcon(folderPath, manifest?.Folder, "folder"),
            HasFileIcon = HasNamedIcon(folderPath, manifest?.File, "file"),
            HasDriveIcon = HasNamedIcon(folderPath, manifest?.Drive, "drive"),
            IsBuiltIn = isBuiltIn
        };
    }

    public static string BuildPackDescription(IconPackInfo info)
    {
        var parts = new List<string>
        {
            $"v{info.Version}",
            $"{info.ExtensionCount} ext",
            $"{info.ImageCount} img"
        };

        if (!string.IsNullOrWhiteSpace(info.Author))
            parts.Add(info.Author);

        if (!string.IsNullOrWhiteSpace(info.UpdatedAt))
            parts.Add(info.UpdatedAt);

        var specials = new List<string>();
        if (info.HasFolderIcon) specials.Add("folder");
        if (info.HasFileIcon) specials.Add("file");
        if (info.HasDriveIcon) specials.Add("drive");
        if (specials.Count > 0)
            parts.Add(string.Join("/", specials));

        var description = string.Join(" · ", parts);
        if (!string.IsNullOrWhiteSpace(info.Description))
            description += Environment.NewLine + info.Description;

        return description;
    }

    private static IEnumerable<string> GetSearchRoots()
    {
        yield return PackSyncService.UserIconPacksRoot;
        yield return PackSyncService.AppIconPacksRoot;
    }

    private static IconPackManifest? ReadManifest(string folderPath)
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

    private static bool HasNamedIcon(string folderPath, string? manifestFile, string fallbackStem)
    {
        if (!string.IsNullOrWhiteSpace(manifestFile) &&
            File.Exists(Path.Combine(folderPath, manifestFile)))
            return true;

        return Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
            .Any(path => Path.GetFileNameWithoutExtension(path)
                .Equals(fallbackStem, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsImage(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ico", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }
}
