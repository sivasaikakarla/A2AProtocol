using McpServer.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace McpServer.Data;

/// <summary>
/// Entity Framework Core database context for the MCP Server.
/// Configured for PostgreSQL with pgvector extension enabled for 1024-dimensional semantic similarity vector search.
/// </summary>
public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enable the pgvector extension for vector embeddings
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<DocumentChunk>(e =>
        {
            e.ToTable("document_chunks");
            e.HasKey(x => x.Id);

            e.Property(x => x.Ticker).HasMaxLength(10).IsRequired();
            e.Property(x => x.FilingType).HasMaxLength(20).IsRequired();
            e.Property(x => x.FiscalYear).IsRequired();
            e.Property(x => x.Section).HasMaxLength(100).IsRequired();
            e.Property(x => x.ChunkIndex).IsRequired();
            e.Property(x => x.Content).IsRequired();
            e.Property(x => x.CreatedAt).IsRequired();

            // 1024-dimensional vector embedding column
            e.Property(x => x.Embedding).HasColumnType("vector(1024)");

            // B-Tree indexes for fast metadata filtering
            e.HasIndex(x => x.Ticker);
            e.HasIndex(x => new { x.Ticker, x.FiscalYear });
            e.HasIndex(x => new { x.Ticker, x.FilingType, x.FiscalYear });

            // IVFFlat vector index with cosine distance operators
            e.HasIndex(x => x.Embedding)
                .HasMethod("ivfflat")
                .HasOperators("vector_cosine_ops")
                .HasStorageParameter("lists", 100);
        });

        modelBuilder.Entity<Company>(e =>
        {
            e.ToTable("companies");
            e.HasKey(x => x.Ticker);

            e.Property(x => x.Ticker).HasMaxLength(10).IsRequired();
            e.Property(x => x.CompanyName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Sector).HasMaxLength(100);
            e.Property(x => x.Industry).HasMaxLength(100).IsRequired();
            e.Property(x => x.Cik).HasMaxLength(20);
            e.Property(x => x.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.HasKey(x => x.Id);

            e.Property(x => x.ToolName).HasMaxLength(100).IsRequired();
            e.Property(x => x.ClientId).HasMaxLength(100);
            e.Property(x => x.Timestamp).IsRequired();

            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => x.ToolName);
            e.HasIndex(x => x.ClientId);
        });
    }
}
