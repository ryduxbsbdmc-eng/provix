using System.IO;

namespace FileExplorer.Services;

public static class TextFileHelper
{
    private static readonly HashSet<string> EditableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".cs", ".xaml", ".json", ".xml", ".md", ".html", ".js", ".css", ".py"
    };

    public static bool IsEditableTextFile(string filePath)
    {
        var extension = Path.GetExtension(filePath);
        return !string.IsNullOrEmpty(extension) && EditableExtensions.Contains(extension);
    }
}
