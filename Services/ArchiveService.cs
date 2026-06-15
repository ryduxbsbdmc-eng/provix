using System.IO;

using FileExplorer.Models;

using SharpCompress.Archives;

using SharpCompress.Common;



namespace FileExplorer.Services;



public sealed class ArchiveService

{

    public IReadOnlyList<ArchiveEntryItem> GetEntries(string archivePath)

    {

        using var archive = ArchiveFactory.OpenArchive(archivePath);



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



    public Task ExtractAllAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken = default) =>

        Task.Run(() => ExtractAll(archivePath, destinationDirectory, cancellationToken), cancellationToken);



    public Task ExtractEntryAsync(

        string archivePath,

        string entryKey,

        string destinationDirectory,

        CancellationToken cancellationToken = default) =>

        Task.Run(() => ExtractEntry(archivePath, entryKey, destinationDirectory, cancellationToken), cancellationToken);



    private static void ExtractAll(string archivePath, string destinationDirectory, CancellationToken cancellationToken)

    {

        Directory.CreateDirectory(destinationDirectory);



        using var archive = ArchiveFactory.OpenArchive(archivePath);

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

        CancellationToken cancellationToken)

    {

        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(destinationDirectory);



        using var archive = ArchiveFactory.OpenArchive(archivePath);

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


