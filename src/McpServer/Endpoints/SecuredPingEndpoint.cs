using System.Security.Claims;
using FastEndpoints;

namespace McpServer.Endpoints;

public sealed class SecuredPingEndpoint : EndpointWithoutRequest<SecuredPingResponse>
{
    public override void Configure()
    {
        Get("/api/secure/ping");
        Roles("tool-executor", "admin");
        Description(b => b
            .WithName("SecuredPing")
            .WithSummary("Protected endpoint requiring tool-executor role to verify JWT middleware"));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var clientId = User.FindFirst("client_id")?.Value
                       ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? "Unknown";

        var roles = User.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();

        await Send.OkAsync(new SecuredPingResponse
        {
            Message = "Authentication & Authorization succeeded",
            ClientId = clientId,
            Roles = roles,
            Timestamp = DateTime.UtcNow
        }, ct);
    }
}

public sealed class SecuredPingResponse
{
    public required string Message { get; init; }
    public required string ClientId { get; init; }
    public required string[] Roles { get; init; }
    public required DateTime Timestamp { get; init; }
}
