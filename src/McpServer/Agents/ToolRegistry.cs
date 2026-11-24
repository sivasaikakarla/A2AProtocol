using McpServer.ErrorHandling;

namespace McpServer.Agents;

public sealed class ToolParameterProperty
{
    public required string Type { get; set; }
    public required string Description { get; set; }
    public List<string>? Enum { get; set; }
}

public sealed class ToolParameterSchema
{
    public string Type { get; set; } = "object";
    public Dictionary<string, ToolParameterProperty> Properties { get; set; } = new();
    public List<string> Required { get; set; } = new();
}

public sealed class McpToolDefinition
{
    public required string Name { get; set; }
    public required string Description { get; set; }
    public required string EndpointRoute { get; set; }
    public string RequiredRole { get; set; } = "tool-executor";
    public required ToolParameterSchema InputSchema { get; set; }
}

/// <summary>
/// Registry service that provides tool definitions, schema metadata, and validation
/// for all enterprise MCP tools exposed by the server.
/// </summary>
public sealed class ToolRegistry
{
    private static readonly Dictionary<string, McpToolDefinition> Tools = new(StringComparer.OrdinalIgnoreCase)
    {
        ["search_financial_filings"] = new()
        {
            Name = "search_financial_filings",
            Description = "Executes semantic RAG vector search across regulatory SEC 10-K filings using pgvector and synthesizes grounded answers with citations.",
            EndpointRoute = "/api/tools/search-filings",
            InputSchema = new ToolParameterSchema
            {
                Properties = new Dictionary<string, ToolParameterProperty>
                {
                    ["query"] = new() { Type = "string", Description = "Natural language search query or financial question (max 1000 characters)." },
                    ["ticker"] = new() { Type = "string", Description = "Optional company stock ticker in uppercase (e.g. NVDA, AAPL)." },
                    ["fiscalYear"] = new() { Type = "integer", Description = "Optional fiscal year filter (e.g. 2024)." },
                    ["topK"] = new() { Type = "integer", Description = "Number of chunks to retrieve for synthesis (1 to 20, default 5)." }
                },
                Required = new List<string> { "query" }
            }
        },
        ["check_compliance"] = new()
        {
            Name = "check_compliance",
            Description = "Performs automated corporate compliance auditing against regulatory frameworks (SOX-404, SEC-Risk-Disclosure, Basel-III) on filed SEC disclosures.",
            EndpointRoute = "/api/tools/check-compliance",
            InputSchema = new ToolParameterSchema
            {
                Properties = new Dictionary<string, ToolParameterProperty>
                {
                    ["ticker"] = new() { Type = "string", Description = "Company stock ticker in uppercase (e.g. NVDA)." },
                    ["fiscalYear"] = new() { Type = "integer", Description = "Fiscal year of filing under audit." },
                    ["framework"] = new()
                    {
                        Type = "string",
                        Description = "Regulatory framework to audit against.",
                        Enum = new List<string> { "SOX-404", "SEC-Risk-Disclosure", "Basel-III" }
                    }
                },
                Required = new List<string> { "ticker", "fiscalYear", "framework" }
            }
        },
        ["generate_audit_report"] = new()
        {
            Name = "generate_audit_report",
            Description = "Generates structured multi-section financial audit reports (executive-summary, risk-analysis, full) synthesizing SEC 10-K disclosures.",
            EndpointRoute = "/api/tools/generate-report",
            InputSchema = new ToolParameterSchema
            {
                Properties = new Dictionary<string, ToolParameterProperty>
                {
                    ["ticker"] = new() { Type = "string", Description = "Company stock ticker in uppercase (e.g. NVDA)." },
                    ["fiscalYear"] = new() { Type = "integer", Description = "Fiscal year of the report." },
                    ["reportType"] = new()
                    {
                        Type = "string",
                        Description = "Type of report to generate.",
                        Enum = new List<string> { "executive-summary", "risk-analysis", "full" }
                    }
                },
                Required = new List<string> { "ticker", "fiscalYear", "reportType" }
            }
        },
        ["lookup_company"] = new()
        {
            Name = "lookup_company",
            Description = "Looks up corporate metadata, SEC CIK numbers, industry classifications, and available indexed filing periods for a given stock ticker.",
            EndpointRoute = "/api/tools/lookup-company",
            InputSchema = new ToolParameterSchema
            {
                Properties = new Dictionary<string, ToolParameterProperty>
                {
                    ["ticker"] = new() { Type = "string", Description = "Company stock ticker in uppercase (e.g. NVDA, MSFT)." }
                },
                Required = new List<string> { "ticker" }
            }
        }
    };

    public IReadOnlyList<McpToolDefinition> GetAllTools() => Tools.Values.ToList();

    public McpToolDefinition? GetTool(string name)
    {
        Tools.TryGetValue(name, out var tool);
        return tool;
    }

    public McpToolDefinition GetRequiredTool(string name)
    {
        return GetTool(name) ?? throw new ToolNotFoundException(name);
    }
}
