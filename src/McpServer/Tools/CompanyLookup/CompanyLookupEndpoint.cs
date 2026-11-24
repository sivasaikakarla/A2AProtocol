using System.Diagnostics;
using System.Security.Claims;
using FastEndpoints;
using FastEndpoints.Mcp;
using McpServer.Data;
using McpServer.ErrorHandling;
using McpServer.Services;
using Microsoft.EntityFrameworkCore;

namespace McpServer.Tools.CompanyLookup;

public sealed class CompanyLookupEndpoint : Endpoint<LookupCompanyRequest, LookupCompanyResponse>
{
    private readonly AppDbContext _db;
    private readonly AuditLogService _auditLogService;

    public CompanyLookupEndpoint(AppDbContext db, AuditLogService auditLogService)
    {
        _db = db;
        _auditLogService = auditLogService;
    }

    public override void Configure()
    {
        Post("/api/tools/lookup-company");
        Roles("tool-executor", "admin");
        this.McpTool("lookup_company", "Looks up corporate metadata, SEC CIK numbers, industry classifications, and available indexed filing periods for a given stock ticker.");
        Description(b => b
            .Produces<LookupCompanyResponse>(200, "application/json")
            .ProducesProblemFE(422)
            .ProducesProblemFE(404)
            .ProducesProblemFE(401)
            .WithTags("MCP Tools"));
    }

    public override async Task HandleAsync(LookupCompanyRequest req, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var clientId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value
                       ?? "anonymous-client";

        try
        {
            var ticker = req.Ticker.Trim().ToUpperInvariant();

            var company = await _db.Companies
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Ticker == ticker, ct);

            if (company == null)
            {
                throw new DocumentNotFoundException($"Company with ticker '{ticker}'");
            }

            var chunkCount = await _db.DocumentChunks
                .AsNoTracking()
                .CountAsync(c => c.Ticker == ticker, ct);

            var availableYears = await _db.DocumentChunks
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
            await _auditLogService.RecordAsync(
                "lookup_company", clientId, req, response, (int)sw.ElapsedMilliseconds, true, null, ct);

            await Send.OkAsync(response, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            await _auditLogService.RecordAsync(
                "lookup_company", clientId, req, null, (int)sw.ElapsedMilliseconds, false, ex.Message, ct);
            throw;
        }
    }
}
