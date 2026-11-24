using FastEndpoints;
using McpServer.ErrorHandling;

namespace McpServer.Endpoints;

public sealed class ErrorSimulationRequest
{
    public string? Type { get; set; }
}

/// <summary>
/// Diagnostic endpoint for verifying RFC 7807 Problem Details error responses.
/// Allowed anonymously for diagnostic and automated integration testing.
/// </summary>
public sealed class ErrorSimulationEndpoint : Endpoint<ErrorSimulationRequest>
{
    public override void Configure()
    {
        Get("/api/test/error");
        AllowAnonymous();
    }

    public override Task HandleAsync(ErrorSimulationRequest req, CancellationToken ct)
    {
        var errorType = req.Type?.ToLowerInvariant() ?? "generic";

        switch (errorType)
        {
            case "not-found":
                throw new ToolNotFoundException("financial_search");

            case "validation":
                throw new ToolSchemaValidationException(new Dictionary<string, string[]>
                {
                    ["query"] = new[] { "Query string cannot be empty.", "Query must be at least 3 characters." },
                    ["topK"] = new[] { "topK must be between 1 and 20." }
                });

            case "timeout":
                throw new ToolExecutionTimeoutException("compliance_audit", TimeSpan.FromSeconds(30));

            case "forbidden":
                throw new ToolAccessDeniedException("confidential_financial_tool", "client-app-123");

            case "llm":
                throw new LlmProviderException("NVIDIA NIM upstream endpoint returned HTTP 503 Service Unavailable");

            case "rate-limit":
                throw new ToolRateLimitExceededException("financial_search", 60);

            case "document-not-found":
                throw new DocumentNotFoundException("sec-10k-chunk-999");

            case "generic":
            default:
                throw new InvalidOperationException("An unhandled system failure occurred during processing.");
        }
    }
}
