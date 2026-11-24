namespace McpServer.ErrorHandling;

/// <summary>
/// Abstract base class for domain and operational exceptions in the MCP server.
/// Maps directly to RFC 7807 Problem Details fields (type, status, title).
/// </summary>
public abstract class DomainException : Exception
{
    /// <summary>
    /// A URI reference identifying the problem type (RFC 7807 "type" field).
    /// </summary>
    public abstract string ProblemType { get; }

    /// <summary>
    /// The HTTP status code this exception maps to.
    /// </summary>
    public abstract int StatusCode { get; }

    protected DomainException(string message) : base(message) { }
    protected DomainException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Thrown when a requested tool is not found in the MCP tool catalogue (HTTP 404).
/// </summary>
public sealed class ToolNotFoundException : DomainException
{
    public string ToolName { get; }
    public override string ProblemType => "https://mcpserver.dev/problems/tool-not-found";
    public override int StatusCode => StatusCodes.Status404NotFound;

    public ToolNotFoundException(string toolName)
        : base($"Tool '{toolName}' was not found in the tool catalogue.")
    {
        ToolName = toolName;
    }
}

/// <summary>
/// Thrown when tool or endpoint input arguments fail schema validation (HTTP 422).
/// </summary>
public sealed class ToolSchemaValidationException : DomainException
{
    public IDictionary<string, string[]> ValidationErrors { get; }
    public override string ProblemType => "https://mcpserver.dev/problems/schema-validation-failed";
    public override int StatusCode => StatusCodes.Status422UnprocessableEntity;

    public ToolSchemaValidationException(IDictionary<string, string[]> validationErrors)
        : base("Tool input failed schema validation.")
    {
        ValidationErrors = validationErrors;
    }
}

/// <summary>
/// Thrown when an external LLM provider (such as NVIDIA NIM) is unavailable or encounters an error (HTTP 502).
/// </summary>
public sealed class LlmProviderException : DomainException
{
    public override string ProblemType => "https://mcpserver.dev/problems/llm-provider-error";
    public override int StatusCode => StatusCodes.Status502BadGateway;

    public LlmProviderException(string message, Exception? inner = null)
        : base($"LLM provider error: {message}", inner!) { }
}

/// <summary>
/// Thrown when tool execution exceeds its allotted execution timeout (HTTP 504).
/// </summary>
public sealed class ToolExecutionTimeoutException : DomainException
{
    public string ToolName { get; }
    public TimeSpan Timeout { get; }
    public override string ProblemType => "https://mcpserver.dev/problems/tool-execution-timeout";
    public override int StatusCode => StatusCodes.Status504GatewayTimeout;

    public ToolExecutionTimeoutException(string toolName, TimeSpan timeout)
        : base($"Tool '{toolName}' exceeded the maximum execution timeout of {timeout.TotalSeconds} seconds.")
    {
        ToolName = toolName;
        Timeout = timeout;
    }
}

/// <summary>
/// Thrown when an authenticated client does not have permissions to execute a specific tool (HTTP 403).
/// </summary>
public sealed class ToolAccessDeniedException : DomainException
{
    public string ToolName { get; }
    public string ClientId { get; }
    public override string ProblemType => "https://mcpserver.dev/problems/tool-access-denied";
    public override int StatusCode => StatusCodes.Status403Forbidden;

    public ToolAccessDeniedException(string toolName, string clientId)
        : base($"Client '{clientId}' is not authorized to execute tool '{toolName}'.")
    {
        ToolName = toolName;
        ClientId = clientId;
    }
}

/// <summary>
/// Thrown when a client exceeds the tool invocation rate limit (HTTP 429).
/// </summary>
public sealed class ToolRateLimitExceededException : DomainException
{
    public string ToolName { get; }
    public int RetryAfterSeconds { get; }
    public override string ProblemType => "https://mcpserver.dev/problems/rate-limit-exceeded";
    public override int StatusCode => StatusCodes.Status429TooManyRequests;

    public ToolRateLimitExceededException(string toolName, int retryAfterSeconds)
        : base($"Rate limit exceeded for tool '{toolName}'. Retry after {retryAfterSeconds} seconds.")
    {
        ToolName = toolName;
        RetryAfterSeconds = retryAfterSeconds;
    }
}

/// <summary>
/// Thrown when a requested document or SEC filing chunk is not found (HTTP 404).
/// </summary>
public sealed class DocumentNotFoundException : DomainException
{
    public string DocumentId { get; }
    public override string ProblemType => "https://mcpserver.dev/problems/document-not-found";
    public override int StatusCode => StatusCodes.Status404NotFound;

    public DocumentNotFoundException(string documentId)
        : base($"Document '{documentId}' was not found in the vector store.")
    {
        DocumentId = documentId;
    }
}
