using System.IO;

namespace FileExplorer.Services;

public sealed class FileRenameService
{
    public FileRenameResult Rename(string sourcePath, string newName)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return FileRenameResult.Failed("No item was selected.");

        if (string.IsNullOrWhiteSpace(newName))
            return FileRenameResult.Failed("Name cannot be empty.");

        var trimmedName = newName.Trim();
        if (trimmedName is "." or "..")
            return FileRenameResult.Failed("This name is not allowed.");

        if (trimmedName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            return FileRenameResult.Failed("The name contains invalid characters.");

        var fullSource = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSource) && !Directory.Exists(fullSource))
            return FileRenameResult.Failed("The selected item could not be found.");

        var parentDirectory = Path.GetDirectoryName(fullSource);
        if (string.IsNullOrEmpty(parentDirectory))
            return FileRenameResult.Failed("Unable to determine the parent folder.");

        var resolvedName = trimmedName;
        var destinationPath = Path.GetFullPath(Path.Combine(parentDirectory, resolvedName));

        if (string.Equals(fullSource.TrimEnd('\\'), destinationPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            return FileRenameResult.Succeeded(destinationPath, noChange: true);

        if (Directory.Exists(destinationPath) || File.Exists(destinationPath))
            return FileRenameResult.Failed("An item with that name already exists.");

        try
        {
            if (Directory.Exists(fullSource))
                Directory.Move(fullSource, destinationPath);
            else
                File.Move(fullSource, destinationPath);

            return FileRenameResult.Succeeded(destinationPath);
        }
        catch (UnauthorizedAccessException)
        {
            return FileRenameResult.Failed("Permission denied. Unable to rename the item.");
        }
        catch (IOException ex) when (IsAlreadyExistsError(ex))
        {
            return FileRenameResult.Failed("An item with that name already exists.");
        }
        catch (Exception ex)
        {
            return FileRenameResult.Failed(ex.Message);
        }
    }

    private static bool IsAlreadyExistsError(IOException exception) =>
        exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("Cannot create a file when that file already exists", StringComparison.OrdinalIgnoreCase);
}

public sealed class FileRenameResult
{
    public bool Success { get; init; }
    public string? DestinationPath { get; init; }
    public string? ErrorMessage { get; init; }
    public bool NoChange { get; init; }

    public static FileRenameResult Succeeded(string destinationPath, bool noChange = false) =>
        new() { Success = true, DestinationPath = destinationPath, NoChange = noChange };

    public static FileRenameResult Failed(string message) =>
        new() { Success = false, ErrorMessage = message };
}
