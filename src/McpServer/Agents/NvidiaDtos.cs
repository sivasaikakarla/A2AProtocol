using System.Text.Json.Serialization;

namespace McpServer.Agents;

public sealed class ChatCompletionRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("messages")]
    public required List<ChatMessageDto> Messages { get; set; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 2048;

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.1;
}

public sealed class ChatMessageDto
{
    [JsonPropertyName("role")]
    public required string Role { get; set; }

    [JsonPropertyName("content")]
    public required string Content { get; set; }
}

public sealed class ChatCompletionResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("choices")]
    public List<ChatChoiceDto> Choices { get; set; } = new();

    [JsonPropertyName("usage")]
    public UsageDto? Usage { get; set; }
}

public sealed class ChatChoiceDto
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public ChatResponseMessageDto Message { get; set; } = new();

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

public sealed class ChatResponseMessageDto
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "assistant";

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; set; }
}

public sealed class EmbeddingRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; set; }

    [JsonPropertyName("input")]
    public required List<string> Input { get; set; }

    [JsonPropertyName("input_type")]
    public string InputType { get; set; } = "query";

    [JsonPropertyName("dimensions")]
    public int? Dimensions { get; set; }
}

public sealed class EmbeddingResponse
{
    [JsonPropertyName("data")]
    public List<EmbeddingItemDto> Data { get; set; } = new();

    [JsonPropertyName("usage")]
    public UsageDto? Usage { get; set; }
}

public sealed class EmbeddingItemDto
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("embedding")]
    public float[] Embedding { get; set; } = Array.Empty<float>();
}

public sealed class UsageDto
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}
