using System.Text.Json;
using McpServer.Data;
using McpServer.Data.Entities;

namespace McpServer.Services;

/// <summary>
/// Service responsible for recording execution audit trails for enterprise MCP tools into PostgreSQL.
/// </summary>
public sealed class AuditLogService
{
    private readonly AppDbContext _db;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(AppDbContext db, ILogger<AuditLogService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RecordAsync(
        string toolName,
        string? clientId,
        object? request,
        object? response,
        int durationMs,
        bool success,
        string? errorDetail = null,
        CancellationToken ct = default)
    {
        try
        {
            var requestJson = request != null ? JsonSerializer.Serialize(request) : null;
            var responseJson = response != null ? JsonSerializer.Serialize(response) : null;

            var logEntry = new AuditLog
            {
                ToolName = toolName,
                ClientId = clientId,
                Request = requestJson,
                Response = responseJson,
                DurationMs = durationMs,
                Success = success,
                ErrorDetail = errorDetail,
                Timestamp = DateTime.UtcNow
            };

            _db.AuditLogs.Add(logEntry);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Logging audit failures should not fail the primary tool response
            _logger.LogError(ex, "Failed to record audit log for tool '{ToolName}'.", toolName);
        }
    }
}
