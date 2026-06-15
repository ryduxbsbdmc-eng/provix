namespace FileExplorer.Models;

public sealed class UiAiProviderOption
{
    public required AiProvider Provider { get; init; }

    public required string Label { get; init; }
}
