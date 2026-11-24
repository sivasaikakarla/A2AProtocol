namespace McpServer.Data.Entities;

/// <summary>
/// Represents a company covered in the financial intelligence knowledge base.
/// </summary>
public sealed class Company
{
    /// <summary>
    /// Unique stock ticker symbol (Primary Key, e.g. "NVDA", "AAPL").
    /// </summary>
    public required string Ticker { get; set; }

    /// <summary>
    /// Full corporate legal name (e.g. "NVIDIA Corporation").
    /// </summary>
    public required string CompanyName { get; set; }

    /// <summary>
    /// High-level market sector (e.g. "Technology", "Financial Services").
    /// </summary>
    public string Sector { get; set; } = string.Empty;

    /// <summary>
    /// Specific industry classification (e.g. "Semiconductors", "Consumer Electronics").
    /// </summary>
    public required string Industry { get; set; }

    /// <summary>
    /// SEC Central Index Key (CIK) used for regulatory filings lookup.
    /// </summary>
    public string Cik { get; set; } = string.Empty;

    /// <summary>
    /// Summary description of the company's core business model and operations.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Timestamp when this company record was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
