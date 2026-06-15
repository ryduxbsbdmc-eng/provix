using System.Windows.Media;

namespace FileExplorer.Models;

public sealed class NavigationHistoryItem
{
    public required string Path { get; init; }

    public required string DisplayName { get; init; }

    public required NavigationHistoryKind Kind { get; init; }

    public required DateTime VisitedAt { get; init; }

    public ImageSource? Icon { get; init; }

    public required string KindLabel { get; init; }

    public required string VisitedAtDisplay { get; init; }

    public bool Exists { get; init; } = true;
}
