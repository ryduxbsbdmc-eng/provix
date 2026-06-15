using System.IO;
using System.Text.Json;
using FileExplorer.Models;

namespace FileExplorer.Services;

public sealed class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly Lazy<SettingsManager> LazyInstance = new(() => new SettingsManager());

    private readonly string _settingsPath;

    private SettingsManager()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folder = Path.Combine(appData, "FileExplorer");
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, "settings.json");
    }

    public static SettingsManager Instance => LazyInstance.Value;

    public AppSettings Current { get; private set; } = new();

    public string SettingsFilePath => _settingsPath;

    public event EventHandler<string>? SettingChanged;

    public void Load()
    {
        if (!File.Exists(_settingsPath))
        {
            Current = CreateDefaultSettings();
            Save();
            return;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? CreateDefaultSettings();
        }
        catch
        {
            Current = CreateDefaultSettings();
        }

        Normalize(Current);
    }

    public void Save()
    {
        Normalize(Current);
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    public void UpdateTheme(AppTheme theme)
    {
        if (Current.Theme == theme)
            return;

        Current.Theme = theme;
        if (theme != AppTheme.Custom)
            Current.CustomThemePath = string.Empty;

        Save();
        SettingChanged?.Invoke(this, nameof(AppSettings.Theme));
    }

    public void UpdateCustomThemePath(string path)
    {
        var normalized = path.Trim();
        if (string.Equals(Current.CustomThemePath, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        Current.CustomThemePath = normalized;
        if (!string.IsNullOrWhiteSpace(normalized))
            Current.Theme = AppTheme.Custom;

        Save();
        SettingChanged?.Invoke(this, nameof(AppSettings.Theme));
        SettingChanged?.Invoke(this, nameof(AppSettings.CustomThemePath));
    }

    public void ApplyThemeSelection(AppTheme theme, string? customThemePath)
    {
        theme = ThemeCatalog.Normalize(theme);
        var normalizedPath = customThemePath?.Trim() ?? string.Empty;

        var changed = Current.Theme != theme ||
                      !string.Equals(Current.CustomThemePath, normalizedPath, StringComparison.OrdinalIgnoreCase);
        if (!changed)
            return;

        Current.Theme = theme;
        Current.CustomThemePath = theme == AppTheme.Custom ? normalizedPath : string.Empty;
        Save();
        SettingChanged?.Invoke(this, nameof(AppSettings.Theme));
        if (theme == AppTheme.Custom)
            SettingChanged?.Invoke(this, nameof(AppSettings.CustomThemePath));
    }

    public void UpdateTimeFormat(TimeFormatMode timeFormat)
    {
        if (Current.TimeFormat == timeFormat)
            return;

        Current.TimeFormat = timeFormat;
        Save();
        SettingChanged?.Invoke(this, nameof(AppSettings.TimeFormat));
    }

    public void UpdateLanguage(string language)
    {
        if (string.Equals(Current.Language, language, StringComparison.OrdinalIgnoreCase))
            return;

        Current.Language = language;
        Save();
        SettingChanged?.Invoke(this, nameof(AppSettings.Language));
    }

    public void UpdateOpenRouterApiKey(string apiKey)
    {
        var normalized = apiKey.Trim();
        if (string.Equals(Current.OpenRouterApiKey, normalized, StringComparison.Ordinal))
            return;

        Current.OpenRouterApiKey = normalized;
        Save();
        SettingChanged?.Invoke(this, nameof(AppSettings.OpenRouterApiKey));
    }

    public void UpdateAiProvider(AiProvider provider)
    {
        provider = AiProviderCatalog.Normalize(provider);
        if (Current.AiProvider == provider)
            return;

        Current.AiProvider = provider;
        Current.PreferredAiModel = AiProviderCatalog.GetDefaultModel(provider);
        Current.LocalAiEndpoint = string.Empty;

        Save();
        SettingChanged?.Invoke(this, nameof(AppSettings.AiProvider));
        SettingChanged?.Invoke(this, nameof(AppSettings.PreferredAiModel));
    }

    public void UpdateLocalAiEndpoint(string endpoint)
    {
        var normalized = endpoint.Trim();
        if (string.Equals(Current.LocalAiEndpoint, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        Current.LocalAiEndpoint = normalized;
        Save();
        SettingChanged?.Invoke(this, nameof(AppSettings.LocalAiEndpoint));
    }

    public void UpdatePreferredAiModel(string model)
    {
        var normalized = model.Trim();
        if (string.Equals(Current.PreferredAiModel, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        Current.PreferredAiModel = normalized;
        Save();
        SettingChanged?.Invoke(this, nameof(AppSettings.PreferredAiModel));
    }

    public void UpdateNavigationHistory(IReadOnlyList<NavigationHistoryRecord> history)
    {
        Current.NavigationHistory = history.ToList();
        Save();
    }

    public void UpdateBookmarks(IReadOnlyList<BookmarkRecord> bookmarks)
    {
        Current.Bookmarks = bookmarks.ToList();
        Save();
    }

    public void UpdateScrollSensitivity(double sensitivity)
    {
        sensitivity = Math.Clamp(sensitivity, MinScrollSensitivity, MaxScrollSensitivity);
        if (Math.Abs(Current.ScrollSensitivity - sensitivity) < 0.001)
            return;

        Current.ScrollSensitivity = sensitivity;
        Save();
        SettingChanged?.Invoke(this, nameof(AppSettings.ScrollSensitivity));
    }

    public void UpdateFileIconStyle(FileIconStyle iconStyle)
    {
        if (Current.FileIconStyle == iconStyle)
            return;

        Current.FileIconStyle = iconStyle;
        Save();
        SettingChanged?.Invoke(this, nameof(AppSettings.FileIconStyle));
    }

    public void UpdateCustomIconPackPath(string path)
    {
        var normalized = path.Trim();
        if (string.Equals(Current.CustomIconPackPath, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        Current.CustomIconPackPath = normalized;
        Save();
        SettingChanged?.Invoke(this, nameof(AppSettings.CustomIconPackPath));
    }

    public void UpdateCustomFontPath(string path)
    {
        var normalized = path.Trim();
        if (string.Equals(Current.CustomFontPath, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        Current.CustomFontPath = normalized;
        Save();
        SettingChanged?.Invoke(this, nameof(AppSettings.CustomFontPath));
    }

    public void UpdateUiFontId(string fontId)
    {
        var normalized = string.IsNullOrWhiteSpace(fontId)
            ? UiFontIds.Default
            : fontId.Trim().ToLowerInvariant();

        if (string.Equals(Current.UiFontId, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        Current.UiFontId = normalized;
        Save();
        SettingChanged?.Invoke(this, nameof(AppSettings.UiFontId));
    }

    public void UpdateUseBuiltInMediaViewer(bool enabled)
    {
        if (Current.UseBuiltInMediaViewer == enabled)
            return;

        Current.UseBuiltInMediaViewer = enabled;
        Save();
        SettingChanged?.Invoke(this, nameof(AppSettings.UseBuiltInMediaViewer));
    }

    public void UpdateTerminalPreferences(double panelHeight, bool isOpen)
    {
        panelHeight = Math.Clamp(panelHeight, MinTerminalPanelHeight, MaxTerminalPanelHeight);

        if (Math.Abs(Current.TerminalPanelHeight - panelHeight) < 0.5 &&
            Current.IsTerminalOpen == isOpen)
        {
            return;
        }

        Current.TerminalPanelHeight = panelHeight;
        Current.IsTerminalOpen = isOpen;
        Save();
    }

    public void UpdateWindowSize(double width, double height)
    {
        width = Math.Clamp(width, MinWindowWidth, MaxWindowWidth);
        height = Math.Clamp(height, MinWindowHeight, MaxWindowHeight);

        if (Math.Abs(Current.WindowWidth - width) < 0.5 &&
            Math.Abs(Current.WindowHeight - height) < 0.5)
        {
            return;
        }

        Current.WindowWidth = width;
        Current.WindowHeight = height;
        Save();
    }

    public void PersistSession(
        double scrollSensitivity,
        double terminalPanelHeight,
        bool isTerminalOpen,
        double windowWidth,
        double windowHeight,
        int windowState)
    {
        Current.ScrollSensitivity = Math.Clamp(scrollSensitivity, MinScrollSensitivity, MaxScrollSensitivity);
        Current.TerminalPanelHeight = Math.Clamp(terminalPanelHeight, MinTerminalPanelHeight, MaxTerminalPanelHeight);
        Current.IsTerminalOpen = isTerminalOpen;
        Current.WindowWidth = Math.Clamp(windowWidth, MinWindowWidth, MaxWindowWidth);
        Current.WindowHeight = Math.Clamp(windowHeight, MinWindowHeight, MaxWindowHeight);
        Current.WindowState = Math.Clamp(windowState, 0, 2);
        Save();
    }

    public const double MinScrollSensitivity = 0.25;
    public const double MaxScrollSensitivity = 3.0;
    public const double DefaultScrollSensitivity = 1.0;

    public const double MinTerminalPanelHeight = 120;
    public const double MaxTerminalPanelHeight = 600;
    public const double DefaultTerminalPanelHeight = 220;

    public const double MinWindowWidth = 720;
    public const double MinWindowHeight = 420;
    public const double MaxWindowWidth = 3840;
    public const double MaxWindowHeight = 2160;

    public string FormatDateTime(DateTime dateTime) =>
        Current.TimeFormat == TimeFormatMode.Hour24
            ? dateTime.ToString("yyyy-MM-dd HH:mm")
            : dateTime.ToString("yyyy-MM-dd hh:mm tt");

    private static AppSettings CreateDefaultSettings() => new();

    private static void Normalize(AppSettings settings)
    {
        if (settings.SettingsVersion <= 0)
            settings.SettingsVersion = 1;

        if (settings.ScrollSensitivity <= 0)
            settings.ScrollSensitivity = DefaultScrollSensitivity;

        settings.ScrollSensitivity = Math.Clamp(
            settings.ScrollSensitivity,
            MinScrollSensitivity,
            MaxScrollSensitivity);

        if (settings.TerminalPanelHeight <= 0)
            settings.TerminalPanelHeight = DefaultTerminalPanelHeight;

        settings.TerminalPanelHeight = Math.Clamp(
            settings.TerminalPanelHeight,
            MinTerminalPanelHeight,
            MaxTerminalPanelHeight);

        if (settings.WindowWidth < MinWindowWidth)
            settings.WindowWidth = 1100;

        if (settings.WindowHeight < MinWindowHeight)
            settings.WindowHeight = 680;

        settings.WindowWidth = Math.Clamp(settings.WindowWidth, MinWindowWidth, MaxWindowWidth);
        settings.WindowHeight = Math.Clamp(settings.WindowHeight, MinWindowHeight, MaxWindowHeight);
        settings.WindowState = Math.Clamp(settings.WindowState, 0, 2);

        settings.NavigationHistory ??= [];

        if (!Enum.IsDefined(settings.FileIconStyle))
            settings.FileIconStyle = FileIconStyle.Windows;

        settings.CustomIconPackPath ??= string.Empty;
        settings.CustomFontPath ??= string.Empty;
        settings.CustomThemePath ??= string.Empty;
        settings.UiFontId = BuiltInFontCatalog.NormalizeFontId(settings.UiFontId, settings.CustomFontPath);
        settings.Theme = ThemeCatalog.Normalize(settings.Theme);
        if (settings.Theme == AppTheme.Custom && string.IsNullOrWhiteSpace(settings.CustomThemePath))
            settings.Theme = AppTheme.Dark;

        AiProviderCatalog.NormalizeSettings(settings);
    }
}
