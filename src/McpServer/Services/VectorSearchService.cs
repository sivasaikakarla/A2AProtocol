using McpServer.Agents;
using McpServer.Data;
using McpServer.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace McpServer.Services;

/// <summary>
/// Service providing high-speed semantic similarity search over regulatory SEC filings stored in PostgreSQL with pgvector.
/// Converts text search queries to 1024-dimensional embeddings via NVIDIA NIM and queries pgvector using cosine distance.
/// </summary>
public sealed class VectorSearchService
{
    private readonly AppDbContext _dbContext;
    private readonly NvidiaLlmClient _nvidiaClient;
    private readonly ILogger<VectorSearchService> _logger;

    public VectorSearchService(
        AppDbContext dbContext,
        NvidiaLlmClient nvidiaClient,
        ILogger<VectorSearchService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _nvidiaClient = nvidiaClient ?? throw new ArgumentNullException(nameof(nvidiaClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a vector cosine similarity search against SEC filing document chunks.
    /// </summary>
    /// <param name="query">The user query or search phrase.</param>
    /// <param name="ticker">Optional stock ticker filter (e.g. NVDA, AAPL).</param>
    /// <param name="fiscalYear">Optional fiscal year filter (e.g. 2024).</param>
    /// <param name="topK">Maximum number of chunks to return (default 5, clamped between 1 and 25).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Ranked list of matching document chunks.</returns>
    public async Task<List<DocumentChunk>> SearchAsync(
        string query,
        string? ticker = null,
        int? fiscalYear = null,
        int topK = 5,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<DocumentChunk>();
        }

        topK = Math.Clamp(topK, 1, 25);

        _logger.LogInformation("Generating embedding for search query: '{Query}' (Filters: Ticker={Ticker}, FY={FY}, TopK={TopK})",
            query, ticker, fiscalYear, topK);

        // Generate 1024-dimensional embedding for query
        var embeddings = await _nvidiaClient.EmbedAsync(new[] { query }, inputType: "query", ct);
        if (embeddings.Length == 0 || embeddings[0].Length == 0)
        {
            _logger.LogWarning("No embedding was returned for query '{Query}'.", query);
            return new List<DocumentChunk>();
        }

        var queryVector = new Vector(embeddings[0]);

        // Base query
        var dbQuery = _dbContext.DocumentChunks
            .AsNoTracking()
            .Where(c => c.Embedding != null);

        // Optional metadata filters
        if (!string.IsNullOrWhiteSpace(ticker))
        {
            var normalizedTicker = ticker.Trim().ToUpperInvariant();
            dbQuery = dbQuery.Where(c => c.Ticker == normalizedTicker);
        }

        if (fiscalYear.HasValue)
        {
            dbQuery = dbQuery.Where(c => c.FiscalYear == fiscalYear.Value);
        }

        // Cosine distance nearest neighbor ranking via pgvector <=> operator
        var results = await dbQuery
            .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
            .Take(topK)
            .ToListAsync(ct);

        _logger.LogInformation("Retrieved {Count} document chunks matching query '{Query}'.",
            results.Count, query);

        return results;
    }
}
