using System.Diagnostics;
using System.Security.Claims;
using FastEndpoints;
using FastEndpoints.Mcp;
using FluentValidation;
using McpServer.Agents;
using McpServer.Services;

namespace McpServer.Endpoints;

public sealed class RunAgentValidator : Validator<AgentRequest>
{
    public RunAgentValidator()
    {
        RuleFor(x => x.Request)
            .NotEmpty().WithMessage("Request is required.")
            .MaximumLength(4000).WithMessage("Request cannot exceed 4000 characters.");

        RuleFor(x => x.MaxIterations)
            .InclusiveBetween(1, 10).WithMessage("MaxIterations must be between 1 and 10.");
    }
}

public sealed class RunAgentEndpoint : Endpoint<AgentRequest, AgentResponse>
{
    private readonly AgentOrchestrator _orchestrator;
    private readonly AuditLogService _auditLogService;

    public RunAgentEndpoint(AgentOrchestrator orchestrator, AuditLogService auditLogService)
    {
        _orchestrator = orchestrator;
        _auditLogService = auditLogService;
    }

    public override void Configure()
    {
        Post("/api/agent/run");
        Roles("tool-executor", "admin");
        this.McpTool("run_agent", "Autonomous enterprise reasoning agent loop that coordinates multi-turn tool calling across regulatory filings and compliance.");
        Description(b => b
            .Produces<AgentResponse>(200, "application/json")
            .ProducesProblemFE(422)
            .ProducesProblemFE(401)
            .WithTags("Agent"));
    }

    public override async Task HandleAsync(AgentRequest req, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var clientId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? User.FindFirst("sub")?.Value
                       ?? "anonymous-client";

        var response = await _orchestrator.RunAsync(req, ct);
        sw.Stop();

        await _auditLogService.RecordAsync(
            "run_agent",
            clientId,
            req,
            response,
            (int)sw.ElapsedMilliseconds,
            response.Success,
            response.ErrorMessage,
            ct);

        await Send.OkAsync(response, ct);
    }
}
