using FastEndpoints;

namespace McpServer.Endpoints;

public sealed class RootEndpoint : EndpointWithoutRequest<RootResponse>
{
    public override void Configure()
    {
        Get("/");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await Send.OkAsync(new RootResponse
        {
            Service = "A2A Enterprise MCP Server",
            Version = "1.0.0",
            Status = "Healthy"
        }, ct);
    }
}

public sealed class RootResponse
{
    public required string Service { get; init; }
    public required string Version { get; init; }
    public required string Status { get; init; }
}
