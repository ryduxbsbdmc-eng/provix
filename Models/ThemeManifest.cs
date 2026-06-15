namespace FileExplorer.Models;

public sealed class ThemeManifest
{
    public string Name { get; set; } = "Custom theme";

    public string Version { get; set; } = "1.0.0";

    public string Description { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string UpdatedAt { get; set; } = string.Empty;

    public Dictionary<string, string> Colors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
