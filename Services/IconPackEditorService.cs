using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileExplorer.Models;

namespace FileExplorer.Services;

public sealed class IconPackEditorService
{
    private const int PreviewSize = 32;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "folder", "file", "drive", "iconpack", "manifest", "pack"
    };

    public ObservableCollection<IconPackMappingEntry> ExtensionMappings { get; } = [];

    public string PackName { get; set; } = "My Icon Pack";

    public string PackFolder { get; private set; } = string.Empty;

    public string? FolderIconFile { get; private set; }

    public string? FileIconFile { get; private set; }

    public string? DriveIconFile { get; private set; }

    public static string DefaultPacksRoot
    {
        get
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FileExplorer",
                "IconPacks");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public bool HasPackFolder => !string.IsNullOrWhiteSpace(PackFolder) && Directory.Exists(PackFolder);

    public string CreateNew(string? suggestedName = null)
    {
        var baseName = SanitizeFolderName(string.IsNullOrWhiteSpace(suggestedName) ? PackName : suggestedName!);
        if (string.IsNullOrEmpty(baseName))
            baseName = "IconPack";

        var folder = GetUniquePackFolder(baseName);
        Directory.CreateDirectory(folder);
        PackFolder = folder;
        ExtensionMappings.Clear();
        FolderIconFile = null;
        FileIconFile = null;
        DriveIconFile = null;
        return folder;
    }

    public bool Load(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return false;

        PackFolder = Path.GetFullPath(folderPath);
        ExtensionMappings.Clear();
        FolderIconFile = null;
        FileIconFile = null;
        DriveIconFile = null;
        PackName = Path.GetFileName(PackFolder);

        var manifestPath = FindManifestPath(PackFolder);
        if (manifestPath is not null)
        {
            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<IconPackManifest>(json, JsonOptions);
                if (manifest is not null)
                {
                    if (!string.IsNullOrWhiteSpace(manifest.Name))
                        PackName = manifest.Name.Trim();

                    FolderIconFile = NormalizeExistingFile(manifest.Folder);
                    FileIconFile = NormalizeExistingFile(manifest.File);
                    DriveIconFile = NormalizeExistingFile(manifest.Drive);

                    foreach (var pair in manifest.Extensions)
                    {
                        var extension = NormalizeExtension(pair.Key);
                        if (string.IsNullOrEmpty(extension))
                            continue;

                        var fileName = NormalizeExistingFile(pair.Value);
                        if (fileName is null)
                            continue;

                        AddMappingEntry(extension, fileName);
                    }
                }
            }
            catch
            {
                // Fall back to scanning files.
            }
        }

        IndexUnmappedImages();
        return true;
    }

    public void Save()
    {
        if (!HasPackFolder)
            throw new InvalidOperationException("Pack folder is not set.");

        Directory.CreateDirectory(PackFolder);

        var manifest = new IconPackManifest
        {
            Name = PackName.Trim(),
            Version = string.IsNullOrWhiteSpace(ReadManifestVersion()) ? "1.0.0" : ReadManifestVersion()!,
            Author = "Provix",
            UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            Description = ReadManifestDescription(),
            Folder = FolderIconFile ?? "folder.png",
            File = FileIconFile ?? "file.png",
            Drive = DriveIconFile ?? "drive.png",
            Extensions = ExtensionMappings.ToDictionary(
                entry => "." + entry.Extension,
                entry => entry.ImageFileName,
                StringComparer.OrdinalIgnoreCase)
        };

        var manifestPath = Path.Combine(PackFolder, "iconpack.json");
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        File.WriteAllText(manifestPath, json);
    }

    private string? ReadManifestVersion()
    {
        var manifest = ReadExistingManifest();
        return manifest?.Version;
    }

    private string ReadManifestDescription()
    {
        return ReadExistingManifest()?.Description ?? string.Empty;
    }

    private IconPackManifest? ReadExistingManifest()
    {
        if (!HasPackFolder)
            return null;

        var manifestPath = FindManifestPath(PackFolder);
        if (manifestPath is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<IconPackManifest>(File.ReadAllText(manifestPath), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public string ImportSpecialIcon(IconPackSpecialKind kind, string sourceImagePath)
    {
        EnsurePackFolder();
        var targetName = kind switch
        {
            IconPackSpecialKind.Folder => "folder" + Path.GetExtension(sourceImagePath),
            IconPackSpecialKind.File => "file" + Path.GetExtension(sourceImagePath),
            IconPackSpecialKind.Drive => "drive" + Path.GetExtension(sourceImagePath),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        var copied = CopyImageIntoPack(sourceImagePath, targetName);
        switch (kind)
        {
            case IconPackSpecialKind.Folder:
                FolderIconFile = copied;
                break;
            case IconPackSpecialKind.File:
                FileIconFile = copied;
                break;
            case IconPackSpecialKind.Drive:
                DriveIconFile = copied;
                break;
        }

        return copied;
    }

    public string AddExtensionFromImage(string extension, string sourceImagePath)
    {
        EnsurePackFolder();
        var normalized = NormalizeExtension(extension);
        if (string.IsNullOrEmpty(normalized))
            throw new InvalidOperationException("Extension is required.");

        if (ReservedNames.Contains(normalized))
            throw new InvalidOperationException("This name is reserved for system icons.");

        var targetName = normalized + Path.GetExtension(sourceImagePath);
        var copied = CopyImageIntoPack(sourceImagePath, targetName);

        var existing = ExtensionMappings.FirstOrDefault(entry =>
            entry.Extension.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            ExtensionMappings.Remove(existing);

        AddMappingEntry(normalized, copied);
        return copied;
    }

    public void RemoveExtension(string extension)
    {
        var normalized = NormalizeExtension(extension);
        var entry = ExtensionMappings.FirstOrDefault(item =>
            item.Extension.Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (entry is not null)
            ExtensionMappings.Remove(entry);
    }

    public ImageSource? GetPreview(string? fileName)
    {
        if (!HasPackFolder || string.IsNullOrWhiteSpace(fileName))
            return null;

        var path = Path.Combine(PackFolder, fileName);
        if (!File.Exists(path) || !IsSupportedImage(path))
            return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.DecodePixelWidth = PreviewSize;
            image.DecodePixelHeight = PreviewSize;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            if (image.CanFreeze)
                image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private void EnsurePackFolder()
    {
        if (!HasPackFolder)
            CreateNew(PackName);
    }

    private string CopyImageIntoPack(string sourceImagePath, string targetFileName)
    {
        if (!File.Exists(sourceImagePath))
            throw new FileNotFoundException("Image file not found.", sourceImagePath);

        if (!IsSupportedImage(sourceImagePath))
            throw new InvalidOperationException("Unsupported image format.");

        var destination = Path.Combine(PackFolder, SanitizeFileName(targetFileName));
        File.Copy(sourceImagePath, destination, overwrite: true);
        return Path.GetFileName(destination);
    }

    private void IndexUnmappedImages()
    {
        if (!HasPackFolder)
            return;

        foreach (var path in Directory.EnumerateFiles(PackFolder, "*.*", SearchOption.TopDirectoryOnly))
        {
            if (!IsSupportedImage(path))
                continue;

            var fileName = Path.GetFileName(path);
            var stem = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(stem))
                continue;

            if (stem.Equals("folder", StringComparison.OrdinalIgnoreCase))
            {
                FolderIconFile ??= fileName;
                continue;
            }

            if (stem.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                FileIconFile ??= fileName;
                continue;
            }

            if (stem.Equals("drive", StringComparison.OrdinalIgnoreCase))
            {
                DriveIconFile ??= fileName;
                continue;
            }

            if (ReservedNames.Contains(stem))
                continue;

            if (ExtensionMappings.Any(entry => entry.Extension.Equals(stem, StringComparison.OrdinalIgnoreCase)))
                continue;

            AddMappingEntry(stem, fileName);
        }
    }

    private void AddMappingEntry(string extension, string imageFileName)
    {
        ExtensionMappings.Add(new IconPackMappingEntry
        {
            Extension = extension,
            ImageFileName = imageFileName,
            Preview = GetPreview(imageFileName)
        });
    }

    private string? NormalizeExistingFile(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || !HasPackFolder)
            return null;

        var candidate = Path.Combine(PackFolder, fileName.Trim());
        return File.Exists(candidate) ? Path.GetFileName(candidate) : null;
    }

    private static string? FindManifestPath(string folderPath)
    {
        foreach (var fileName in new[] { "iconpack.json", "manifest.json", "pack.json" })
        {
            var candidate = Path.Combine(folderPath, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string GetUniquePackFolder(string baseName)
    {
        var root = DefaultPacksRoot;
        var candidate = Path.Combine(root, baseName);
        if (!Directory.Exists(candidate))
            return candidate;

        for (var index = 2; index < 1000; index++)
        {
            candidate = Path.Combine(root, $"{baseName}-{index}");
            if (!Directory.Exists(candidate))
                return candidate;
        }

        return Path.Combine(root, $"{baseName}-{Guid.NewGuid():N}");
    }

    private static string SanitizeFolderName(string value)
    {
        var trimmed = value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
            trimmed = trimmed.Replace(invalid, '_');

        return trimmed.Trim();
    }

    private static string SanitizeFileName(string value)
    {
        var name = Path.GetFileName(value);
        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        return name;
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        return extension.Trim().TrimStart('.').ToLowerInvariant();
    }

    private static bool IsSupportedImage(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ico", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }
}
