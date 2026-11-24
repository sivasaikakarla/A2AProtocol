using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace McpServer.ErrorHandling;

/// <summary>
/// Global exception handler implementing ASP.NET Core's IExceptionHandler interface.
/// Intercepts all unhandled exceptions and writes RFC 7807 (application/problem+json) responses.
/// Ensures server stack traces are never leaked to clients while logging full details server-side.
/// </summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService ?? throw new ArgumentNullException(nameof(problemDetailsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Unhandled exception during HTTP request {Path}: {Message}",
            httpContext.Request.Path, exception.Message);

        var (statusCode, title, problemType, detail) = exception switch
        {
            DomainException domainEx => (
                domainEx.StatusCode,
                domainEx.GetType().Name.Replace("Exception", string.Empty),
                domainEx.ProblemType,
                domainEx.Message
            ),
            _ => (
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                "https://mcpserver.dev/problems/internal-error",
                "An unexpected error occurred. Please try again later."
            )
        };

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = problemType,
            Detail = detail,
            Instance = httpContext.Request.Path
        };

        // If this is a validation failure, attach the structured dictionary
        if (exception is ToolSchemaValidationException validationEx)
        {
            problemDetails.Extensions["validationErrors"] = validationEx.ValidationErrors;
        }

        // Trace correlation and timestamp
        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;
        problemDetails.Extensions["timestamp"] = DateTime.UtcNow.ToString("O");

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";

        var handled = await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = problemDetails
        });

        if (!handled)
        {
            await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken: cancellationToken);
        }

        return true;
    }
}
