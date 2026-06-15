using System.Diagnostics;
using System.IO;
using System.Text;
using FileExplorer.Models;

namespace FileExplorer.Services;

public sealed class ContentSearchService
{
    public const string QueryPrefix = "content:";

    private const int MaxResults = 2_000;

    private const long MaxFileBytes = 512 * 1024;

    private const int MaxDepth = 12;

    private static readonly HashSet<string> SearchableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".cs", ".json", ".xml", ".md", ".html", ".js", ".css", ".py",
        ".ts", ".tsx", ".jsx", ".yaml", ".yml", ".ini", ".cfg", ".log", ".xaml",
        ".csproj", ".sln", ".props", ".targets", ".bat", ".cmd", ".ps1", ".sh",
        ".sql", ".csv", ".toml", ".rpy", ".lua", ".go", ".rs", ".java", ".kt",
        ".cpp", ".h", ".hpp", ".c", ".vb", ".fs", ".swift"
    };

    public static bool TryParseQuery(string input, out string query)
    {
        query = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var trimmed = input.Trim();
        if (!trimmed.StartsWith(QueryPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        query = trimmed[QueryPrefix.Length..].Trim();
        return query.Length > 0;
    }

    public async Task<IReadOnlyList<ContentSearchMatch>> SearchAsync(
        string rootPath,
        string query,
        IProgress<SearchProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        var trimmedQuery = query.Trim();
        if (trimmedQuery.Length == 0)
            return [];

        return await Task.Run(
            () => SearchCore(rootPath, trimmedQuery, progress, cancellationToken),
            cancellationToken);
    }

    private List<ContentSearchMatch> SearchCore(
        string rootPath,
        string query,
        IProgress<SearchProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        var results = new List<ContentSearchMatch>(Math.Min(MaxResults, 128));
        var pendingDirectories = new Queue<(string Path, int Depth)>();
        pendingDirectories.Enqueue((rootPath, 0));

        var stopwatch = Stopwatch.StartNew();
        var lastStatusReportMs = 0L;
        var scannedDirectories = 0;
        var isTruncated = false;

        void ReportStatus(bool complete)
        {
            progress?.Report(new SearchProgressReport
            {
                TotalCount = results.Count,
                ScannedDirectoryCount = scannedDirectories,
                IsComplete = complete,
                IsTruncated = isTruncated
            });
        }

        while (pendingDirectories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (directoryPath, depth) = pendingDirectories.Dequeue();
            scannedDirectories++;

            if (stopwatch.ElapsedMilliseconds - lastStatusReportMs >= 120)
            {
                lastStatusReportMs = stopwatch.ElapsedMilliseconds;
                ReportStatus(complete: false);
            }

            IEnumerable<string> files;
            IEnumerable<string> directories;

            try
            {
                files = Directory.EnumerateFiles(directoryPath);
                directories = depth < MaxDepth ? Directory.EnumerateDirectories(directoryPath) : [];
            }
            catch
            {
                continue;
            }

            foreach (var filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (results.Count >= MaxResults)
                {
                    isTruncated = true;
                    ReportStatus(complete: true);
                    return results;
                }

                var extension = Path.GetExtension(filePath);
                if (string.IsNullOrEmpty(extension) || !SearchableExtensions.Contains(extension))
                    continue;

                if (!TextFileHelper.TryGetFileSize(filePath, out var size) || size > MaxFileBytes)
                    continue;

                try
                {
                    ScanFile(rootPath, filePath, query, results);
                }
                catch
                {
                    // Skip unreadable files.
                }
            }

            if (depth < MaxDepth)
            {
                foreach (var childDirectory in directories)
                {
                    if (ShouldSkipDirectory(childDirectory))
                        continue;

                    pendingDirectories.Enqueue((childDirectory, depth + 1));
                }
            }
        }

        ReportStatus(complete: true);
        return results;
    }

    private static void ScanFile(
        string rootPath,
        string filePath,
        string query,
        List<ContentSearchMatch> results)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            bufferSize: 81920,
            FileOptions.SequentialScan);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var lineNumber = 0;

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (line.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (results.Count >= MaxResults)
                return;

            var relativePath = Path.GetRelativePath(rootPath, filePath);
            results.Add(new ContentSearchMatch
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                LineNumber = lineNumber,
                LineText = TrimLine(line),
                RelativePath = relativePath
            });
        }
    }

    private static string TrimLine(string line) =>
        line.Length <= 240 ? line : line[..237] + "...";

    private static bool ShouldSkipDirectory(string directoryPath)
    {
        var name = Path.GetFileName(directoryPath);
        if (string.IsNullOrEmpty(name))
            return false;

        return name is ".git" or "node_modules" or "bin" or "obj" or ".vs" or "packages";
    }
}
