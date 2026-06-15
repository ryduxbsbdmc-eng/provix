namespace FileExplorer.Models;

public sealed class IconPackManifest
{
    public string Name { get; set; } = string.Empty;

    public string Version { get; set; } = "1.0.0";

    public string Description { get; set; } = string.Empty;

    public string Author { get; set; } = "Provix";

    public string UpdatedAt { get; set; } = string.Empty;

    public string Folder { get; set; } = "folder.png";

    public string File { get; set; } = "file.png";

    public string Drive { get; set; } = "drive.png";

    public Dictionary<string, string> Extensions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
