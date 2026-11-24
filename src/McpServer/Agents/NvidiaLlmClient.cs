using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using McpServer.ErrorHandling;
using Microsoft.Extensions.Options;

namespace McpServer.Agents;

/// <summary>
/// Resilient HTTP client for interacting with the NVIDIA NIM API for LLM chat reasoning and vector embeddings.
/// Includes automatic fallback to secondary models on 429 rate limits and error mapping to RFC 7807 domain exceptions.
/// </summary>
public sealed class NvidiaLlmClient
{
    private readonly HttpClient _httpClient;
    private readonly NvidiaConfig _config;
    private readonly ILogger<NvidiaLlmClient> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public NvidiaLlmClient(
        HttpClient httpClient,
        IOptions<NvidiaConfig> config,
        ILogger<NvidiaLlmClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (!string.IsNullOrWhiteSpace(_config.BaseUrl) && _httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(_config.BaseUrl.TrimEnd('/') + "/");
        }

        if (!string.IsNullOrWhiteSpace(_config.ApiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        }
    }

    /// <summary>
    /// Executes a chat completion prompt against NVIDIA NIM with automatic model fallback on rate limits.
    /// </summary>
    public async Task<string> ChatAsync(
        string systemPrompt,
        string userMessage,
        string? model = null,
        CancellationToken ct = default)
    {
        var messages = new List<ChatMessageDto>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            messages.Add(new ChatMessageDto { Role = "system", Content = systemPrompt });
        }
        messages.Add(new ChatMessageDto { Role = "user", Content = userMessage });

        return await ChatMessagesAsync(messages, model, ct);
    }

    /// <summary>
    /// Executes a multi-turn chat completion conversation against NVIDIA NIM.
    /// </summary>
    public async Task<string> ChatMessagesAsync(
        IEnumerable<ChatMessageDto> messages,
        string? model = null,
        CancellationToken ct = default)
    {
        var targetModel = model ?? _config.ChatModel;

        var requestPayload = new ChatCompletionRequest
        {
            Model = targetModel,
            Messages = messages.ToList(),
            MaxTokens = _config.MaxTokens,
            Temperature = _config.Temperature
        };

        var response = await ExecuteChatRequestWithFallbackAsync(requestPayload, ct);
        var choice = response.Choices.FirstOrDefault();

        if (choice == null)
        {
            throw new LlmProviderException($"NVIDIA NIM model '{targetModel}' returned an empty choice set.");
        }

        // Return content or fallback to reasoning_content if content is empty
        var output = choice.Message.Content;
        if (string.IsNullOrWhiteSpace(output) && !string.IsNullOrWhiteSpace(choice.Message.ReasoningContent))
        {
            output = choice.Message.ReasoningContent;
        }

        return output ?? string.Empty;
    }

    /// <summary>
    /// Generates high-dimensional embeddings for a batch of text segments using NVIDIA NIM embedding models.
    /// </summary>
    public async Task<float[][]> EmbedAsync(
        IReadOnlyList<string> texts,
        string inputType = "query",
        CancellationToken ct = default)
    {
        if (texts == null || texts.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        var requestPayload = new EmbeddingRequest
        {
            Model = _config.EmbeddingModel,
            Input = texts.ToList(),
            InputType = inputType,
            Dimensions = _config.EmbeddingDimensions
        };

        var json = JsonSerializer.Serialize(requestPayload, JsonOptions);
        using var requestContent = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _httpClient.PostAsync("embeddings", requestContent, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to connect to NVIDIA NIM embeddings endpoint.");
            throw new LlmProviderException($"Network failure contacting NVIDIA NIM embeddings: {ex.Message}", ex);
        }

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorBody = await httpResponse.Content.ReadAsStringAsync(ct);
            _logger.LogError("NVIDIA NIM embeddings returned HTTP {StatusCode}: {ErrorBody}",
                (int)httpResponse.StatusCode, errorBody);

            throw new LlmProviderException(
                $"NVIDIA NIM embeddings returned HTTP {(int)httpResponse.StatusCode}: {errorBody}");
        }

        var responseStream = await httpResponse.Content.ReadAsStreamAsync(ct);
        var result = await JsonSerializer.DeserializeAsync<EmbeddingResponse>(responseStream, JsonOptions, ct);

        if (result == null || result.Data.Count == 0)
        {
            throw new LlmProviderException("NVIDIA NIM embeddings returned null or empty payload.");
        }

        return result.Data
            .OrderBy(d => d.Index)
            .Select(d => d.Embedding)
            .ToArray();
    }

    private async Task<ChatCompletionResponse> ExecuteChatRequestWithFallbackAsync(
        ChatCompletionRequest requestPayload,
        CancellationToken ct)
    {
        var primaryModel = requestPayload.Model;
        var json = JsonSerializer.Serialize(requestPayload, JsonOptions);
        using var requestContent = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage httpResponse;
        try
        {
            httpResponse = await _httpClient.PostAsync("chat/completions", requestContent, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to connect to NVIDIA NIM chat endpoint with model {Model}.", primaryModel);
            throw new LlmProviderException($"Network failure contacting NVIDIA NIM: {ex.Message}", ex);
        }

        // Automatic fallback on 429 TooManyRequests or 503 ServiceUnavailable
        if ((httpResponse.StatusCode == HttpStatusCode.TooManyRequests ||
             httpResponse.StatusCode == HttpStatusCode.ServiceUnavailable) &&
            !string.Equals(primaryModel, _config.FallbackModel, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("NVIDIA NIM model {PrimaryModel} returned {StatusCode}. Initiating fallback to {FallbackModel}.",
                primaryModel, httpResponse.StatusCode, _config.FallbackModel);

            requestPayload.Model = _config.FallbackModel;
            var fallbackJson = JsonSerializer.Serialize(requestPayload, JsonOptions);
            using var fallbackContent = new StringContent(fallbackJson, Encoding.UTF8, "application/json");

            httpResponse = await _httpClient.PostAsync("chat/completions", fallbackContent, ct);
        }

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorBody = await httpResponse.Content.ReadAsStringAsync(ct);
            _logger.LogError("NVIDIA NIM chat completions failed with HTTP {StatusCode}: {ErrorBody}",
                (int)httpResponse.StatusCode, errorBody);

            throw new LlmProviderException(
                $"NVIDIA NIM returned HTTP {(int)httpResponse.StatusCode}: {errorBody}");
        }

        var responseStream = await httpResponse.Content.ReadAsStreamAsync(ct);
        var result = await JsonSerializer.DeserializeAsync<ChatCompletionResponse>(responseStream, JsonOptions, ct);

        return result ?? throw new LlmProviderException("NVIDIA NIM chat response deserialized to null.");
    }
}
