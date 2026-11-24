namespace McpServer.Agents;

/// <summary>
/// Strongly-typed configuration options for NVIDIA NIM API integration.
/// Binds to the "Nvidia" configuration section or environment variables.
/// </summary>
public sealed class NvidiaConfig
{
    public const string SectionName = "Nvidia";

    /// <summary>
    /// NVIDIA NIM API Bearer Token.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for NVIDIA NIM REST API.
    /// </summary>
    public string BaseUrl { get; set; } = "https://integrate.api.nvidia.com/v1";

    /// <summary>
    /// Primary model for reasoning, chat, and MCP tool synthesis.
    /// </summary>
    public string ChatModel { get; set; } = "nvidia/nemotron-3.5-super-vl-120b-a12b";

    /// <summary>
    /// High-performance embedding model for semantic retrieval.
    /// </summary>
    public string EmbeddingModel { get; set; } = "nvidia/nemotron-3.5-embed-1b";

    /// <summary>
    /// Resilient fallback model used when the primary model encounters rate limits (429) or transient faults.
    /// </summary>
    public string FallbackModel { get; set; } = "nvidia/nemotron-3-ultra-550b-a55b";

    /// <summary>
    /// Maximum tokens for generated completions.
    /// </summary>
    public int MaxTokens { get; set; } = 2048;

    /// <summary>
    /// Sampling temperature (default 0.1 for high determinism in enterprise financial analysis).
    /// </summary>
    public double Temperature { get; set; } = 0.1;

    /// <summary>
    /// Target dimension size for embeddings (default 1024 to match pgvector storage).
    /// </summary>
    public int EmbeddingDimensions { get; set; } = 1024;
}
