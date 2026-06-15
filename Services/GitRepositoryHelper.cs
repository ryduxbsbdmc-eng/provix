using System.IO;

namespace FileExplorer.Services;

public static class GitRepositoryHelper
{
    /// <summary>
    /// Walks from <paramref name="startingPath"/> up to the filesystem root and returns the directory
    /// that contains <c>.git</c>, or null when not inside a repository.
    /// </summary>
    public static string? GetGitRepositoryRoot(string? startingPath)
    {
        if (string.IsNullOrWhiteSpace(startingPath))
            return null;

        DirectoryInfo? dir;
        try
        {
            var fullPath = Path.GetFullPath(startingPath);
            if (!Directory.Exists(fullPath))
                return null;

            dir = new DirectoryInfo(fullPath);
        }
        catch
        {
            return null;
        }

        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>
    /// Returns true when <paramref name="directoryPath"/> contains a <c>.git</c> directory or file at that exact path.
    /// </summary>
    public static bool HasGitRepositoryAtDirectory(string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(directoryPath);
            return Directory.Exists(Path.Combine(fullPath, ".git")) ||
                   File.Exists(Path.Combine(fullPath, ".git"));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true when <paramref name="directoryPath"/> or any ancestor contains a <c>.git</c> directory or file.
    /// </summary>
    public static bool ContainsGitRepository(string? directoryPath) =>
        GetGitRepositoryRoot(directoryPath) is not null;
}
