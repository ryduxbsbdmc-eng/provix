using System.IO;

namespace FileExplorer.Services;

public static class DirectorySizeHelper
{
    private static readonly EnumerationOptions SizeEnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
    };

    public static long CalculateSize(string directoryPath, CancellationToken cancellationToken = default)
    {
        long total = 0;

        try
        {
            foreach (var file in Directory.EnumerateFiles(directoryPath, "*", SizeEnumerationOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    total += new FileInfo(file).Length;
                }
                catch
                {
                    // Skip files that disappear or are locked.
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Skip directories that cannot be enumerated.
        }

        return total;
    }
}
