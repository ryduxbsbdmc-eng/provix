using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace FileExplorer.Services;

public sealed class LocalizationManager : INotifyPropertyChanged
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Lazy<LocalizationManager> LazyInstance = new(() => new LocalizationManager());

    private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);

    private LocalizationManager()
    {
    }

    public static LocalizationManager Instance => LazyInstance.Value;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key))
                return string.Empty;

            return _strings.TryGetValue(key, out var value) ? value : key;
        }
    }

    public IReadOnlyList<LocaleInfo> GetAvailableLocales() =>
        ScanLocalesDirectory()
            .OrderBy(locale => locale.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public void LoadLanguage(string languageCode)
    {
        var localesRoot = GetLocalesDirectory();
        var filePath = Path.Combine(localesRoot, $"{languageCode}.json");

        _strings.Clear();

        if (!File.Exists(filePath))
        {
            var fallback = Path.Combine(localesRoot, "en-US.json");
            if (File.Exists(fallback))
                filePath = fallback;
            else
            {
                NotifyAll();
                return;
            }
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            if (parsed is not null)
            {
                foreach (var pair in parsed)
                    _strings[pair.Key] = pair.Value;
            }
        }
        catch
        {
            // Keep whatever strings were already loaded.
        }

        NotifyAll();
    }

    public static IReadOnlyList<LocaleInfo> ScanLocalesDirectory()
    {
        var localesRoot = GetLocalesDirectory();
        if (!Directory.Exists(localesRoot))
            return [];

        return Directory
            .EnumerateFiles(localesRoot, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                var code = Path.GetFileNameWithoutExtension(path);
                var displayName = ResolveDisplayName(code);
                return new LocaleInfo(code, displayName);
            })
            .ToList();
    }

    private static string GetLocalesDirectory() =>
        Path.Combine(AppContext.BaseDirectory, "Locales");

    private static string ResolveDisplayName(string code) =>
        code.ToLowerInvariant() switch
        {
            "en-us" => "English (US)",
            "ru-ru" => "Русский",
            _ => code
        };

    private void NotifyAll() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

public sealed record LocaleInfo(string Code, string DisplayName);
