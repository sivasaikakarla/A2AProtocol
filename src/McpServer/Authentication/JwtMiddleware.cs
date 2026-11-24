using Microsoft.Extensions.Options;

namespace McpServer.Authentication;

/// <summary>
/// Middleware that checks whether the incoming HTTP request path is in the ExcludedPaths list.
/// If excluded, request bypasses authentication checks.
/// Otherwise, ensures request passes through standard authentication and validates token presence.
/// </summary>
public sealed class JwtMiddleware
{
    private readonly RequestDelegate _next;
    private readonly JwtConfiguration _config;
    private readonly ILogger<JwtMiddleware> _logger;

    public JwtMiddleware(
        RequestDelegate next,
        IOptions<JwtConfiguration> config,
        ILogger<JwtMiddleware> logger)
    {
        _next = next;
        _config = config.Value;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Check if the current request path matches any excluded path
        if (IsPathExcluded(path))
        {
            _logger.LogDebug("Path '{Path}' is excluded from authentication enforcement.", path);
            await _next(context);
            return;
        }

        // For non-excluded paths, if no authorization header is provided, return 401 early
        if (!context.Request.Headers.ContainsKey("Authorization"))
        {
            _logger.LogWarning("Unauthorized access attempt to protected path '{Path}'.", path);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://mcpserver.dev/problems/unauthorized",
                title = "Unauthorized",
                status = 401,
                detail = $"Authentication is required to access '{path}'. Pass a valid Bearer token in the Authorization header.",
                instance = path
            });
            return;
        }

        await _next(context);
    }

    private bool IsPathExcluded(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        foreach (var excluded in _config.ExcludedPaths)
        {
            // Exact match (e.g. "/")
            if (string.Equals(path, excluded, StringComparison.OrdinalIgnoreCase))
                return true;

            // Prefix match for subpaths (e.g. "/health/ready" matches "/health")
            if (excluded != "/" && path.StartsWith(excluded, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
