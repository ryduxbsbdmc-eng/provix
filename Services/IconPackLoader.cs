using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FileExplorer.Models;

namespace FileExplorer.Services;

public sealed class IconPackLoader
{
    private const int IconSize = 16;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly Dictionary<string, string> _extensionPaths = new(StringComparer.OrdinalIgnoreCase);
    private string _rootPath = string.Empty;
    private string _folderFile = "folder.png";
    private string _defaultFile = "file.png";
    private string _driveFile = "drive.png";

    public string Name { get; private set; } = string.Empty;

    public bool Load(string folderPath)
    {
        _extensionPaths.Clear();
        Name = string.Empty;

        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return false;

        _rootPath = Path.GetFullPath(folderPath);
        var manifestPath = FindManifestPath(_rootPath);
        if (manifestPath is not null)
            LoadManifest(manifestPath);

        IndexPngFiles(_rootPath);
        return Directory.EnumerateFiles(_rootPath, "*.*", SearchOption.TopDirectoryOnly)
            .Any(path => IsSupportedImage(path));
    }

    public ImageSource? GetFolderIcon() => LoadImage(_folderFile);

    public ImageSource? GetDriveIcon() => LoadImage(_driveFile) ?? GetFolderIcon();

    public ImageSource? GetFileIcon(string extension)
    {
        var normalized = NormalizeExtension(extension);
        if (!string.IsNullOrEmpty(normalized) &&
            _extensionPaths.TryGetValue(normalized, out var mapped))
        {
            return LoadImage(mapped);
        }

        return LoadImage(_defaultFile);
    }

    private void LoadManifest(string manifestPath)
    {
        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<IconPackManifest>(json, JsonOptions);
            if (manifest is null)
                return;

            if (!string.IsNullOrWhiteSpace(manifest.Name))
                Name = manifest.Name.Trim();

            if (!string.IsNullOrWhiteSpace(manifest.Folder))
                _folderFile = manifest.Folder.Trim();

            if (!string.IsNullOrWhiteSpace(manifest.File))
                _defaultFile = manifest.File.Trim();

            if (!string.IsNullOrWhiteSpace(manifest.Drive))
                _driveFile = manifest.Drive.Trim();

            foreach (var pair in manifest.Extensions)
            {
                var key = NormalizeExtension(pair.Key);
                if (string.IsNullOrEmpty(key) || string.IsNullOrWhiteSpace(pair.Value))
                    continue;

                _extensionPaths[key] = pair.Value.Trim();
            }
        }
        catch
        {
            // Ignore invalid manifest and rely on indexed files.
        }
    }

    private void IndexPngFiles(string folderPath)
    {
        foreach (var path in Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly))
        {
            if (!IsSupportedImage(path))
                continue;

            var fileName = Path.GetFileName(path);
            var extensionKey = NormalizeExtension(Path.GetFileNameWithoutExtension(fileName));
            if (string.IsNullOrEmpty(extensionKey))
                continue;

            if (extensionKey is "folder" or "file" or "drive" or "iconpack" or "manifest")
                continue;

            _extensionPaths.TryAdd(extensionKey, fileName);
        }
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

    private ImageSource? LoadImage(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        var path = Path.IsPathRooted(fileName)
            ? fileName
            : Path.Combine(_rootPath, fileName);

        if (!File.Exists(path) || !IsSupportedImage(path))
            return null;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.DecodePixelWidth = IconSize;
            image.DecodePixelHeight = IconSize;
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

    private static bool IsSupportedImage(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ico", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        return extension.Trim().TrimStart('.').ToLowerInvariant();
    }
}
