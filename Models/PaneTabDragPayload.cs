using FileExplorer.Controls;

namespace FileExplorer.Models;

public sealed class PaneTabDragPayload
{
    public required PaneTab Tab { get; init; }

    public required DirectoryPaneControl SourceControl { get; init; }
}
