namespace FileExplorer.Models;

public sealed class AiCommand
{
    public string Op { get; set; } = string.Empty;
    public string? Path { get; set; }
    public string? Src { get; set; }
    public string? Dest { get; set; }
    public string? Message { get; set; }
    public string? Content { get; set; }

    public string GetDisplayDescription()
    {
        var op = Op.Trim().ToUpperInvariant();

        return op switch
        {
            "MKDIR" => $"Create folder: {Path}",
            "MOVE" => $"Move: {Src} → {Dest}",
            "RENAME" => $"Rename: {Src} → {Dest}",
            "WRITE" => $"Write file: {Path}",
            "DELETE" => $"Delete: {Path}",
            "GIT_COMMIT" => $"Git commit: {Truncate(Message, 80)}",
            _ => $"{Op}: {Path ?? Src ?? Dest ?? Message ?? "(unknown)"}"
        };
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "(no message)";

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "…";
    }
}

public sealed class AiCommandResult
{
    public bool Success { get; init; }
    public int ExecutedCount { get; init; }
    public string Message { get; init; } = string.Empty;

    public static AiCommandResult Succeeded(int count, string message) =>
        new() { Success = true, ExecutedCount = count, Message = message };

    public static AiCommandResult Failed(string message) =>
        new() { Success = false, ExecutedCount = 0, Message = message };
}

public sealed class AiGenerationResult
{
    public bool Success { get; init; }
    public IReadOnlyList<AiCommand> Commands { get; init; } = [];
    public string ErrorMessage { get; init; } = string.Empty;

    public static AiGenerationResult Ok(IReadOnlyList<AiCommand> commands) =>
        new() { Success = true, Commands = commands };

    public static AiGenerationResult Fail(string message) =>
        new() { Success = false, ErrorMessage = message };
}
