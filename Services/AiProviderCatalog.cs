using FileExplorer.Models;

namespace FileExplorer.Services;

public static class AiProviderCatalog
{
    public const string DefaultOllamaEndpoint = "http://127.0.0.1:11434/v1/chat/completions";
    public const string DefaultLmStudioEndpoint = "http://127.0.0.1:1234/v1/chat/completions";

    public static string GetLabelKey(AiProvider provider) =>
        provider switch
        {
            AiProvider.Ollama => "UI_AiProviderOllama",
            AiProvider.LmStudio => "UI_AiProviderLmStudio",
            _ => "UI_AiProviderOpenRouter"
        };

    public static string GetDefaultModel(AiProvider provider) =>
        provider switch
        {
            AiProvider.Ollama => "llama3.2",
            AiProvider.LmStudio => "local-model",
            _ => "openai/gpt-4o-mini"
        };

    public static string GetDefaultEndpoint(AiProvider provider) =>
        provider switch
        {
            AiProvider.Ollama => DefaultOllamaEndpoint,
            AiProvider.LmStudio => DefaultLmStudioEndpoint,
            _ => string.Empty
        };

    public static string GetModelHintKey(AiProvider provider) =>
        provider switch
        {
            AiProvider.Ollama => "UI_AiModelHintOllama",
            AiProvider.LmStudio => "UI_AiModelHintLmStudio",
            _ => "UI_AiModelHintOpenRouter"
        };

    public static AiProvider Normalize(AiProvider provider) =>
        Enum.IsDefined(provider) ? provider : AiProvider.OpenRouter;

    public static bool IsConfigured(AppSettings settings)
    {
        settings = NormalizeSettings(settings);

        return settings.AiProvider switch
        {
            AiProvider.OpenRouter => !string.IsNullOrWhiteSpace(settings.OpenRouterApiKey),
            AiProvider.Ollama or AiProvider.LmStudio =>
                !string.IsNullOrWhiteSpace(ResolveEndpoint(settings)) &&
                !string.IsNullOrWhiteSpace(ResolveModel(settings)),
            _ => false
        };
    }

    public static AppSettings NormalizeSettings(AppSettings settings)
    {
        settings.AiProvider = Normalize(settings.AiProvider);
        settings.LocalAiEndpoint ??= string.Empty;
        settings.OpenRouterApiKey ??= string.Empty;

        if (string.IsNullOrWhiteSpace(settings.PreferredAiModel))
            settings.PreferredAiModel = GetDefaultModel(settings.AiProvider);

        return settings;
    }

    public static string ResolveEndpoint(AppSettings settings)
    {
        settings = NormalizeSettings(settings);

        return settings.AiProvider switch
        {
            AiProvider.Ollama => string.IsNullOrWhiteSpace(settings.LocalAiEndpoint)
                ? DefaultOllamaEndpoint
                : settings.LocalAiEndpoint.Trim().TrimEnd('/'),
            AiProvider.LmStudio => string.IsNullOrWhiteSpace(settings.LocalAiEndpoint)
                ? DefaultLmStudioEndpoint
                : settings.LocalAiEndpoint.Trim().TrimEnd('/'),
            _ => "https://openrouter.ai/api/v1/chat/completions"
        };
    }

    public static string ResolveModel(AppSettings settings) =>
        string.IsNullOrWhiteSpace(settings.PreferredAiModel)
            ? GetDefaultModel(Normalize(settings.AiProvider))
            : settings.PreferredAiModel.Trim();

    public static string GetProviderDisplayName(AiProvider provider, LocalizationManager loc) =>
        loc[GetLabelKey(provider)];

    public static string GetRequestErrorPrefix(AiProvider provider) =>
        provider switch
        {
            AiProvider.Ollama => "Ollama",
            AiProvider.LmStudio => "LM Studio",
            _ => "OpenRouter"
        };

    public static string GetNotConfiguredMessage(AiProvider provider, LocalizationManager loc) =>
        provider switch
        {
            AiProvider.Ollama => loc["UI_AiNotConfiguredOllama"],
            AiProvider.LmStudio => loc["UI_AiNotConfiguredLmStudio"],
            _ => loc["UI_AiNotConfiguredOpenRouter"]
        };

    public static string GetConnectionHint(AiProvider provider, LocalizationManager loc) =>
        provider switch
        {
            AiProvider.Ollama => loc["UI_AiConnectionHintOllama"],
            AiProvider.LmStudio => loc["UI_AiConnectionHintLmStudio"],
            _ => loc["UI_AiApiKeyHint"]
        };
}
