namespace McpServer.Data.Entities;

/// <summary>
/// Audit trail record for all MCP tool executions, tracking invocation parameters,
/// client identity, latency, and success/failure details for enterprise compliance.
/// </summary>
public sealed class AuditLog
{
    public long Id { get; set; }

    /// <summary>
    /// Name of the invoked MCP tool (e.g. "financial_search", "compliance_checker").
    /// </summary>
    public required string ToolName { get; set; }

    /// <summary>
    /// Identifier of the caller / authenticated client principal.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Serialized JSON payload of the request arguments.
    /// </summary>
    public string? Request { get; set; }

    /// <summary>
    /// Serialized JSON payload of the tool output response.
    /// </summary>
    public string? Response { get; set; }

    /// <summary>
    /// Execution duration in milliseconds.
    /// </summary>
    public int DurationMs { get; set; }

    /// <summary>
    /// Indicates whether the tool execution completed successfully without error.
    /// </summary>
    public bool Success { get; set; } = true;

    /// <summary>
    /// Error message or failure details if the execution failed.
    /// </summary>
    public string? ErrorDetail { get; set; }

    /// <summary>
    /// UTC timestamp of the tool execution.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
