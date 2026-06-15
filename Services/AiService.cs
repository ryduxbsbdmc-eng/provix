using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using FileExplorer.Models;

namespace FileExplorer.Services;

public sealed class AiService
{
    private const string OpenRouterEndpoint = "https://openrouter.ai/api/v1/chat/completions";
    private const int RequestTimeoutSeconds = 90;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions CommandParseOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly HttpClient _httpClient;

    public AiService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(RequestTimeoutSeconds)
        };
    }

    public async Task<AiGenerationResult> GenerateCommandsAsync(
        string directoryPath,
        IReadOnlyList<FileSystemEntry> entries,
        string userQuery,
        CancellationToken cancellationToken = default)
    {
        var settings = SettingsManager.Instance.Current;
        var apiKey = settings.OpenRouterApiKey?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(apiKey))
            return AiGenerationResult.Fail("OpenRouter API key is not configured. Add it in Settings.");

        if (string.IsNullOrWhiteSpace(userQuery))
            return AiGenerationResult.Fail("Please enter a request.");

        var model = string.IsNullOrWhiteSpace(settings.PreferredAiModel)
            ? "openai/gpt-4o-mini"
            : settings.PreferredAiModel.Trim();

        var fileContext = BuildFileContext(directoryPath, entries);
        var systemPrompt = BuildSystemPrompt(directoryPath);
        var userMessage = $"Current directory: {directoryPath}\n\nFiles:\n{fileContext}\n\nUser request: {userQuery.Trim()}";

        try
        {
            var requestBody = new OpenRouterChatRequest
            {
                Model = model,
                Messages =
                [
                    new OpenRouterMessage { Role = "system", Content = systemPrompt },
                    new OpenRouterMessage { Role = "user", Content = userMessage }
                ]
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, OpenRouterEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/fileexplorer");
            request.Headers.TryAddWithoutValidation("X-Title", "provix");

            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var detail = TryExtractApiError(responseBody) ?? response.ReasonPhrase ?? "Unknown error";
                return AiGenerationResult.Fail($"OpenRouter API error ({(int)response.StatusCode}): {detail}");
            }

            var chatResponse = JsonSerializer.Deserialize<OpenRouterChatResponse>(responseBody, JsonOptions);
            var content = chatResponse?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();

            if (string.IsNullOrEmpty(content))
                return AiGenerationResult.Fail("The AI returned an empty response.");

            var commands = ParseCommandsFromContent(content);
            if (commands.Count == 0)
                return AiGenerationResult.Fail("The AI did not return any file operations.");

            return AiGenerationResult.Ok(commands);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AiGenerationResult.Fail("The AI request timed out. Try again or use a faster model.");
        }
        catch (HttpRequestException ex)
        {
            return AiGenerationResult.Fail($"Network error: {ex.Message}");
        }
        catch (JsonException ex)
        {
            return AiGenerationResult.Fail($"Invalid JSON from AI: {ex.Message}");
        }
        catch (Exception ex)
        {
            return AiGenerationResult.Fail(ex.Message);
        }
    }

    private static string BuildSystemPrompt(string directoryPath)
    {
        return "You are a file management assistant. Given a list of files in a directory, generate a JSON array of operations to satisfy the user's request.\n" +
            $"All paths must be relative to the current directory: {directoryPath}\n" +
            "Use forward slashes in paths (e.g. \"images/photo.png\").\n" +
            "Only operate on files listed in the context. Do not invent files that are not present.\n" +
            "Supported operations:\n" +
            "- MKDIR: create a folder. Fields: \"op\": \"MKDIR\", \"path\": \"folder_name\"\n" +
            "- MOVE: move a file or folder. Fields: \"op\": \"MOVE\", \"src\": \"source_name\", \"dest\": \"destination/path\"\n" +
            "- RENAME: rename a file or folder in place. Fields: \"op\": \"RENAME\", \"src\": \"old_name\", \"dest\": \"new_name\"\n" +
            "Return ONLY a valid JSON array with no markdown, no explanation, no code fences.\n" +
            "Example: [{\"op\": \"MKDIR\", \"path\": \"A\"}, {\"op\": \"MOVE\", \"src\": \"apple.txt\", \"dest\": \"A/apple.txt\"}]\n" +
            "If no operations are needed, return an empty array: []";
    }

    private static string BuildFileContext(string directoryPath, IReadOnlyList<FileSystemEntry> entries)
    {
        if (entries.Count == 0)
            return "(empty directory)";

        var lines = new List<string>(entries.Count);

        foreach (var entry in entries)
        {
            var extension = entry.IsDirectory
                ? "(folder)"
                : Path.GetExtension(entry.Name);

            if (string.IsNullOrEmpty(extension) && !entry.IsDirectory)
                extension = "(no extension)";

            var size = entry.IsDirectory ? "-" : entry.Size.ToString();
            var date = entry.DateModified.ToString("yyyy-MM-dd HH:mm");
            lines.Add($"- {entry.Name} | ext: {extension} | size: {size} | modified: {date}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    internal static List<AiCommand> ParseCommandsFromContent(string content)
    {
        var json = ExtractJsonArray(content);

        var commands = JsonSerializer.Deserialize<List<AiCommand>>(json, CommandParseOptions)
            ?? [];

        return commands
            .Where(command => !string.IsNullOrWhiteSpace(command.Op))
            .ToList();
    }

    private static string ExtractJsonArray(string content)
    {
        var trimmed = content.Trim();

        var fenceMatch = Regex.Match(
            trimmed,
            @"```(?:json)?\s*(\[[\s\S]*?\])\s*```",
            RegexOptions.IgnoreCase);

        if (fenceMatch.Success)
            return fenceMatch.Groups[1].Value.Trim();

        var start = trimmed.IndexOf('[');
        var end = trimmed.LastIndexOf(']');

        if (start >= 0 && end > start)
            return trimmed[start..(end + 1)];

        return trimmed;
    }

    private static string? TryExtractApiError(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                if (error.TryGetProperty("message", out var message))
                    return message.GetString();

                return error.ToString();
            }
        }
        catch
        {
            // Ignore parse errors; fall back to raw body snippet.
        }

        return responseBody.Length > 200 ? responseBody[..200] + "…" : responseBody;
    }

    private sealed class OpenRouterChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<OpenRouterMessage> Messages { get; set; } = [];
    }

    private sealed class OpenRouterMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class OpenRouterChatResponse
    {
        [JsonPropertyName("choices")]
        public List<OpenRouterChoice>? Choices { get; set; }
    }

    private sealed class OpenRouterChoice
    {
        [JsonPropertyName("message")]
        public OpenRouterMessage? Message { get; set; }
    }
}
