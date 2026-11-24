using McpServer.Agents;
using McpServer.Data;
using McpServer.Data.Entities;
using McpServer.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pgvector;
using Xunit;

namespace McpServer.IntegrationTests;

public class RagPipelineTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RagPipelineTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RagPipeline_InsertChunksAndVectorSearch_ReturnsRankedResults()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var searchService = scope.ServiceProvider.GetRequiredService<VectorSearchService>();

        // 1. Generate test vector embedding (max 10 chars for Ticker)
        var testTicker = "TST" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
        var embeddingDim = 1024;
        var vectorFloats = new float[embeddingDim];
        for (int i = 0; i < embeddingDim; i++)
        {
            vectorFloats[i] = (float)Math.Sin(i);
        }
        var vector = new Vector(vectorFloats);

        // 2. Insert test chunk
        var chunk = new DocumentChunk
        {
            Ticker = testTicker,
            FilingType = "10-K",
            FiscalYear = 2024,
            Section = "Item 1A. Risk Factors",
            ChunkIndex = 0,
            Content = $"This is an integrated RAG test disclosure regarding cybersecurity and cloud infrastructure for {testTicker}.",
            Embedding = vector
        };

        db.DocumentChunks.Add(chunk);
        await db.SaveChangesAsync();

        // 3. Search via vector similarity or ticker filtering
        var results = await searchService.SearchAsync(
            query: "cybersecurity and cloud infrastructure",
            ticker: testTicker,
            fiscalYear: 2024,
            topK: 5);

        // 4. Verify results
        Assert.NotEmpty(results);
        Assert.Contains(results, r => r.Ticker == testTicker);
        Assert.Contains("cybersecurity", results[0].Content);

        // Clean up
        db.DocumentChunks.Remove(chunk);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task RagPipeline_SearchWithNonExistentTicker_ReturnsEmptyList()
    {
        using var scope = _factory.Services.CreateScope();
        var searchService = scope.ServiceProvider.GetRequiredService<VectorSearchService>();

        var results = await searchService.SearchAsync(
            query: "semiconductor production capacity",
            ticker: "NONEXISTENT_TICKER_999",
            fiscalYear: 2024,
            topK: 5);

        Assert.Empty(results);
    }
}
