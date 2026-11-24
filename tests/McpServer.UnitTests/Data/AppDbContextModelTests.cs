using McpServer.Data;
using McpServer.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Xunit;

namespace McpServer.UnitTests.Data;

public class AppDbContextModelTests
{
    private readonly AppDbContext _context;

    public AppDbContextModelTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=test", o => o.UseVector())
            .Options;

        _context = new AppDbContext(options);
    }

    [Fact]
    public void Model_ContainsRequiredEntityTypes()
    {
        var entityTypes = _context.Model.GetEntityTypes().Select(e => e.ClrType).ToList();

        Assert.Contains(typeof(DocumentChunk), entityTypes);
        Assert.Contains(typeof(Company), entityTypes);
        Assert.Contains(typeof(AuditLog), entityTypes);
    }

    [Fact]
    public void DocumentChunk_HasCorrectTableAndColumnConfiguration()
    {
        var entity = _context.Model.FindEntityType(typeof(DocumentChunk));
        Assert.NotNull(entity);

        Assert.Equal("document_chunks", entity.GetTableName());

        // Primary key
        var pk = entity.FindPrimaryKey();
        Assert.NotNull(pk);
        Assert.Single(pk.Properties);
        Assert.Equal("Id", pk.Properties[0].Name);

        // Embedding column vector(1024)
        var embeddingProp = entity.FindProperty(nameof(DocumentChunk.Embedding));
        Assert.NotNull(embeddingProp);
        Assert.Equal("vector(1024)", embeddingProp.GetColumnType());

        // Indexes
        var indexes = entity.GetIndexes().ToList();
        Assert.Contains(indexes, i => i.Properties.Count == 1 && i.Properties[0].Name == "Ticker");
        Assert.Contains(indexes, i => i.Properties.Count == 2 &&
                                      i.Properties.Any(p => p.Name == "Ticker") &&
                                      i.Properties.Any(p => p.Name == "FiscalYear"));
        Assert.Contains(indexes, i => i.Properties.Count == 1 && i.Properties[0].Name == "Embedding");
    }

    [Fact]
    public void Company_HasCorrectPrimaryKeyAndTable()
    {
        var entity = _context.Model.FindEntityType(typeof(Company));
        Assert.NotNull(entity);

        Assert.Equal("companies", entity.GetTableName());

        var pk = entity.FindPrimaryKey();
        Assert.NotNull(pk);
        Assert.Single(pk.Properties);
        Assert.Equal("Ticker", pk.Properties[0].Name);

        var nameProp = entity.FindProperty(nameof(Company.CompanyName));
        Assert.NotNull(nameProp);
        Assert.Equal(200, nameProp.GetMaxLength());
    }

    [Fact]
    public void AuditLog_HasCorrectTableAndIndexes()
    {
        var entity = _context.Model.FindEntityType(typeof(AuditLog));
        Assert.NotNull(entity);

        Assert.Equal("audit_logs", entity.GetTableName());

        var pk = entity.FindPrimaryKey();
        Assert.NotNull(pk);
        Assert.Single(pk.Properties);
        Assert.Equal("Id", pk.Properties[0].Name);

        var indexes = entity.GetIndexes().ToList();
        Assert.Contains(indexes, i => i.Properties.Any(p => p.Name == "Timestamp"));
        Assert.Contains(indexes, i => i.Properties.Any(p => p.Name == "ToolName"));
    }
}
