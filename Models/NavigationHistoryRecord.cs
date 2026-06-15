namespace FileExplorer.Models;

public sealed class NavigationHistoryRecord
{
    public string Path { get; set; } = string.Empty;

    public NavigationHistoryKind Kind { get; set; } = NavigationHistoryKind.Folder;

    public long VisitedAtTicks { get; set; }
}
