using System.Diagnostics;
using System.Security.Claims;
using FastEndpoints;
using McpServer.Agents;
using McpServer.Data;
using McpServer.Services;
using Microsoft.EntityFrameworkCore;

namespace McpServer.Tools.ComplianceChecker;

public sealed class ComplianceCheckerEndpoint : Endpoint<CheckComplianceRequest, CheckComplianceResponse>
{
    private readonly AppDbContext _db;
    private readonly ComplianceRulesEngine _rulesEngine;
    private readonly NvidiaLlmClient _llmClient;
    private readonly AuditLogService _auditLogService;

    public ComplianceCheckerEndpoint(
        AppDbContext db,
        ComplianceRulesEngine rulesEngine,
        NvidiaLlmClient llmClient,
        AuditLogService auditLogService)
    {
        _db = db;
        _rulesEngine = rulesEngine;
        _llmClient = llmClient;
        _auditLogService = auditLogService;
    }

    public override void Configure()
    {
        Post("/api/tools/check-compliance");
        Roles("tool-executor", "admin");
        Description(b => b
            .Produces<CheckComplianceResponse>(200, "application/json")
            .ProducesProblemFE(422)
            .ProducesProblemFE(401)
            .WithTags("MCP Tools"));
    }

    public override async Task HandleAsync(CheckComplianceRequest req, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var clientId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value
                       ?? "anonymous-client";

        try
        {
            // 1. Fetch relevant filing text chunks for the company and fiscal year
            var chunks = await _db.DocumentChunks
                .AsNoTracking()
                .Where(c => c.Ticker == req.Ticker && c.FiscalYear == req.FiscalYear)
                .OrderBy(c => c.ChunkIndex)
                .Take(25)
                .ToListAsync(ct);

            var filingText = string.Join("\n", chunks.Select(c => c.Content));

            // 2. Evaluate against regulatory framework rule templates
            var eval = _rulesEngine.Evaluate(req.Framework, filingText);

            // 3. Synthesize auditor executive summary via LLM
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

            var summary = await _llmClient.ChatAsync(
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
            await _auditLogService.RecordAsync(
                "check_compliance", clientId, req, response, (int)sw.ElapsedMilliseconds, true, null, ct);

            await Send.OkAsync(response, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            await _auditLogService.RecordAsync(
                "check_compliance", clientId, req, null, (int)sw.ElapsedMilliseconds, false, ex.Message, ct);
            throw;
        }
    }
}
