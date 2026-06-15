namespace FileExplorer.Models;

public sealed class PaneTab
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string? Path { get; set; }

    public Stack<string> BackHistory { get; } = new();

    public Stack<string> ForwardHistory { get; } = new();

    public string GetTitle()
    {
        if (string.IsNullOrWhiteSpace(Path))
            return string.Empty;

        var name = System.IO.Path.GetFileName(Path.TrimEnd(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar));

        return name.Length > 0 ? name : Path;
    }
}
