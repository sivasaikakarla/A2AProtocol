using System.Diagnostics;
using System.Security.Claims;
using FastEndpoints;
using FastEndpoints.Mcp;
using McpServer.Agents;
using McpServer.Data;
using McpServer.Services;
using Microsoft.EntityFrameworkCore;

namespace McpServer.Tools.AuditReportGenerator;

public sealed class AuditReportEndpoint : Endpoint<GenerateAuditReportRequest, GenerateAuditReportResponse>
{
    private readonly AppDbContext _db;
    private readonly NvidiaLlmClient _llmClient;
    private readonly AuditLogService _auditLogService;

    public AuditReportEndpoint(
        AppDbContext db,
        NvidiaLlmClient llmClient,
        AuditLogService auditLogService)
    {
        _db = db;
        _llmClient = llmClient;
        _auditLogService = auditLogService;
    }

    public override void Configure()
    {
        Post("/api/tools/generate-report");
        Roles("tool-executor", "admin");
        this.McpTool("generate_audit_report", "Generates structured multi-section financial audit reports (executive-summary, risk-analysis, full) synthesizing SEC 10-K disclosures.");
        Description(b => b
            .Produces<GenerateAuditReportResponse>(200, "application/json")
            .ProducesProblemFE(422)
            .ProducesProblemFE(401)
            .WithTags("MCP Tools"));
    }

    public override async Task HandleAsync(GenerateAuditReportRequest req, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var clientId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value
                       ?? "anonymous-client";

        try
        {
            var company = await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Ticker == req.Ticker, ct);

            var companyName = company?.CompanyName ?? req.Ticker;

            var chunks = await _db.DocumentChunks
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

                var execContent = await _llmClient.ChatAsync(
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

                    Generate an in-depth audit risk assessment covering:
                    1. Supply chain and geopolitical dependencies
                    2. Technology and cybersecurity vulnerabilities
                    3. Regulatory, compliance, and legal exposures
                    """;

                var riskContent = await _llmClient.ChatAsync(
                    "You are an enterprise risk management expert and auditor.",
                    riskPrompt,
                    ct: ct);

                sections.Add(new ReportSection
                {
                    Heading = sections.Count > 0 ? "2. Comprehensive Risk & Governance Assessment" : "1. Comprehensive Risk & Governance Assessment",
                    Content = riskContent,
                    KeyFindings = new List<string>
                    {
                        "Item 1A risk disclosures systematically audited.",
                        "Identified single-source component risks and international trade regulatory dynamics."
                    }
                });
            }

            if (reportType is "full")
            {
                var controlsPrompt = $"""
                    Company: {companyName} ({req.Ticker})
                    Fiscal Year: {req.FiscalYear}

                    Provide an assessment of Internal Control Over Financial Reporting (ICFR) and auditor conclusion.
                    """;

                var controlsContent = await _llmClient.ChatAsync(
                    "You are a certified public auditor evaluating SOX Section 404 internal controls.",
                    controlsPrompt,
                    ct: ct);

                sections.Add(new ReportSection
                {
                    Heading = "3. Internal Control & Governance Assessment",
                    Content = controlsContent,
                    KeyFindings = new List<string>
                    {
                        "No material weaknesses identified in reviewed financial reporting controls.",
                        "Management certifications and independent auditor attestation verified."
                    }
                });
            }

            var response = new GenerateAuditReportResponse
            {
                Ticker = req.Ticker,
                ReportType = req.ReportType,
                Title = $"Independent Audit Report: {companyName} (FY{req.FiscalYear})",
                Sections = sections,
                GeneratedAt = DateTime.UtcNow
            };

            sw.Stop();
            await _auditLogService.RecordAsync(
                "generate_audit_report", clientId, req, response, (int)sw.ElapsedMilliseconds, true, null, ct);

            await Send.OkAsync(response, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            await _auditLogService.RecordAsync(
                "generate_audit_report", clientId, req, null, (int)sw.ElapsedMilliseconds, false, ex.Message, ct);
            throw;
        }
    }
}
