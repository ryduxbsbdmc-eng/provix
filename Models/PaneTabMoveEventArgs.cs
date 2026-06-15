using FileExplorer.Controls;

namespace FileExplorer.Models;

public sealed class PaneTabMoveEventArgs : EventArgs
{
    public required PaneTab Tab { get; init; }

    public required DirectoryPaneControl SourceControl { get; init; }

    public int InsertIndex { get; init; }
}
