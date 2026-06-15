namespace FileExplorer.Models;

public sealed class PaneTabEventArgs : EventArgs
{
    public PaneTabEventArgs(PaneTab tab)
    {
        Tab = tab;
    }

    public PaneTab Tab { get; }
}
