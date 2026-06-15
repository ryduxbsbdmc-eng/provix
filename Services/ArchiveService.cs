using System.IO;
using FileExplorer.Models;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace FileExplorer.Services;

public sealed class ArchiveService
{
    public bool RequiresPassword(string archivePath)
    {
        try
        {
            using var archive = OpenArchive(archivePath);
            return archive.Entries.Any(entry => entry.IsEncrypted);
        }
        catch
        {
            return true;
        }
    }

    public IReadOnlyList<ArchiveEntryItem> GetEntries(string archivePath, string? password = null)
    {
        using var archive = OpenArchive(archivePath, password);

        return archive.Entries
            .Select(entry => new ArchiveEntryItem
            {
                EntryKey = entry.Key ?? string.Empty,
                Name = entry.Key ?? string.Empty,
                IsDirectory = entry.IsDirectory,
                OriginalSize = entry.IsDirectory ? 0 : entry.Size,
                CompressedSize = entry.IsDirectory ? 0 : entry.CompressedSize
            })
            .OrderBy(entry => entry.IsDirectory ? 0 : 1)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task ExtractAllAsync(
        string archivePath,
        string destinationDirectory,
        string? password = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ExtractAll(archivePath, destinationDirectory, password, cancellationToken), cancellationToken);

    public Task ExtractEntryAsync(
        string archivePath,
        string entryKey,
        string destinationDirectory,
        string? password = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => ExtractEntry(archivePath, entryKey, destinationDirectory, password, cancellationToken),
            cancellationToken);

    private static IArchive OpenArchive(string archivePath, string? password = null)
    {
        if (string.IsNullOrEmpty(password))
            return ArchiveFactory.OpenArchive(archivePath);

        return ArchiveFactory.OpenArchive(
            archivePath,
            new ReaderOptions { Password = password });
    }

    private static void ExtractAll(
        string archivePath,
        string destinationDirectory,
        string? password,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationDirectory);

        using var archive = OpenArchive(archivePath, password);
        var options = CreateExtractionOptions();

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (entry.IsDirectory)
                continue;

            entry.WriteToDirectory(destinationDirectory, options);
        }
    }

    private static void ExtractEntry(
        string archivePath,
        string entryKey,
        string destinationDirectory,
        string? password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destinationDirectory);

        using var archive = OpenArchive(archivePath, password);
        var entry = archive.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, entryKey, StringComparison.Ordinal));

        if (entry is null)
            throw new FileNotFoundException($"The archive does not contain \"{entryKey}\".");

        entry.WriteToDirectory(destinationDirectory, CreateExtractionOptions());
    }

    private static ExtractionOptions CreateExtractionOptions() =>
        new()
        {
            ExtractFullPath = true,
            Overwrite = true
        };
}
