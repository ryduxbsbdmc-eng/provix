using System.Windows.Media;

namespace FileExplorer.Models;

public sealed class UiFontOption
{
    public required string Id { get; init; }

    public required string LabelKey { get; init; }

    public bool IsCustom { get; init; }
}
