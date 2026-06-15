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
    private const int RequestTimeoutSeconds = 120;

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
        var settings = AiProviderCatalog.NormalizeSettings(SettingsManager.Instance.Current);
        var loc = LocalizationManager.Instance;

        if (!AiProviderCatalog.IsConfigured(settings))
            return AiGenerationResult.Fail(AiProviderCatalog.GetNotConfiguredMessage(settings.AiProvider, loc));

        if (string.IsNullOrWhiteSpace(userQuery))
            return AiGenerationResult.Fail("Please enter a request.");

        var endpoint = AiProviderCatalog.ResolveEndpoint(settings);
        var model = AiProviderCatalog.ResolveModel(settings);
        var provider = settings.AiProvider;

        var gitContext = await BuildGitContextAsync(directoryPath, cancellationToken).ConfigureAwait(false);
        var fileContext = BuildFileContext(directoryPath, entries);
        var changedFileContext = await BuildChangedFilePreviewsAsync(directoryPath, cancellationToken)
            .ConfigureAwait(false);
        var systemPrompt = BuildSystemPrompt(directoryPath);
        var userMessage =
            $"Current directory: {directoryPath}\n\n" +
            $"{gitContext}\n\n" +
            $"Files in current directory:\n{fileContext}\n\n" +
            (string.IsNullOrEmpty(changedFileContext)
                ? string.Empty
                : $"Changed file previews (repo-wide):\n{changedFileContext}\n\n") +
            $"User request: {userQuery.Trim()}";

        try
        {
            var requestBody = new ChatCompletionRequest
            {
                Model = model,
                Messages =
                [
                    new ChatMessage { Role = "system", Content = systemPrompt },
                    new ChatMessage { Role = "user", Content = userMessage }
                ]
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            ApplyAuthorization(request, settings);

            if (provider == AiProvider.OpenRouter)
            {
                request.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/fileexplorer");
                request.Headers.TryAddWithoutValidation("X-Title", "provix");
            }

            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var detail = TryExtractApiError(responseBody) ?? response.ReasonPhrase ?? "Unknown error";
                var prefix = AiProviderCatalog.GetRequestErrorPrefix(provider);
                return AiGenerationResult.Fail($"{prefix} error ({(int)response.StatusCode}): {detail}");
            }

            var chatResponse = JsonSerializer.Deserialize<ChatCompletionResponse>(responseBody, JsonOptions);
            var content = chatResponse?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();

            if (string.IsNullOrEmpty(content))
                return AiGenerationResult.Fail("The AI returned an empty response.");

            var commands = ParseCommandsFromContent(content);
            if (commands.Count == 0)
                return AiGenerationResult.Fail("The AI did not return any operations.");

            return AiGenerationResult.Ok(commands);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return AiGenerationResult.Fail("The AI request timed out. Try again or use a faster model.");
        }
        catch (HttpRequestException ex)
        {
            var hint = provider is AiProvider.Ollama or AiProvider.LmStudio
                ? $" {AiProviderCatalog.GetConnectionHint(provider, loc)}"
                : string.Empty;
            return AiGenerationResult.Fail($"Network error: {ex.Message}.{hint}");
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

    private static void ApplyAuthorization(HttpRequestMessage request, AppSettings settings)
    {
        if (settings.AiProvider != AiProvider.OpenRouter)
            return;

        var apiKey = settings.OpenRouterApiKey.Trim();
        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    private static string BuildSystemPrompt(string directoryPath)
    {
        var gitAvailable = ExternalToolsService.IsAvailable(ExternalTool.Git);
        var gitOperations = gitAvailable
            ? "- GIT_COMMIT: stage all changes in the repository (git add .) and commit. Fields: \"op\": \"GIT_COMMIT\", \"message\": \"commit message\"\n" +
              "Rules for GIT_COMMIT:\n" +
              "- Only use when the folder is inside a git repository (see Git context).\n" +
              "- Put GIT_COMMIT last, after any file changes (WRITE, MOVE, RENAME, DELETE, MKDIR).\n" +
              "- Write a clear, concise commit message in the user's language when possible.\n" +
              "- If the working tree is already clean and no file changes are needed, you may still return GIT_COMMIT only when the user explicitly asks to commit; otherwise return [].\n"
            : string.Empty;

        var example = gitAvailable
            ? "Example: [{\"op\": \"WRITE\", \"path\": \"notes.txt\", \"content\": \"Hello\"}, {\"op\": \"GIT_COMMIT\", \"message\": \"Add notes file\"}]\n"
            : "Example: [{\"op\": \"WRITE\", \"path\": \"notes.txt\", \"content\": \"Hello\"}]\n";

        return "You are a file and git assistant for a Windows file manager. Given directory context and optional git status, generate a JSON array of operations to satisfy the user's request.\n" +
            $"All file paths must be relative to the current directory: {directoryPath}\n" +
            "Use forward slashes in paths (e.g. \"src/App.cs\").\n" +
            "Prefer operating on files listed in the context. For WRITE you may create new files under the current directory.\n" +
            "Supported operations:\n" +
            "- MKDIR: create a folder. Fields: \"op\": \"MKDIR\", \"path\": \"folder_name\"\n" +
            "- MOVE: move a file or folder. Fields: \"op\": \"MOVE\", \"src\": \"source\", \"dest\": \"destination/path\"\n" +
            "- RENAME: rename in place. Fields: \"op\": \"RENAME\", \"src\": \"old_name\", \"dest\": \"new_name\"\n" +
            "- WRITE: create or overwrite a text file. Fields: \"op\": \"WRITE\", \"path\": \"file.txt\", \"content\": \"file body\"\n" +
            "- DELETE: delete a file or empty folder. Fields: \"op\": \"DELETE\", \"path\": \"file.txt\"\n" +
            gitOperations +
            "Return ONLY a valid JSON array with no markdown, no explanation, no code fences.\n" +
            example +
            "If no operations are needed, return an empty array: []";
    }

    private static async Task<string> BuildGitContextAsync(string directoryPath, CancellationToken cancellationToken)
    {
        if (!ExternalToolsService.IsAvailable(ExternalTool.Git))
            return "Git: not available on this system.";

        var repositoryRoot = GitRepositoryHelper.GetGitRepositoryRoot(directoryPath);
        if (repositoryRoot is null)
            return "Git: not a repository (git commit unavailable).";

        cancellationToken.ThrowIfCancellationRequested();

        var status = await GitService.GetWorkingTreeStatusAsync(repositoryRoot).ConfigureAwait(false);
        if (!status.Success)
            return $"Git: unable to read status ({status.Message}).";

        var lines = new List<string>
        {
            $"Git repository root: {repositoryRoot}",
            $"Branch: {(string.IsNullOrWhiteSpace(status.BranchName) ? "(detached or unknown)" : status.BranchName)}",
            $"Changed files: {status.ChangedFileCount}"
        };

        if (status.IsClean)
        {
            lines.Add("Working tree: clean.");
            return string.Join(Environment.NewLine, lines);
        }

        lines.Add("Working tree changes:");
        foreach (var change in status.Changes)
            lines.Add($"- [{change.Status}] {change.FilePath}");

        return string.Join(Environment.NewLine, lines);
    }

    private const int MaxPreviewFiles = 8;
    private const int MaxPreviewCharsPerFile = 3000;

    private static async Task<string> BuildChangedFilePreviewsAsync(
        string directoryPath,
        CancellationToken cancellationToken)
    {
        if (!ExternalToolsService.IsAvailable(ExternalTool.Git))
            return string.Empty;

        var repositoryRoot = GitRepositoryHelper.GetGitRepositoryRoot(directoryPath);
        if (repositoryRoot is null)
            return string.Empty;

        cancellationToken.ThrowIfCancellationRequested();

        var status = await GitService.GetWorkingTreeStatusAsync(repositoryRoot).ConfigureAwait(false);
        if (!status.Success || status.Changes.Count == 0)
            return string.Empty;

        var previews = new List<string>();
        var repoRootNormalized = Path.GetFullPath(repositoryRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var change in status.Changes)
        {
            if (previews.Count >= MaxPreviewFiles)
                break;

            if (change.Status is GitFileStatusType.Deleted or GitFileStatusType.Ignored or GitFileStatusType.Unchanged)
                continue;

            var absolutePath = Path.GetFullPath(Path.Combine(repoRootNormalized, change.FilePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(absolutePath) || !TextFileHelper.IsEditableTextFile(absolutePath))
                continue;

            try
            {
                var info = new FileInfo(absolutePath);
                if (info.Length > TextFileHelper.MaxInAppEditorBytes)
                    continue;

                cancellationToken.ThrowIfCancellationRequested();

                var text = await File.ReadAllTextAsync(absolutePath, cancellationToken).ConfigureAwait(false);
                if (text.Length > MaxPreviewCharsPerFile)
                    text = text[..MaxPreviewCharsPerFile] + "\n…(truncated)";

                previews.Add($"--- {change.FilePath} [{change.Status}] ---\n{text}");
            }
            catch
            {
                // Skip unreadable files.
            }
        }

        return previews.Count == 0 ? string.Empty : string.Join(Environment.NewLine + Environment.NewLine, previews);
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
                if (error.ValueKind == JsonValueKind.Object &&
                    error.TryGetProperty("message", out var message))
                {
                    return message.GetString();
                }

                if (error.ValueKind == JsonValueKind.String)
                    return error.GetString();

                return error.ToString();
            }
        }
        catch
        {
            // Ignore parse errors; fall back to raw body snippet.
        }

        return responseBody.Length > 200 ? responseBody[..200] + "…" : responseBody;
    }

    private sealed class ChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; set; } = [];
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<ChatChoice>? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }
    }
}
