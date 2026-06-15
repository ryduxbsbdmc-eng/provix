namespace FileExplorer.Models;

public sealed class UiThemeComboItem
{
    public AppTheme Theme { get; init; }

    public string? JsonPath { get; init; }

    public bool RequiresManualPath { get; init; }

    public required string Label { get; init; }

    public string Description { get; init; } = string.Empty;
}
