using System.Text.Json.Serialization;

namespace FileExplorer.Models;

public sealed class AppSettings
{
    public int SettingsVersion { get; set; } = 8;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    public string CustomThemePath { get; set; } = string.Empty;

    public TimeFormatMode TimeFormat { get; set; } = TimeFormatMode.Hour24;

    public string Language { get; set; } = "en-US";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FileIconStyle FileIconStyle { get; set; } = FileIconStyle.Windows;

    public string CustomIconPackPath { get; set; } = string.Empty;

    public string CustomFontPath { get; set; } = string.Empty;

    public string UiFontId { get; set; } = UiFontIds.Default;

    public bool UseBuiltInMediaViewer { get; set; } = true;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AiProvider AiProvider { get; set; } = AiProvider.OpenRouter;

    public string OpenRouterApiKey { get; set; } = string.Empty;

    public string LocalAiEndpoint { get; set; } = string.Empty;

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

    public List<BookmarkRecord> Bookmarks { get; set; } = [];

    public bool ShowPreviewPanel { get; set; } = true;

    public double PreviewPanelWidth { get; set; } = 280;
}
