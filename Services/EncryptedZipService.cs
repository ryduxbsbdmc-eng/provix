using System.IO;
using FileExplorer.Models;
using ICSharpCode.SharpZipLib.Zip;

namespace FileExplorer.Services;

public sealed class EncryptedZipService
{
    private static readonly HashSet<string> SkippedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".idea",
        "node_modules",
        "__pycache__",
        "bin",
        "obj"
    };

    public Task CreateFromDirectoryAsync(
        string sourceDirectory,
        string destinationZipPath,
        string password,
        FolderZipEncryptionMethod encryptionMethod,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => CreateFromDirectory(
                sourceDirectory,
                destinationZipPath,
                password,
                encryptionMethod,
                progress,
                cancellationToken),
            cancellationToken);

    private static void CreateFromDirectory(
        string sourceDirectory,
        string destinationZipPath,
        string password,
        FolderZipEncryptionMethod encryptionMethod,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Folder not found: {sourceDirectory}");

        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Password is required.");

        var parentDirectory = Path.GetDirectoryName(destinationZipPath);
        if (!string.IsNullOrEmpty(parentDirectory))
            Directory.CreateDirectory(parentDirectory);

        if (File.Exists(destinationZipPath))
            throw new IOException($"A file named \"{Path.GetFileName(destinationZipPath)}\" already exists.");

        var rootFolderName = Path.GetFileName(sourceDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(rootFolderName))
            rootFolderName = "folder";

        using var output = File.Create(destinationZipPath);
        using var zipStream = new ZipOutputStream(output)
        {
            IsStreamOwner = true
        };

        zipStream.SetLevel(6);
        zipStream.Password = password;

        var useAes = encryptionMethod == FolderZipEncryptionMethod.Aes256;
        var addedAny = false;

        foreach (var filePath in EnumerateFiles(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(sourceDirectory, filePath).Replace('\\', '/');
            var entryName = $"{rootFolderName}/{relativePath}";

            progress?.Report(entryName);

            var entry = new ZipEntry(entryName)
            {
                DateTime = File.GetLastWriteTime(filePath),
                Size = new FileInfo(filePath).Length
            };

            if (useAes)
                entry.AESKeySize = 256;

            zipStream.PutNextEntry(entry);
            using (var input = File.OpenRead(filePath))
                input.CopyTo(zipStream);

            zipStream.CloseEntry();
            addedAny = true;
        }

        if (!addedAny)
            throw new InvalidOperationException("The folder does not contain any files to archive.");

        zipStream.Finish();
    }

    private static IEnumerable<string> EnumerateFiles(string sourceDirectory)
    {
        foreach (var filePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            if (!ShouldSkipPath(sourceDirectory, filePath))
                yield return filePath;
        }
    }

    private static bool ShouldSkipPath(string rootDirectory, string filePath)
    {
        var relativePath = Path.GetRelativePath(rootDirectory, filePath);
        foreach (var segment in relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (SkippedDirectoryNames.Contains(segment))
                return true;
        }

        return false;
    }
}
