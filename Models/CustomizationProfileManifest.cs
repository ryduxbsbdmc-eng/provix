namespace FileExplorer.Models;

public sealed class CustomizationProfileManifest
{
    public int Version { get; set; } = 1;

    public string Theme { get; set; } = nameof(AppTheme.Dark);

    public string? ThemeFile { get; set; }

    public string IconStyle { get; set; } = "Windows";

    public string? IconPackFolder { get; set; }

    public string UiFontId { get; set; } = UiFontIds.Default;

    public string? FontFile { get; set; }

    public string ExportedAt { get; set; } = string.Empty;

    public string AppVersion { get; set; } = string.Empty;
}
