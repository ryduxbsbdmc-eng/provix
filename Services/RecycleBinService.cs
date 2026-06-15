using System.IO;
using FileExplorer.Models;
using Microsoft.VisualBasic.FileIO;

namespace FileExplorer.Services;

public sealed class RecycleBinService
{
    public void SendToRecycleBin(FileSystemEntry entry)
    {
        if (entry.IsDirectory)
        {
            SendDirectoryToRecycleBin(entry.FullPath);
            return;
        }

        FileSystem.DeleteFile(
            entry.FullPath,
            UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin);
    }

    private static void SendDirectoryToRecycleBin(string directoryPath)
    {
        foreach (var file in Directory.GetFiles(directoryPath))
        {
            FileSystem.DeleteFile(
                file,
                UIOption.OnlyErrorDialogs,
                RecycleOption.SendToRecycleBin);
        }

        foreach (var subdirectory in Directory.GetDirectories(directoryPath))
            SendDirectoryToRecycleBin(subdirectory);

        FileSystem.DeleteDirectory(
            directoryPath,
            UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin);
    }
}
