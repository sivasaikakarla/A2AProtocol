using System.Diagnostics;
using System.Text.Json;
using McpServer.Data;
using McpServer.ErrorHandling;
using McpServer.Services;
using McpServer.Tools.AuditReportGenerator;
using McpServer.Tools.CompanyLookup;
using McpServer.Tools.ComplianceChecker;
using McpServer.Tools.FinancialSearch;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
/// Registry service that provides tool definitions, schema metadata, description generation for LLMs,
/// and dynamic in-process tool execution.
/// </summary>
public sealed class ToolRegistry
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

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
                    ["ticker"] = new() { Type = "string", Description = "Optional company stock ticker in uppercase (e.g. NVDA, AAPL, MSFT, JPM, TSLA, GOOGL)." },
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
                    ["ticker"] = new() { Type = "string", Description = "Company stock ticker in uppercase (e.g. NVDA, AAPL, MSFT, TSLA)." },
                    ["fiscalYear"] = new() { Type = "integer", Description = "Fiscal year of filing under audit (e.g. 2024)." },
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
                    ["ticker"] = new() { Type = "string", Description = "Company stock ticker in uppercase (e.g. NVDA, AAPL, MSFT, TSLA)." },
                    ["fiscalYear"] = new() { Type = "integer", Description = "Fiscal year of the report (e.g. 2024)." },
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
                    ["ticker"] = new() { Type = "string", Description = "Company stock ticker in uppercase (e.g. NVDA, AAPL, MSFT, TSLA, GOOGL, JPM)." }
                },
                Required = new List<string> { "ticker" }
            }
        }
    };

    public IReadOnlyList<McpToolDefinition> GetAllTools() => Tools.Values.ToList();

    public McpToolDefinition? GetTool(string name)
    {
        var resolvedName = NormalizeToolName(name);
        Tools.TryGetValue(resolvedName, out var tool);
        return tool;
    }

    public McpToolDefinition GetRequiredTool(string name)
    {
        return GetTool(name) ?? throw new ToolNotFoundException(name);
    }

    /// <summary>
    /// Generates formatted markdown text describing all registered tools, their parameters, and types
    /// for injection into LLM agent reasoning system prompts.
    /// </summary>
    public string GetToolDescriptions()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You have access to the following enterprise MCP tools:");
        sb.AppendLine();

        int i = 1;
        foreach (var tool in Tools.Values)
        {
            sb.AppendLine($"{i++}. `{tool.Name}`");
            sb.AppendLine($"   Description: {tool.Description}");
            sb.AppendLine("   Parameters:");
            foreach (var (propName, prop) in tool.InputSchema.Properties)
            {
                var isRequired = tool.InputSchema.Required.Contains(propName) ? "required" : "optional";
                var enumDesc = prop.Enum != null ? $" [Allowed: {string.Join(", ", prop.Enum)}]" : "";
                sb.AppendLine($"     - `{propName}` ({prop.Type}, {isRequired}): {prop.Description}{enumDesc}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("To invoke a tool, output a code block in the following EXACT format:");
        sb.AppendLine("```tool_call");
        sb.AppendLine("{\"tool\": \"tool_name\", \"arguments\": {\"param1\": \"value1\"}}");
        sb.AppendLine("```");
        sb.AppendLine("Only call one tool at a time. When you have enough information to answer the question, formulate your final comprehensive answer without any tool_call block.");

        return sb.ToString();
    }

    /// <summary>
    /// Executes a registered MCP tool in-process using scoped dependency injection services.
    /// </summary>
    public async Task<string> ExecuteToolAsync(
        string toolName,
        JsonElement arguments,
        IServiceProvider serviceProvider,
        CancellationToken ct = default)
    {
        var normalized = NormalizeToolName(toolName);
        var sw = Stopwatch.StartNew();
        var auditLog = serviceProvider.GetRequiredService<AuditLogService>();

        try
        {
            switch (normalized)
            {
                case "search_financial_filings":
                {
                    var req = JsonSerializer.Deserialize<SearchFilingsRequest>(arguments.GetRawText(), JsonOpts)
                              ?? throw new ToolSchemaValidationException(new Dictionary<string, string[]> { ["arguments"] = new[] { "Failed to deserialize search_financial_filings arguments." } });

                    var searchService = serviceProvider.GetRequiredService<VectorSearchService>();
                    var llmClient = serviceProvider.GetRequiredService<NvidiaLlmClient>();

                    var chunks = await searchService.SearchAsync(
                        query: req.Query,
                        ticker: req.Ticker,
                        fiscalYear: req.FiscalYear,
                        topK: req.TopK,
                        ct: ct);

                    if (chunks.Count == 0)
                    {
                        var empty = new SearchFilingsResponse
                        {
                            Answer = $"No regulatory SEC filing excerpts matching '{req.Query}' were found for ticker '{req.Ticker ?? "ALL"}'.",
                            Sources = new List<SourceCitation>(),
                            Confidence = 0.0
                        };
                        sw.Stop();
                        await auditLog.RecordAsync("search_financial_filings", "agent-orchestrator", req, empty, (int)sw.ElapsedMilliseconds, true, null, ct);
                        return JsonSerializer.Serialize(empty, JsonOpts);
                    }

                    var citations = chunks.Select(c => new SourceCitation
                    {
                        Ticker = c.Ticker,
                        FilingType = c.FilingType,
                        FiscalYear = c.FiscalYear,
                        Section = c.Section,
                        ChunkIndex = c.ChunkIndex,
                        Excerpt = c.Content.Length > 250 ? c.Content[..250] + "..." : c.Content
                    }).ToList();

                    var contextBuilder = new System.Text.StringBuilder();
                    for (int cIdx = 0; cIdx < chunks.Count; cIdx++)
                    {
                        contextBuilder.AppendLine($"[Source {cIdx + 1}] ({chunks[cIdx].Ticker} {chunks[cIdx].FilingType} FY{chunks[cIdx].FiscalYear} - {chunks[cIdx].Section}):");
                        contextBuilder.AppendLine(chunks[cIdx].Content);
                        contextBuilder.AppendLine();
                    }

                    var systemPrompt = """
                        You are an expert enterprise financial analyst and auditor.
                        Answer the user's question accurately using ONLY the provided filing excerpts.
                        Cite the source filing, fiscal year, and section for all facts. If information is not in the sources, state so explicitly.
                        Keep the answer precise, factual, and professional.
                        """;

                    var userPrompt = $"""
                        Question: {req.Query}

                        Filing Excerpts:
                        {contextBuilder}

                        Provide a comprehensive, factual answer citing the sources:
                        """;

                    var answer = await llmClient.ChatAsync(systemPrompt, userPrompt, ct: ct);
                    var response = new SearchFilingsResponse
                    {
                        Answer = answer,
                        Sources = citations,
                        Confidence = Math.Round(Math.Min(1.0, 0.6 + (chunks.Count * 0.08)), 2)
                    };

                    sw.Stop();
                    await auditLog.RecordAsync("search_financial_filings", "agent-orchestrator", req, response, (int)sw.ElapsedMilliseconds, true, null, ct);
                    return JsonSerializer.Serialize(response, JsonOpts);
                }

                case "check_compliance":
                {
                    var req = JsonSerializer.Deserialize<CheckComplianceRequest>(arguments.GetRawText(), JsonOpts)
                              ?? throw new ToolSchemaValidationException(new Dictionary<string, string[]> { ["arguments"] = new[] { "Failed to deserialize check_compliance arguments." } });

                    var db = serviceProvider.GetRequiredService<AppDbContext>();
                    var rulesEngine = serviceProvider.GetRequiredService<ComplianceRulesEngine>();
                    var llmClient = serviceProvider.GetRequiredService<NvidiaLlmClient>();

                    var chunks = await db.DocumentChunks
                        .AsNoTracking()
                        .Where(c => c.Ticker == req.Ticker && c.FiscalYear == req.FiscalYear)
                        .OrderBy(c => c.ChunkIndex)
                        .Take(25)
                        .ToListAsync(ct);

                    var filingText = string.Join("\n", chunks.Select(c => c.Content));
                    var eval = rulesEngine.Evaluate(req.Framework, filingText);

                    var prompt = $"""
                        Company: {req.Ticker}
                        Fiscal Year: {req.FiscalYear}
                        Framework: {req.Framework}
                        Rules Evaluated: {eval.Checks.Count}
                        Computed Score: {eval.Score}% ({eval.OverallStatus})

                        Rule Findings:
                        {string.Join("\n", eval.Checks.Select(c => $"- [{c.Status}] {c.Title}: {c.Finding}"))}

                        Synthesize a professional 2-paragraph auditor compliance summary assessing {req.Ticker}'s adherence to {req.Framework}. Highlight key strengths and critical remediation actions.
                        """;

                    var summary = await llmClient.ChatAsync(
                        systemPrompt: "You are an enterprise regulatory compliance officer and external auditor.",
                        userMessage: prompt,
                        ct: ct);

                    var response = new CheckComplianceResponse
                    {
                        Ticker = req.Ticker,
                        Framework = req.Framework,
                        FiscalYear = req.FiscalYear,
                        OverallStatus = eval.OverallStatus,
                        Score = eval.Score,
                        Checks = eval.Checks.Select(c => new ComplianceCheckResult
                        {
                            RuleId = c.RuleId,
                            Title = c.Title,
                            Status = c.Status,
                            Finding = c.Finding,
                            Recommendation = c.Recommendation
                        }).ToList(),
                        Summary = summary
                    };

                    sw.Stop();
                    await auditLog.RecordAsync("check_compliance", "agent-orchestrator", req, response, (int)sw.ElapsedMilliseconds, true, null, ct);
                    return JsonSerializer.Serialize(response, JsonOpts);
                }

                case "generate_audit_report":
                {
                    var req = JsonSerializer.Deserialize<GenerateAuditReportRequest>(arguments.GetRawText(), JsonOpts)
                              ?? throw new ToolSchemaValidationException(new Dictionary<string, string[]> { ["arguments"] = new[] { "Failed to deserialize generate_audit_report arguments." } });

                    var db = serviceProvider.GetRequiredService<AppDbContext>();
                    var llmClient = serviceProvider.GetRequiredService<NvidiaLlmClient>();

                    var company = await db.Companies
                        .AsNoTracking()
                        .FirstOrDefaultAsync(c => c.Ticker == req.Ticker, ct);

                    var companyName = company?.CompanyName ?? req.Ticker;

                    var chunks = await db.DocumentChunks
                        .AsNoTracking()
                        .Where(c => c.Ticker == req.Ticker && c.FiscalYear == req.FiscalYear)
                        .Take(20)
                        .ToListAsync(ct);

                    var contextSnippet = chunks.Count > 0
                        ? string.Join("\n\n", chunks.Take(5).Select(c => $"[{c.Section}]: {c.Content}"))
                        : "No indexed filing excerpts available in the database.";

                    var reportType = req.ReportType.ToLowerInvariant();
                    var sections = new List<ReportSection>();

                    if (reportType is "executive-summary" or "full")
                    {
                        var execPrompt = $"""
                            Company: {companyName} ({req.Ticker})
                            Fiscal Year: {req.FiscalYear}
                            Context:
                            {contextSnippet}

                            Generate an executive summary of financial performance, strategic market position, and corporate direction.
                            """;

                        var execContent = await llmClient.ChatAsync(
                            "You are a senior Big 4 audit director. Produce an executive summary report.",
                            execPrompt,
                            ct: ct);

                        sections.Add(new ReportSection
                        {
                            Heading = "1. Executive Summary & Corporate Performance",
                            Content = execContent,
                            KeyFindings = new List<string>
                            {
                                $"Fiscal year {req.FiscalYear} disclosures reviewed for {companyName}.",
                                "Operational execution and revenue progression aligned with reported filings."
                            }
                        });
                    }

                    if (reportType is "risk-analysis" or "full")
                    {
                        var riskPrompt = $"""
                            Company: {companyName} ({req.Ticker})
                            Fiscal Year: {req.FiscalYear}
                            Context:
                            {contextSnippet}

                            Extract and analyze top operational, cybersecurity, macroeconomic, and competitive risk disclosures.
                            """;

                        var riskContent = await llmClient.ChatAsync(
                            "You are a chief risk officer and auditor. Produce a comprehensive risk disclosure breakdown.",
                            riskPrompt,
                            ct: ct);

                        sections.Add(new ReportSection
                        {
                            Heading = "2. Risk Factor & Regulatory Analysis",
                            Content = riskContent,
                            KeyFindings = new List<string>
                            {
                                "Key macroeconomic, supply chain, and regulatory risks identified in 10-K Item 1A.",
                                "Mitigation controls documented in risk management disclosures."
                            }
                        });
                    }

                    var response = new GenerateAuditReportResponse
                    {
                        Ticker = req.Ticker,
                        ReportType = req.ReportType,
                        Title = $"Enterprise Audit Report: {companyName} (FY{req.FiscalYear}) - {req.ReportType.ToUpperInvariant()}",
                        Sections = sections,
                        GeneratedAt = DateTime.UtcNow
                    };

                    sw.Stop();
                    await auditLog.RecordAsync("generate_audit_report", "agent-orchestrator", req, response, (int)sw.ElapsedMilliseconds, true, null, ct);
                    return JsonSerializer.Serialize(response, JsonOpts);
                }

                case "lookup_company":
                {
                    var req = JsonSerializer.Deserialize<LookupCompanyRequest>(arguments.GetRawText(), JsonOpts)
                              ?? throw new ToolSchemaValidationException(new Dictionary<string, string[]> { ["arguments"] = new[] { "Failed to deserialize lookup_company arguments." } });

                    var ticker = req.Ticker.Trim().ToUpperInvariant();
                    var db = serviceProvider.GetRequiredService<AppDbContext>();

                    var company = await db.Companies
                        .AsNoTracking()
                        .FirstOrDefaultAsync(c => c.Ticker == ticker, ct);

                    if (company == null)
                    {
                        throw new DocumentNotFoundException($"Company with ticker '{ticker}'");
                    }

                    var chunkCount = await db.DocumentChunks
                        .AsNoTracking()
                        .CountAsync(c => c.Ticker == ticker, ct);

                    var availableYears = await db.DocumentChunks
                        .AsNoTracking()
                        .Where(c => c.Ticker == ticker)
                        .Select(c => c.FiscalYear)
                        .Distinct()
                        .OrderBy(y => y)
                        .ToListAsync(ct);

                    var response = new LookupCompanyResponse
                    {
                        Ticker = company.Ticker,
                        CompanyName = company.CompanyName,
                        Sector = company.Sector,
                        Industry = company.Industry,
                        Cik = company.Cik,
                        Description = company.Description,
                        AvailableFilings = chunkCount,
                        AvailableFiscalYears = availableYears
                    };

                    sw.Stop();
                    await auditLog.RecordAsync("lookup_company", "agent-orchestrator", req, response, (int)sw.ElapsedMilliseconds, true, null, ct);
                    return JsonSerializer.Serialize(response, JsonOpts);
                }

                default:
                    throw new ToolNotFoundException(toolName);
            }
        }
        catch (Exception ex) when (ex is not ToolNotFoundException and not DocumentNotFoundException)
        {
            sw.Stop();
            await auditLog.RecordAsync(toolName, "agent-orchestrator", arguments.GetRawText(), null, (int)sw.ElapsedMilliseconds, false, ex.Message, ct);
            throw;
        }
    }

    private static string NormalizeToolName(string name)
    {
        var cleaned = name.Trim().ToLowerInvariant();
        return cleaned switch
        {
            "search_filings" or "searchfilings" or "search-filings" => "search_financial_filings",
            "checkcompliance" or "check-compliance" => "check_compliance",
            "generate_report" or "generatereport" or "generate-report" or "generate_audit_report" => "generate_audit_report",
            "lookupcompany" or "lookup-company" => "lookup_company",
            _ => cleaned
        };
    }
}
