using Pgvector;

namespace McpServer.Data.Entities;

/// <summary>
/// Represents a chunked section of an SEC filing (e.g. 10-K, 10-Q) with high-dimensional vector embeddings.
/// Stored in PostgreSQL with pgvector for cosine similarity semantic search.
/// </summary>
public sealed class DocumentChunk
{
    public long Id { get; set; }

    /// <summary>
    /// Stock ticker symbol in uppercase (e.g. "NVDA", "AAPL", "MSFT").
    /// </summary>
    public required string Ticker { get; set; }

    /// <summary>
    /// Type of regulatory SEC filing (e.g. "10-K", "10-Q", "8-K").
    /// </summary>
    public required string FilingType { get; set; }

    /// <summary>
    /// Fiscal year of the filing (e.g. 2023, 2024).
    /// </summary>
    public int FiscalYear { get; set; }

    /// <summary>
    /// Section name from the filing (e.g. "Item 1A - Risk Factors", "Item 7 - Management's Discussion and Analysis").
    /// </summary>
    public required string Section { get; set; }

    /// <summary>
    /// Index order of this chunk within the section.
    /// </summary>
    public int ChunkIndex { get; set; }

    /// <summary>
    /// Raw textual content extracted from the SEC filing section.
    /// </summary>
    public required string Content { get; set; }

    /// <summary>
    /// 1024-dimensional semantic embedding vector (generated via NVIDIA NIM or compatible embedding models).
    /// </summary>
    public Vector? Embedding { get; set; }

    /// <summary>
    /// Timestamp when this chunk was ingested.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
