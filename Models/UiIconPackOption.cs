using FileExplorer.Models;

namespace FileExplorer.Models;

public sealed class UiIconPackOption
{
    public FileIconStyle Style { get; init; }

    public string? PackFolderPath { get; init; }

    public bool RequiresManualPath { get; init; }

    public required string Label { get; init; }

    public string Description { get; init; } = string.Empty;
}
