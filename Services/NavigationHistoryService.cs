using System.Collections.ObjectModel;
using System.IO;
using FileExplorer.Models;

namespace FileExplorer.Services;

public sealed class NavigationHistoryService
{
    private const int MaxItems = 80;

    private readonly FileIconService _iconService;

    public NavigationHistoryService(FileIconService iconService)
    {
        _iconService = iconService;
    }

    public ObservableCollection<NavigationHistoryItem> Items { get; } = [];

    public void LoadFromSettings()
    {
        Items.Clear();

        foreach (var record in SettingsManager.Instance.Current.NavigationHistory)
        {
            if (string.IsNullOrWhiteSpace(record.Path))
                continue;

            if (record.Path.StartsWith("provix-ftp:", StringComparison.OrdinalIgnoreCase))
                continue;

            var item = CreateItem(record);
            if (item is not null)
                Items.Add(item);
        }
    }

    public void RecordFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath))
                return;

            Record(fullPath, NavigationHistoryKind.Folder);
        }
        catch
        {
            // Ignore invalid paths.
        }
    }

    public void RecordFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                return;

            Record(fullPath, NavigationHistoryKind.File);
        }
        catch
        {
            // Ignore invalid paths.
        }
    }

    public void Clear()
    {
        Items.Clear();
        SettingsManager.Instance.UpdateNavigationHistory([]);
    }

    private void Record(string fullPath, NavigationHistoryKind kind)
    {
        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!PathsEqual(Items[i].Path, fullPath))
                continue;

            Items.RemoveAt(i);
            break;
        }

        var record = new NavigationHistoryRecord
        {
            Path = fullPath,
            Kind = kind,
            VisitedAtTicks = DateTime.UtcNow.Ticks
        };

        var item = CreateItem(record);
        if (item is null)
            return;

        Items.Insert(0, item);

        while (Items.Count > MaxItems)
            Items.RemoveAt(Items.Count - 1);

        Persist();
    }

    private NavigationHistoryItem? CreateItem(NavigationHistoryRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.Path))
            return null;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(record.Path);
        }
        catch
        {
            return null;
        }

        var kind = record.Kind;
        var exists = kind == NavigationHistoryKind.Folder
            ? Directory.Exists(fullPath)
            : File.Exists(fullPath);

        var loc = LocalizationManager.Instance;
        var visitedAt = new DateTime(record.VisitedAtTicks, DateTimeKind.Utc).ToLocalTime();
        var displayName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            is { Length: > 0 } name
                ? name
                : fullPath;

        return new NavigationHistoryItem
        {
            Path = fullPath,
            DisplayName = displayName,
            Kind = kind,
            VisitedAt = visitedAt,
            Icon = kind == NavigationHistoryKind.Folder
                ? _iconService.GetFolderIcon(fullPath)
                : _iconService.GetFileIcon(fullPath),
            KindLabel = kind == NavigationHistoryKind.Folder
                ? loc["UI_HistoryFolder"]
                : loc["UI_HistoryFile"],
            VisitedAtDisplay = SettingsManager.Instance.FormatDateTime(visitedAt),
            Exists = exists
        };
    }

    private void Persist()
    {
        var records = Items.Select(item => new NavigationHistoryRecord
        {
            Path = item.Path,
            Kind = item.Kind,
            VisitedAtTicks = item.VisitedAt.ToUniversalTime().Ticks
        }).ToList();

        SettingsManager.Instance.UpdateNavigationHistory(records);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
