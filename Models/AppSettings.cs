namespace FileExplorer.Models;

public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public TimeFormatMode TimeFormat { get; set; } = TimeFormatMode.Hour24;

    public string Language { get; set; } = "en-US";

    public string OpenRouterApiKey { get; set; } = string.Empty;

    public string PreferredAiModel { get; set; } = "openai/gpt-4o-mini";
}
