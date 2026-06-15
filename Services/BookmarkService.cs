using System.Collections.ObjectModel;
using System.IO;
using FileExplorer.Models;

namespace FileExplorer.Services;

public sealed class BookmarkService
{
    private readonly FileIconService _iconService;

    public BookmarkService(FileIconService iconService)
    {
        _iconService = iconService;
    }

    public ObservableCollection<BookmarkItem> Items { get; } = [];

    public void LoadFromSettings()
    {
        Items.Clear();

        foreach (var record in SettingsManager.Instance.Current.Bookmarks)
        {
            if (string.IsNullOrWhiteSpace(record.Path))
                continue;

            var item = CreateItem(record);
            if (item is not null)
                Items.Add(item);
        }
    }

    public bool Contains(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            return Items.Any(item => PathsEqual(item.Path, fullPath));
        }
        catch
        {
            return false;
        }
    }

    public void Add(string path, string? label = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }

        if (!Directory.Exists(fullPath))
            return;

        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!PathsEqual(Items[i].Path, fullPath))
                continue;

            Items.RemoveAt(i);
            break;
        }

        var record = new BookmarkRecord
        {
            Path = fullPath,
            Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
            AddedAtTicks = DateTime.UtcNow.Ticks
        };

        var item = CreateItem(record);
        if (item is null)
            return;

        Items.Insert(0, item);
        Persist();
    }

    public void Remove(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            return;
        }

        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!PathsEqual(Items[i].Path, fullPath))
                continue;

            Items.RemoveAt(i);
            Persist();
            return;
        }
    }

    public void Clear()
    {
        Items.Clear();
        SettingsManager.Instance.UpdateBookmarks([]);
    }

    private BookmarkItem? CreateItem(BookmarkRecord record)
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

        var exists = Directory.Exists(fullPath);
        var displayName = !string.IsNullOrWhiteSpace(record.Label)
            ? record.Label.Trim()
            : Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
              is { Length: > 0 } name
                ? name
                : fullPath;

        return new BookmarkItem
        {
            Path = fullPath,
            DisplayName = displayName,
            Icon = _iconService.GetFolderIcon(fullPath),
            Exists = exists
        };
    }

    private void Persist()
    {
        var records = Items.Select(item => new BookmarkRecord
        {
            Path = item.Path,
            Label = item.DisplayName,
            AddedAtTicks = DateTime.UtcNow.Ticks
        }).ToList();

        SettingsManager.Instance.UpdateBookmarks(records);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
