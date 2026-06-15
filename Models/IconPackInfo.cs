namespace FileExplorer.Models;

public sealed class IconPackInfo
{
    public required string FolderPath { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Version { get; init; } = "1.0.0";

    public string Description { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public string UpdatedAt { get; init; } = string.Empty;

    public int ExtensionCount { get; init; }

    public int ImageCount { get; init; }

    public bool HasFolderIcon { get; init; }

    public bool HasFileIcon { get; init; }

    public bool HasDriveIcon { get; init; }

    public bool IsBuiltIn { get; init; }
}
