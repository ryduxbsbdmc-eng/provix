using System.IO;
using FileExplorer.Models;

namespace FileExplorer.Services;

public static class FolderCompareService
{
    public static IReadOnlyList<FolderCompareEntry> Compare(string leftPath, string rightPath)
    {
        if (!Directory.Exists(leftPath) || !Directory.Exists(rightPath))
            return [];

        var leftEntries = EnumerateEntries(leftPath);
        var rightEntries = EnumerateEntries(rightPath);
        var allNames = new HashSet<string>(leftEntries.Keys, StringComparer.OrdinalIgnoreCase);
        allNames.UnionWith(rightEntries.Keys);

        var results = new List<FolderCompareEntry>(allNames.Count);

        foreach (var name in allNames.OrderBy(static n => n, StringComparer.OrdinalIgnoreCase))
        {
            var hasLeft = leftEntries.TryGetValue(name, out var left);
            var hasRight = rightEntries.TryGetValue(name, out var right);

            if (hasLeft && !hasRight)
            {
                results.Add(new FolderCompareEntry
                {
                    Name = name,
                    Kind = FolderCompareKind.OnlyLeft,
                    LeftSize = left.Size,
                    IsDirectory = left.IsDirectory
                });
                continue;
            }

            if (!hasLeft && hasRight)
            {
                results.Add(new FolderCompareEntry
                {
                    Name = name,
                    Kind = FolderCompareKind.OnlyRight,
                    RightSize = right.Size,
                    IsDirectory = right.IsDirectory
                });
                continue;
            }

            var sameSize = left.IsDirectory == right.IsDirectory &&
                           (!left.IsDirectory && left.Size == right.Size);

            results.Add(new FolderCompareEntry
            {
                Name = name,
                Kind = sameSize ? FolderCompareKind.BothSame : FolderCompareKind.BothDifferent,
                LeftSize = left.Size,
                RightSize = right.Size,
                IsDirectory = left.IsDirectory
            });
        }

        return results;
    }

    private static Dictionary<string, (bool IsDirectory, long Size)> EnumerateEntries(string rootPath)
    {
        var entries = new Dictionary<string, (bool IsDirectory, long Size)>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in Directory.EnumerateDirectories(rootPath))
        {
            var name = Path.GetFileName(directory);
            if (!string.IsNullOrEmpty(name))
                entries[name] = (true, 0);
        }

        foreach (var file in Directory.EnumerateFiles(rootPath))
        {
            var name = Path.GetFileName(file);
            if (string.IsNullOrEmpty(name))
                continue;

            long size = 0;
            try
            {
                size = new FileInfo(file).Length;
            }
            catch
            {
                // Keep zero size.
            }

            entries[name] = (false, size);
        }

        return entries;
    }
}
