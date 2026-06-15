namespace FileExplorer.Models;

public sealed class AppSettings
{
    public int SettingsVersion { get; set; } = 1;

    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public TimeFormatMode TimeFormat { get; set; } = TimeFormatMode.Hour24;

    public string Language { get; set; } = "en-US";

    public string OpenRouterApiKey { get; set; } = string.Empty;

    public string PreferredAiModel { get; set; } = "openai/gpt-4o-mini";

    /// <summary>1.0 = default wheel speed.</summary>
    public double ScrollSensitivity { get; set; } = 1.0;

    public double TerminalPanelHeight { get; set; } = 220;

    public bool IsTerminalOpen { get; set; }

    public double WindowWidth { get; set; } = 1100;

    public double WindowHeight { get; set; } = 680;

    /// <summary>0 = Normal, 1 = Minimized, 2 = Maximized.</summary>
    public int WindowState { get; set; }

    public List<NavigationHistoryRecord> NavigationHistory { get; set; } = [];
}
