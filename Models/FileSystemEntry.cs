using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace FileExplorer.Models;

public sealed class FileSystemEntry : INotifyPropertyChanged
{
    private long _size = -1;

    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public required bool IsDirectory { get; init; }

    public DateTime DateModified { get; init; }

    public string Type { get; init; } = string.Empty;

    public long Size
    {
        get => _size;
        set
        {
            if (_size == value)
                return;

            _size = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SizeDisplay));
        }
    }

    public ImageSource? Icon { get; init; }

    public GitFileStatusType GitStatus { get; set; } = GitFileStatusType.Unchanged;

    public string SizeDisplay => _size < 0 ? "..." : FormatSize(_size);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{size:0} {units[unitIndex]}"
            : $"{size:0.##} {units[unitIndex]}";
    }
}
