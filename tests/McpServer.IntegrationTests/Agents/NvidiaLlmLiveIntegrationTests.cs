using McpServer.Agents;
using McpServer.Data;
using McpServer.Data.Entities;
using McpServer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Xunit;

namespace McpServer.IntegrationTests.Agents;

public class NvidiaLlmLiveIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=mcptools;Username=mcp;Password=McpS3cur3Pass!";
    private readonly string? _apiKey;
    private AppDbContext? _db;

    public NvidiaLlmLiveIntegrationTests()
    {
        // Read key from environment or local .env
        _apiKey = Environment.GetEnvironmentVariable("NVIDIA_API_KEY");
        if (string.IsNullOrWhiteSpace(_apiKey) && File.Exists("../../../../../.env"))
        {
            foreach (var line in File.ReadAllLines("../../../../../.env"))
            {
                if (line.StartsWith("NVIDIA_API_KEY="))
                {
                    _apiKey = line.Split('=', 2)[1].Trim().Trim('"').Trim('\'');
                    break;
                }
            }
        }
        if (string.IsNullOrWhiteSpace(_apiKey) && File.Exists(".env"))
        {
            foreach (var line in File.ReadAllLines(".env"))
            {
                if (line.StartsWith("NVIDIA_API_KEY="))
                {
                    _apiKey = line.Split('=', 2)[1].Trim().Trim('"').Trim('\'');
                    break;
                }
            }
        }
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString, o => o.UseVector())
            .Options;

        _db = new AppDbContext(options);
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_db != null)
        {
            try
            {
                var liveChunks = await _db.DocumentChunks
                    .Where(c => c.Ticker == "LIVE_NVDA")
                    .ToListAsync();
                _db.DocumentChunks.RemoveRange(liveChunks);
                await _db.SaveChangesAsync();
            }
            catch
            {
                // ignore cleanup errors
            }

            await _db.DisposeAsync();
        }
    }

    private NvidiaLlmClient CreateLiveClient()
    {
        var config = new NvidiaConfig
        {
            ApiKey = _apiKey ?? string.Empty,
            BaseUrl = "https://integrate.api.nvidia.com/v1",
            ChatModel = "nvidia/nemotron-3.5-super-vl-120b-a12b",
            EmbeddingModel = "nvidia/nemotron-3.5-embed-1b",
            FallbackModel = "nvidia/nemotron-3-ultra-550b-a55b",
            MaxTokens = 1024,
            Temperature = 0.1,
            EmbeddingDimensions = 1024
        };

        var httpClient = new HttpClient
        {
            BaseAddress = new Uri(config.BaseUrl.TrimEnd('/') + "/")
        };

        return new NvidiaLlmClient(
            httpClient,
            Options.Create(config),
            NullLogger<NvidiaLlmClient>.Instance);
    }

    [Fact]
    public async Task LiveNvidiaNim_ChatCompletion_ReturnsValidReasoning()
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            // Skip if no API key is available in environment
            return;
        }

        var client = CreateLiveClient();

        // Act
        var response = await client.ChatAsync(
            systemPrompt: "You are an enterprise financial auditor. Keep answers concise.",
            userMessage: "What does SOX Section 404 require of public companies?");

        // Assert
        Assert.NotNull(response);
        Assert.NotEmpty(response);
        Assert.True(response.Length > 20);
    }

    [Fact]
    public async Task LiveNvidiaNim_Embeddings_Returns1024DimFloatVector()
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return;
        }

        var client = CreateLiveClient();

        // Act
        var embeddings = await client.EmbedAsync(
            new[] { "NVIDIA data center compute revenue grew 217% year-over-year." },
            inputType: "query");

        // Assert
        Assert.NotNull(embeddings);
        Assert.Single(embeddings);
        Assert.Equal(1024, embeddings[0].Length);
    }

    [Fact]
    public async Task LiveVectorSearchService_RealSearch_EmbedsAndQueriesPgvector()
    {
        if (string.IsNullOrWhiteSpace(_apiKey) || _db == null)
        {
            return;
        }

        var client = CreateLiveClient();
        var searchService = new VectorSearchService(_db, client, NullLogger<VectorSearchService>.Instance);

        // 1. Embed and insert a live document chunk
        var content = "NVIDIA CUDA architecture and Tensor Core GPUs power generative AI training clusters worldwide.";
        var embeddings = await client.EmbedAsync(new[] { content }, inputType: "passage");
        Assert.Single(embeddings);
        Assert.Equal(1024, embeddings[0].Length);

        var chunk = new DocumentChunk
        {
            Ticker = "LIVE_NVDA",
            FilingType = "10-K",
            FiscalYear = 2024,
            Section = "Item 1 - Business",
            ChunkIndex = 1,
            Content = content,
            Embedding = new Vector(embeddings[0])
        };

        _db.DocumentChunks.Add(chunk);
        await _db.SaveChangesAsync();

        // 2. Search using natural language semantic query
        var searchResults = await searchService.SearchAsync(
            query: "AI GPU clusters and deep learning training acceleration",
            ticker: "LIVE_NVDA",
            fiscalYear: 2024,
            topK: 1);

        // Assert
        Assert.NotNull(searchResults);
        Assert.Single(searchResults);
        Assert.Equal("LIVE_NVDA", searchResults[0].Ticker);
        Assert.Contains("CUDA", searchResults[0].Content);
    }
}
