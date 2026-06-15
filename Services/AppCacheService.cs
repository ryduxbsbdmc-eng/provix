using System.IO;

namespace FileExplorer.Services;

public sealed class CacheClearResult
{
    public int FilesDeleted { get; init; }

    public long BytesFreed { get; init; }

    public static CacheClearResult Empty { get; } = new();
}

public static class AppCacheService
{
    public static string CacheRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileExplorer",
            "Cache");

    public static string ImportedProfilesRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileExplorer",
            "ImportedProfiles");

    public static string ImportedFontsRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FileExplorer",
            "Fonts");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(CacheRoot);
        Directory.CreateDirectory(ImportedProfilesRoot);
        Directory.CreateDirectory(ImportedFontsRoot);
    }

    public static CacheClearResult ClearDiskCache()
    {
        EnsureDirectories();

        var filesDeleted = 0;
        long bytesFreed = 0;

        if (!Directory.Exists(CacheRoot))
            return CacheClearResult.Empty;

        foreach (var file in Directory.EnumerateFiles(CacheRoot, "*", SearchOption.AllDirectories))
        {
            try
            {
                var info = new FileInfo(file);
                bytesFreed += info.Length;
                File.Delete(file);
                filesDeleted++;
            }
            catch
            {
                // Skip locked or inaccessible cache files.
            }
        }

        TryDeleteEmptyDirectories(CacheRoot);
        return new CacheClearResult { FilesDeleted = filesDeleted, BytesFreed = bytesFreed };
    }

    private static void TryDeleteEmptyDirectories(string root)
    {
        if (!Directory.Exists(root))
            return;

        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                    Directory.Delete(directory);
            }
            catch
            {
                // Ignore directories that are not empty or locked.
            }
        }
    }
}
