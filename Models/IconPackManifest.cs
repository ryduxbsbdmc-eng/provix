namespace FileExplorer.Models;

public sealed class IconPackManifest
{
    public string Name { get; set; } = string.Empty;

    public string Folder { get; set; } = "folder.png";

    public string File { get; set; } = "file.png";

    public string Drive { get; set; } = "drive.png";

    public Dictionary<string, string> Extensions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
