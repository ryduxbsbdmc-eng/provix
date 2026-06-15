using System.IO;

namespace FileExplorer.Services;

public sealed class FileMoveService
{
    public FileMoveResult MoveItems(IReadOnlyList<string> sourcePaths, string destinationDirectory)
    {
        if (sourcePaths.Count == 0)
            return FileMoveResult.Failed("No items to move.");

        if (string.IsNullOrWhiteSpace(destinationDirectory) || !Directory.Exists(destinationDirectory))
            return FileMoveResult.Failed("The destination folder is not available.");

        var destinationRoot = Path.GetFullPath(destinationDirectory);
        var movedCount = 0;
        string? lastError = null;

        foreach (var sourcePath in sourcePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!TryMoveSingleItem(sourcePath, destinationRoot, out var error))
                    lastError = error;
                else
                    movedCount++;
            }
            catch (UnauthorizedAccessException)
            {
                lastError = "Access denied while moving one or more items.";
            }
            catch (IOException ex) when (IsAlreadyExistsError(ex))
            {
                lastError = $"An item named \"{Path.GetFileName(sourcePath)}\" already exists in the destination.";
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
            }
        }

        if (movedCount == 0)
            return FileMoveResult.Failed(lastError ?? "Unable to move the selected items.");

        return FileMoveResult.Succeeded(movedCount, lastError);
    }

    private static bool TryMoveSingleItem(string sourcePath, string destinationDirectory, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            error = "A source path was empty.";
            return false;
        }

        var fullSource = Path.GetFullPath(sourcePath);
        var fileName = Path.GetFileName(fullSource);

        if (string.IsNullOrEmpty(fileName))
        {
            error = "Unable to determine the item name.";
            return false;
        }

        var destinationPath = Path.GetFullPath(Path.Combine(destinationDirectory, fileName));

        if (!File.Exists(fullSource) && !Directory.Exists(fullSource))
        {
            error = $"\"{fileName}\" could not be found.";
            return false;
        }

        if (IsSamePath(fullSource, destinationPath))
            return true;

        var sourceParent = Path.GetDirectoryName(fullSource.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrEmpty(sourceParent) && IsSamePath(sourceParent, destinationDirectory))
            return true;

        if (IsInsideDirectory(fullSource, destinationDirectory))
        {
            error = "Cannot move a folder into one of its own subfolders.";
            return false;
        }

        if (Directory.Exists(fullSource))
        {
            if (Directory.Exists(destinationPath) || File.Exists(destinationPath))
            {
                error = $"A file or folder named \"{fileName}\" already exists.";
                return false;
            }

            Directory.Move(fullSource, destinationPath);
            return true;
        }

        if (File.Exists(destinationPath))
        {
            error = $"A file or folder named \"{fileName}\" already exists.";
            return false;
        }

        File.Move(fullSource, destinationPath);
        return true;
    }

    private static bool IsSamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd('\\'),
            Path.GetFullPath(right).TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsInsideDirectory(string sourcePath, string directoryPath)
    {
        if (!Directory.Exists(sourcePath))
            return false;

        var normalizedSource = Path.GetFullPath(sourcePath).TrimEnd('\\');
        var normalizedDirectory = Path.GetFullPath(directoryPath).TrimEnd('\\');

        return normalizedDirectory.StartsWith(normalizedSource + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAlreadyExistsError(IOException exception) =>
        exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
        exception.Message.Contains("Cannot create a file when that file already exists", StringComparison.OrdinalIgnoreCase);
}

public sealed class FileMoveResult
{
    public bool Success { get; init; }
    public int MovedCount { get; init; }
    public string? ErrorMessage { get; init; }

    public static FileMoveResult Succeeded(int movedCount, string? warning = null) =>
        new() { Success = movedCount > 0, MovedCount = movedCount, ErrorMessage = warning };

    public static FileMoveResult Failed(string message) =>
        new() { Success = false, MovedCount = 0, ErrorMessage = message };
}
