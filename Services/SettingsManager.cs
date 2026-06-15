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

    public event EventHandler<string>? SettingChanged;

    public void Load()
    {
        if (!File.Exists(_settingsPath))
        {
            Current = new AppSettings();
            Save();
            return;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Current, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    public void UpdateTheme(AppTheme theme)
    {
        if (Current.Theme == theme)
            return;

        Current.Theme = theme;
        Save();
        SettingChanged?.Invoke(this, nameof(AppSettings.Theme));
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

    public void UpdatePreferredAiModel(string model)
    {
        var normalized = model.Trim();
        if (string.Equals(Current.PreferredAiModel, normalized, StringComparison.OrdinalIgnoreCase))
            return;

        Current.PreferredAiModel = normalized;
        Save();
        SettingChanged?.Invoke(this, nameof(AppSettings.PreferredAiModel));
    }

    public string FormatDateTime(DateTime dateTime) =>
        Current.TimeFormat == TimeFormatMode.Hour24
            ? dateTime.ToString("yyyy-MM-dd HH:mm")
            : dateTime.ToString("yyyy-MM-dd hh:mm tt");
}
