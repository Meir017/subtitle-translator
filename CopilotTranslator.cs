using System.Text.Json;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Logging;

public class CopilotTranslator : ITranslator, IDisposable
{
    private readonly CopilotClient _client;
    private CopilotSession? _session;
    private readonly ILogger<CopilotTranslator> _logger;
    private int _messagesSent;
    private readonly int _maxMessagesPerSession = 5;
    private bool _disposed = false;

    // Accept shared CopilotClient via DI so there is a single client across the app
    public CopilotTranslator(CopilotClient client, ILogger<CopilotTranslator> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger.LogInformation("Initializing Copilot translator and creating session...");
    }

    private async Task CreateNewSessionAsync()
    {
        try
        {
            if (_session is not null)
            {
                await _session.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error disposing old session (ignored)");
        }
        _session = await _client.CreateSessionAsync(new SessionConfig { Model = "gpt-4.1", Streaming = false });
        _messagesSent = 0;
        _logger.LogDebug("Created new Copilot session.");
    }

    public async Task<string> TranslateAsync(string text, string targetLanguage)
    {
        if (_session is null)
        {
            await CreateNewSessionAsync();
        }


        _logger.LogDebug("Sending text to Copilot for translation to {lang}", targetLanguage);
        var prompt = $"Translate the following subtitle text to {targetLanguage}. Respond only with the translated text (no extra commentary):\n\n{text}";

        // Exponential retry with increasing timeouts
        int maxRetries = 5;
        TimeSpan initialTimeout = TimeSpan.FromSeconds(10);

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // Exponentially increase timeout: 10s, 20s, 40s, 80s, 160s
                TimeSpan timeout = TimeSpan.FromSeconds(initialTimeout.TotalSeconds * Math.Pow(2, attempt));
                _logger.LogDebug("Translation attempt {attempt} with timeout {timeoutSeconds}s", attempt + 1, timeout.TotalSeconds);

                var response = await _session!.SendAndWaitAsync(new MessageOptions { Prompt = prompt }, timeout);
                _messagesSent++;
                if (_messagesSent >= _maxMessagesPerSession)
                {
                    _logger.LogDebug("Reached {maxMessagesPerSession} messages, rotating Copilot session...", _maxMessagesPerSession);
                    await CreateNewSessionAsync();
                }
                var outText = response?.Data?.Content ?? text;
                _logger.LogDebug("Received translation result (length={len})", outText?.Length ?? 0);
                return outText!;
            }
            catch (TimeoutException ex) when (attempt < maxRetries - 1)
            {
                _logger.LogWarning(ex, "Translation attempt {attempt} timed out, retrying with increased timeout...", attempt + 1);
                // Create a new session for retry to ensure a fresh connection
                await CreateNewSessionAsync();
            }
            catch (TimeoutException ex) when (attempt == maxRetries - 1)
            {
                _logger.LogError(ex, "Translation failed after {maxRetries} attempts with exponential backoff", maxRetries);
                throw;
            }
        }

