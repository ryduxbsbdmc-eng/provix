namespace FileExplorer.Models;

public static class PaneTabDragSession
{
    public static PaneTabDragPayload? Active { get; private set; }

    public static bool MoveCompleted { get; private set; }

    public static void Begin(PaneTabDragPayload payload)
    {
        Active = payload;
        MoveCompleted = false;
    }

    public static void MarkMoveCompleted() =>
        MoveCompleted = true;

    public static void End()
    {
        Active = null;
        MoveCompleted = false;
    }
}
