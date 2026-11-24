using System.Diagnostics;
using System.Security.Claims;
using FastEndpoints;
using FastEndpoints.Mcp;
using McpServer.Agents;
using McpServer.Services;

namespace McpServer.Tools.FinancialSearch;

public sealed class SearchFilingsEndpoint : Endpoint<SearchFilingsRequest, SearchFilingsResponse>
{
    private readonly VectorSearchService _searchService;
    private readonly NvidiaLlmClient _llmClient;
    private readonly AuditLogService _auditLogService;

    public SearchFilingsEndpoint(
        VectorSearchService searchService,
        NvidiaLlmClient llmClient,
        AuditLogService auditLogService)
    {
        _searchService = searchService;
        _llmClient = llmClient;
        _auditLogService = auditLogService;
    }

    public override void Configure()
    {
        Post("/api/tools/search-filings");
        Roles("tool-executor", "admin");
        this.McpTool("search_financial_filings", "Executes semantic RAG vector search across regulatory SEC 10-K filings using pgvector and synthesizes grounded answers with citations.");
        Description(b => b
            .Produces<SearchFilingsResponse>(200, "application/json")
            .ProducesProblemFE(422)
            .ProducesProblemFE(401)
            .WithTags("MCP Tools"));
    }

    public override async Task HandleAsync(SearchFilingsRequest req, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var clientId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value
                       ?? "anonymous-client";

        try
        {
            // 1. Vector similarity search over pgvector
            var chunks = await _searchService.SearchAsync(
                query: req.Query,
                ticker: req.Ticker,
                fiscalYear: req.FiscalYear,
                topK: req.TopK,
                ct: ct);

            if (chunks.Count == 0)
            {
                var emptyResponse = new SearchFilingsResponse
                {
                    Answer = $"No regulatory SEC filing excerpts matching '{req.Query}' were found for ticker '{req.Ticker ?? "ALL"}'.",
                    Sources = new List<SourceCitation>(),
                    Confidence = 0.0
                };

                sw.Stop();
                await _auditLogService.RecordAsync(
                    "search_financial_filings", clientId, req, emptyResponse, (int)sw.ElapsedMilliseconds, true, null, ct);

                await Send.OkAsync(emptyResponse, ct);
                return;
            }

            // 2. Format source citations
            var citations = chunks.Select(c => new SourceCitation
            {
                Ticker = c.Ticker,
                FilingType = c.FilingType,
                FiscalYear = c.FiscalYear,
                Section = c.Section,
                ChunkIndex = c.ChunkIndex,
                Excerpt = c.Content.Length > 250 ? c.Content[..250] + "..." : c.Content
            }).ToList();

            // 3. Grounded LLM reasoning prompt
            var contextBuilder = new System.Text.StringBuilder();
            for (int i = 0; i < chunks.Count; i++)
            {
                contextBuilder.AppendLine($"[Source {i + 1}] ({chunks[i].Ticker} {chunks[i].FilingType} FY{chunks[i].FiscalYear} - {chunks[i].Section}):");
                contextBuilder.AppendLine(chunks[i].Content);
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

                Provide a structured synthesis answering the question based strictly on the excerpts above.
                """;

            var answer = await _llmClient.ChatAsync(systemPrompt, userPrompt, ct: ct);

            var response = new SearchFilingsResponse
            {
                Answer = answer,
                Sources = citations,
                Confidence = Math.Round(Math.Min(1.0, 0.70 + (chunks.Count * 0.05)), 2)
            };

            sw.Stop();
            await _auditLogService.RecordAsync(
                "search_financial_filings", clientId, req, response, (int)sw.ElapsedMilliseconds, true, null, ct);

            await Send.OkAsync(response, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            await _auditLogService.RecordAsync(
                "search_financial_filings", clientId, req, null, (int)sw.ElapsedMilliseconds, false, ex.Message, ct);
            throw;
        }
    }
}
