using McpServer.Data;
using McpServer.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;
using Xunit;

namespace McpServer.IntegrationTests.Database;

public class PostgreSqlVectorIntegrationTests : IAsyncLifetime
{
    private const string ConnectionString = "Host=localhost;Port=5432;Database=mcptools;Username=mcp;Password=McpS3cur3Pass!";
    private AppDbContext _db = null!;

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
        // Clean up test data inserted during tests
        try
        {
            var testChunks = await _db.DocumentChunks.Where(c => c.Ticker.StartsWith("TEST_")).ToListAsync();
            _db.DocumentChunks.RemoveRange(testChunks);

            var testCompanies = await _db.Companies.Where(c => c.Ticker.StartsWith("TEST_")).ToListAsync();
            _db.Companies.RemoveRange(testCompanies);

            var testLogs = await _db.AuditLogs.Where(l => l.ToolName.StartsWith("test_")).ToListAsync();
            _db.AuditLogs.RemoveRange(testLogs);

            await _db.SaveChangesAsync();
        }
        catch
        {
            // Ignore cleanup failures on test teardown
        }

        await _db.DisposeAsync();
    }

    [Fact]
    public async Task Company_CanInsertRetrieveAndQuery()
    {
        // Arrange
        var ticker = "TEST_NVDA";
        var company = new Company
        {
            Ticker = ticker,
            CompanyName = "Test NVIDIA Corporation",
            Sector = "Technology",
            Industry = "Semiconductors",
            Cik = "0001045810",
            Description = "Pioneer of GPU-accelerated computing and modern enterprise AI."
        };

        // Act
        _db.Companies.Add(company);
        await _db.SaveChangesAsync();

        var retrieved = await _db.Companies.FirstOrDefaultAsync(c => c.Ticker == ticker);

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("Test NVIDIA Corporation", retrieved.CompanyName);
        Assert.Equal("Technology", retrieved.Sector);
        Assert.Equal("Semiconductors", retrieved.Industry);
        Assert.Equal("0001045810", retrieved.Cik);
    }

    [Fact]
    public async Task DocumentChunk_CanInsertAndRetrieve1024DimVector()
    {
        // Arrange
        var ticker = "TEST_CHUNK";
        var embeddingValues = new float[1024];
        for (int i = 0; i < 1024; i++)
        {
            embeddingValues[i] = (float)Math.Sin(i);
        }
        var vector = new Vector(embeddingValues);

        var chunk = new DocumentChunk
        {
            Ticker = ticker,
            FilingType = "10-K",
            FiscalYear = 2024,
            Section = "Item 1A - Risk Factors",
            ChunkIndex = 0,
            Content = "Our GPU and AI architectures face intense competition and evolving regulatory controls.",
            Embedding = vector
        };

        // Act
        _db.DocumentChunks.Add(chunk);
        await _db.SaveChangesAsync();

        var retrieved = await _db.DocumentChunks.FirstOrDefaultAsync(c => c.Ticker == ticker);

        // Assert
        Assert.NotNull(retrieved);
        Assert.NotNull(retrieved.Embedding);
        var retrievedArray = retrieved.Embedding.ToArray();
        Assert.Equal(1024, retrievedArray.Length);
        Assert.Equal(embeddingValues[0], retrievedArray[0], precision: 4);
        Assert.Equal(embeddingValues[100], retrievedArray[100], precision: 4);
    }

    [Fact]
    public async Task DocumentChunk_CosineSimilarityVectorSearch_ReturnsNearestNeighbors()
    {
        // Arrange: Create 3 chunks with distinct vectors
        var ticker = "TEST_SIM";

        // Query vector points along dimension 0
        var queryArray = new float[1024];
        queryArray[0] = 1.0f;
        var queryVector = new Vector(queryArray);

        // Chunk A: Close to query vector (almost parallel)
        var chunkA_Array = new float[1024];
        chunkA_Array[0] = 0.95f;
        chunkA_Array[1] = 0.05f;

        // Chunk B: Moderate angle
        var chunkB_Array = new float[1024];
        chunkB_Array[0] = 0.5f;
        chunkB_Array[1] = 0.5f;

        // Chunk C: Orthogonal to query vector
        var chunkC_Array = new float[1024];
        chunkC_Array[1] = 1.0f;

        var chunkA = new DocumentChunk
        {
            Ticker = ticker,
            FilingType = "10-K",
            FiscalYear = 2024,
            Section = "Item 7 - MD&A",
            ChunkIndex = 1,
            Content = "High similarity chunk discussing GPU revenue and data center compute demand.",
            Embedding = new Vector(chunkA_Array)
        };

        var chunkB = new DocumentChunk
        {
            Ticker = ticker,
            FilingType = "10-K",
            FiscalYear = 2024,
            Section = "Item 7 - MD&A",
            ChunkIndex = 2,
            Content = "Moderate similarity chunk discussing supply chain logistics.",
            Embedding = new Vector(chunkB_Array)
        };

        var chunkC = new DocumentChunk
        {
            Ticker = ticker,
            FilingType = "10-K",
            FiscalYear = 2024,
            Section = "Item 8 - Financial Statements",
            ChunkIndex = 3,
            Content = "Orthogonal chunk discussing foreign currency lease liabilities.",
            Embedding = new Vector(chunkC_Array)
        };

        _db.DocumentChunks.AddRange(chunkA, chunkB, chunkC);
        await _db.SaveChangesAsync();

        // Act: Cosine distance search using pgvector operator <=>
        var nearestChunks = await _db.DocumentChunks
            .Where(c => c.Ticker == ticker)
            .OrderBy(c => c.Embedding!.CosineDistance(queryVector))
            .Take(3)
            .ToListAsync();

        // Assert
        Assert.Equal(3, nearestChunks.Count);
        // Chunk A must be the first nearest neighbor because it has the smallest cosine distance to query
        Assert.Equal(1, nearestChunks[0].ChunkIndex);
        Assert.Contains("High similarity", nearestChunks[0].Content);
        // Chunk B is second
        Assert.Equal(2, nearestChunks[1].ChunkIndex);
        // Chunk C is third
        Assert.Equal(3, nearestChunks[2].ChunkIndex);
    }

    [Fact]
    public async Task AuditLog_CanRecordAndQueryExecutionAudit()
    {
        // Arrange
        var audit = new AuditLog
        {
            ToolName = "test_compliance_checker",
            ClientId = "test-client-enterprise",
            Request = "{\"rule\":\"SOX-404\",\"company\":\"NVDA\"}",
            Response = "{\"compliant\":true,\"findings\":[]}",
            DurationMs = 142,
            Success = true,
            ErrorDetail = null,
            Timestamp = DateTime.UtcNow
        };

        // Act
        _db.AuditLogs.Add(audit);
        await _db.SaveChangesAsync();

        var retrieved = await _db.AuditLogs
            .FirstOrDefaultAsync(l => l.ToolName == "test_compliance_checker");

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("test-client-enterprise", retrieved.ClientId);
        Assert.Equal(142, retrieved.DurationMs);
        Assert.True(retrieved.Success);
        Assert.Contains("SOX-404", retrieved.Request);
    }
}