        return text;
    }

    public async Task<List<string>> TranslateBulkAsync(List<string> texts, string targetLanguage)
    {
        if (_session == null)
        {
            _logger.LogInformation("No Copilot session available; creating one...");
            await CreateNewSessionAsync();
        }

        if (texts.Count == 0)
        {
            return texts;
        }

        _logger.LogDebug("Sending {count} subtitle entries to Copilot for bulk translation to {lang}", texts.Count, targetLanguage);

        // Build a prompt with numbered sentences
        var promptBuilder = new System.Text.StringBuilder();
        promptBuilder.AppendLine($"Translate the following {texts.Count} subtitle entries to {targetLanguage}.");
        promptBuilder.AppendLine("IMPORTANT: Respond ONLY with a valid JSON array, no additional text before or after.");
        promptBuilder.AppendLine("The JSON must be valid and properly formatted. Use this exact structure:");
        promptBuilder.AppendLine("[");
        promptBuilder.AppendLine("  {\"index\": 1, \"translated\": \"<translated text 1>\"},");
        promptBuilder.AppendLine("  {\"index\": 2, \"translated\": \"<translated text 2>\"},");
        promptBuilder.AppendLine("  {\"index\": 3, \"translated\": \"<translated text 3>\"}");
        promptBuilder.AppendLine("]");
        promptBuilder.AppendLine();
        promptBuilder.AppendLine("Subtitle entries to translate:");
        promptBuilder.AppendLine();

        for (int i = 0; i < texts.Count; i++)
        {
            promptBuilder.AppendLine($"{i + 1}. {texts[i]}");
        }

        var prompt = promptBuilder.ToString();

        // Exponential retry with increasing timeouts
        int maxRetries = 5;
        TimeSpan initialTimeout = TimeSpan.FromSeconds(15);

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                TimeSpan timeout = TimeSpan.FromSeconds(initialTimeout.TotalSeconds * Math.Pow(2, attempt));
                _logger.LogDebug("Bulk translation attempt {attempt} with timeout {timeoutSeconds}s", attempt + 1, timeout.TotalSeconds);

                var response = await _session.SendAndWaitAsync(new MessageOptions { Prompt = prompt }, timeout);
                _messagesSent++;
                if (_messagesSent >= _maxMessagesPerSession)
                {
                    _logger.LogInformation("Reached {maxMessagesPerSession} messages, rotating Copilot session...", _maxMessagesPerSession);
                    await CreateNewSessionAsync();
                }

                var responseText = response?.Data?.Content ?? "";
                _logger.LogDebug("Received bulk translation response (length={len})", responseText.Length);

                // Parse JSON response
                var result = ParseBulkTranslationResponse(responseText, texts.Count);
                if (result.Count > 0)
                {
                    return result;
                }
            }
            catch (TimeoutException ex) when (attempt < maxRetries - 1)
            {
                _logger.LogWarning(ex, "Bulk translation attempt {attempt} timed out, retrying with increased timeout...", attempt + 1);
                await CreateNewSessionAsync();
            }
            catch (TimeoutException ex) when (attempt == maxRetries - 1)
            {
                _logger.LogError(ex, "Bulk translation failed after {maxRetries} attempts with exponential backoff", maxRetries);
                throw;
            }
            catch (System.Text.Json.JsonException ex) when (attempt < maxRetries - 1)
            {
                _logger.LogWarning(ex, "Bulk translation attempt {attempt} - failed to parse JSON response, retrying...", attempt + 1);
                await CreateNewSessionAsync();
            }
            catch (System.Text.Json.JsonException ex) when (attempt == maxRetries - 1)
            {
                _logger.LogError(ex, "Bulk translation failed - invalid JSON response after {maxRetries} retry attempts", maxRetries);
                return texts;
            }
            catch (IOException ex) when (ex.Message.Contains("Session not found") && attempt < maxRetries - 1)
            {
                _logger.LogWarning(ex, "Bulk translation attempt {attempt} - session not found, creating new session and retrying...", attempt + 1);
                await CreateNewSessionAsync();
            }
            catch (IOException ex) when (ex.Message.Contains("Session not found") && attempt == maxRetries - 1)
            {
                _logger.LogError(ex, "Bulk translation failed - session not found after {maxRetries} retry attempts", maxRetries);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error parsing bulk translation response, returning original texts");
                return texts;
            }
        }

        return texts;
    }

    private List<string> ParseBulkTranslationResponse(string jsonResponse, int expectedCount)
    {
        try
        {
            // Try to extract JSON array from response (in case there's extra text)
            var jsonStart = jsonResponse.IndexOf('[');
            var jsonEnd = jsonResponse.LastIndexOf(']');

            if (jsonStart < 0 || jsonEnd < 0)
            {
                _logger.LogWarning("Could not find JSON array in response");
                return new List<string>();
            }

            var jsonStr = jsonResponse.Substring(jsonStart, jsonEnd - jsonStart + 1);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var items = JsonSerializer.Deserialize<List<JsonElement>>(jsonStr, options);

            if (items == null || items.Count == 0)
            {
                _logger.LogWarning("Parsed JSON array is empty");
                return new List<string>();
            }

            var result = new List<string>(expectedCount);
            var itemsByIndex = new Dictionary<int, string>();

            foreach (var item in items)
            {
                if (item.TryGetProperty("index", out var indexElement) &&
                    item.TryGetProperty("translated", out var translatedElement))
                {
                    var index = indexElement.GetInt32();
                    var translated = translatedElement.GetString() ?? "";
                    itemsByIndex[index] = translated;
                }
            }

            // Reconstruct in order
            for (int i = 1; i <= expectedCount; i++)
            {
                if (itemsByIndex.TryGetValue(i, out var translation))
                {
                    result.Add(translation);
                }
                else
                {
                    _logger.LogWarning("Missing translation for index {index}", i);
                    result.Add(""); // Empty fallback
                }
            }

            _logger.LogDebug("Successfully parsed {count} bulk translations", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse bulk translation JSON response");
            return new List<string>();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogInformation("Disposing Copilot session and client...");
        if (_session != null)
        {
            _session.DisposeAsync().GetAwaiter().GetResult();
        }
        _client?.Dispose();
        _logger.LogInformation("Disposed Copilot resources.");
    }
}
