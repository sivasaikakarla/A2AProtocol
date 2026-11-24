using FastEndpoints;
using FluentValidation;

namespace McpServer.Authentication;

public sealed class GenerateTokenRequest
{
    public required string ClientId { get; init; }
    public string[] Roles { get; init; } = [];
}

public sealed class GenerateTokenResponse
{
    public required string Token { get; init; }
    public required string TokenType { get; init; } = "Bearer";
    public required int ExpiresInSeconds { get; init; }
}

public sealed class GenerateTokenValidator : Validator<GenerateTokenRequest>
{
    public GenerateTokenValidator()
    {
        RuleFor(x => x.ClientId)
            .NotEmpty().WithMessage("ClientId is required.")
            .MaximumLength(100).WithMessage("ClientId cannot exceed 100 characters.");
    }
}

public sealed class GenerateTokenEndpoint : Endpoint<GenerateTokenRequest, GenerateTokenResponse>
{
    private readonly TokenService _tokenService;

    public GenerateTokenEndpoint(TokenService tokenService)
    {
        _tokenService = tokenService;
    }

    public override void Configure()
    {
        Post("/auth/token");
        AllowAnonymous();
        Description(b => b
            .WithName("GenerateToken")
            .WithSummary("Issues a signed JWT token for agent or client authentication"));
    }

    public override async Task HandleAsync(GenerateTokenRequest req, CancellationToken ct)
    {
        var token = _tokenService.GenerateToken(req.ClientId, req.Roles);

        await Send.OkAsync(new GenerateTokenResponse
        {
            Token = token,
            TokenType = "Bearer",
            ExpiresInSeconds = 3600
        }, ct);
    }
}
