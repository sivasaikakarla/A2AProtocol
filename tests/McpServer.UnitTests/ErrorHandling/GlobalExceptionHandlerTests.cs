using McpServer.ErrorHandling;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace McpServer.UnitTests.ErrorHandling;

public class GlobalExceptionHandlerTests
{
    private readonly Mock<IProblemDetailsService> _mockProblemDetailsService;
    private readonly Mock<ILogger<GlobalExceptionHandler>> _mockLogger;
    private readonly GlobalExceptionHandler _handler;

    public GlobalExceptionHandlerTests()
    {
        _mockProblemDetailsService = new Mock<IProblemDetailsService>();
        _mockLogger = new Mock<ILogger<GlobalExceptionHandler>>();
        _handler = new GlobalExceptionHandler(_mockProblemDetailsService.Object, _mockLogger.Object);

        // Setup TryWriteAsync to return true by default
        _mockProblemDetailsService
            .Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task TryHandleAsync_ToolNotFoundException_Returns404ProblemDetails()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/tools/execute";
        context.TraceIdentifier = "trace-404-test";
        var ex = new ToolNotFoundException("unknown_tool");
        ProblemDetails? capturedProblem = null;

        _mockProblemDetailsService
            .Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(c => capturedProblem = c.ProblemDetails)
            .ReturnsAsync(true);

        // Act
        var handled = await _handler.TryHandleAsync(context, ex, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.NotNull(capturedProblem);
        Assert.Equal(404, capturedProblem.Status);
        Assert.Equal("https://mcpserver.dev/problems/tool-not-found", capturedProblem.Type);
        Assert.Equal("ToolNotFound", capturedProblem.Title);
        Assert.Contains("unknown_tool", capturedProblem.Detail);
        Assert.Equal("/api/tools/execute", capturedProblem.Instance);
        Assert.Equal("trace-404-test", capturedProblem.Extensions["traceId"]);
        Assert.NotNull(capturedProblem.Extensions["timestamp"]);
    }

    [Fact]
    public async Task TryHandleAsync_ToolSchemaValidationException_Returns422WithValidationErrors()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/tools/validate";
        var errors = new Dictionary<string, string[]>
        {
            ["ticker"] = new[] { "Ticker symbol is required.", "Ticker must be uppercase." },
            ["period"] = new[] { "Period must be one of: FY, Q1, Q2, Q3." }
        };
        var ex = new ToolSchemaValidationException(errors);
        ProblemDetails? capturedProblem = null;

        _mockProblemDetailsService
            .Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(c => capturedProblem = c.ProblemDetails)
            .ReturnsAsync(true);

        // Act
        var handled = await _handler.TryHandleAsync(context, ex, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status422UnprocessableEntity, context.Response.StatusCode);
        Assert.NotNull(capturedProblem);
        Assert.Equal(422, capturedProblem.Status);
        Assert.Equal("https://mcpserver.dev/problems/schema-validation-failed", capturedProblem.Type);
        Assert.Equal("ToolSchemaValidation", capturedProblem.Title);
        Assert.True(capturedProblem.Extensions.ContainsKey("validationErrors"));
        var validationDict = capturedProblem.Extensions["validationErrors"] as IDictionary<string, string[]>;
        Assert.NotNull(validationDict);
        Assert.Equal(2, validationDict.Count);
        Assert.Equal(2, validationDict["ticker"].Length);
    }

    [Fact]
    public async Task TryHandleAsync_LlmProviderException_Returns502BadGateway()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/agent/chat";
        var ex = new LlmProviderException("NVIDIA NIM connection refused.");
        ProblemDetails? capturedProblem = null;

        _mockProblemDetailsService
            .Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(c => capturedProblem = c.ProblemDetails)
            .ReturnsAsync(true);

        // Act
        var handled = await _handler.TryHandleAsync(context, ex, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status502BadGateway, context.Response.StatusCode);
        Assert.NotNull(capturedProblem);
        Assert.Equal(502, capturedProblem.Status);
        Assert.Equal("https://mcpserver.dev/problems/llm-provider-error", capturedProblem.Type);
        Assert.Contains("NVIDIA NIM connection refused", capturedProblem.Detail);
    }

    [Fact]
    public async Task TryHandleAsync_ToolExecutionTimeoutException_Returns504GatewayTimeout()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var ex = new ToolExecutionTimeoutException("sox404_analyzer", TimeSpan.FromSeconds(45));
        ProblemDetails? capturedProblem = null;

        _mockProblemDetailsService
            .Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(c => capturedProblem = c.ProblemDetails)
            .ReturnsAsync(true);

        // Act
        var handled = await _handler.TryHandleAsync(context, ex, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status504GatewayTimeout, context.Response.StatusCode);
        Assert.NotNull(capturedProblem);
        Assert.Equal(504, capturedProblem.Status);
        Assert.Equal("https://mcpserver.dev/problems/tool-execution-timeout", capturedProblem.Type);
        Assert.Contains("45", capturedProblem.Detail);
    }

    [Fact]
    public async Task TryHandleAsync_ToolAccessDeniedException_Returns403Forbidden()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var ex = new ToolAccessDeniedException("audit_generator", "user-456");
        ProblemDetails? capturedProblem = null;

        _mockProblemDetailsService
            .Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(c => capturedProblem = c.ProblemDetails)
            .ReturnsAsync(true);

        // Act
        var handled = await _handler.TryHandleAsync(context, ex, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
        Assert.NotNull(capturedProblem);
        Assert.Equal(403, capturedProblem.Status);
        Assert.Equal("https://mcpserver.dev/problems/tool-access-denied", capturedProblem.Type);
        Assert.Contains("audit_generator", capturedProblem.Detail);
        Assert.Contains("user-456", capturedProblem.Detail);
    }

    [Fact]
    public async Task TryHandleAsync_ToolRateLimitExceededException_Returns429TooManyRequests()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var ex = new ToolRateLimitExceededException("financial_search", 30);
        ProblemDetails? capturedProblem = null;

        _mockProblemDetailsService
            .Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(c => capturedProblem = c.ProblemDetails)
            .ReturnsAsync(true);

        // Act
        var handled = await _handler.TryHandleAsync(context, ex, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        Assert.NotNull(capturedProblem);
        Assert.Equal(429, capturedProblem.Status);
        Assert.Equal("https://mcpserver.dev/problems/rate-limit-exceeded", capturedProblem.Type);
    }

    [Fact]
    public async Task TryHandleAsync_DocumentNotFoundException_Returns404NotFound()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var ex = new DocumentNotFoundException("nvda-10k-2023-chunk-1");
        ProblemDetails? capturedProblem = null;

        _mockProblemDetailsService
            .Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(c => capturedProblem = c.ProblemDetails)
            .ReturnsAsync(true);

        // Act
        var handled = await _handler.TryHandleAsync(context, ex, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.NotNull(capturedProblem);
        Assert.Equal(404, capturedProblem.Status);
        Assert.Equal("https://mcpserver.dev/problems/document-not-found", capturedProblem.Type);
        Assert.Contains("nvda-10k-2023-chunk-1", capturedProblem.Detail);
    }

    [Fact]
    public async Task TryHandleAsync_UnhandledGenericException_SanitizesMessageAndReturns500WithoutLeakingInternals()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/secret-internal-endpoint";
        var rawSensitiveError = "Connection string 'Server=internal-db;Password=secret' failed to open socket at 10.0.0.5:5432";
        var ex = new InvalidOperationException(rawSensitiveError);
        ProblemDetails? capturedProblem = null;

        _mockProblemDetailsService
            .Setup(s => s.TryWriteAsync(It.IsAny<ProblemDetailsContext>()))
            .Callback<ProblemDetailsContext>(c => capturedProblem = c.ProblemDetails)
            .ReturnsAsync(true);

        // Act
        var handled = await _handler.TryHandleAsync(context, ex, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.NotNull(capturedProblem);
        Assert.Equal(500, capturedProblem.Status);
        Assert.Equal("https://mcpserver.dev/problems/internal-error", capturedProblem.Type);
        Assert.Equal("Internal Server Error", capturedProblem.Title);
        // Sensitive connection string must NEVER be present in client-facing ProblemDetails Detail
        Assert.DoesNotContain("secret", capturedProblem.Detail);
        Assert.DoesNotContain("10.0.0.5", capturedProblem.Detail);
        Assert.Equal("An unexpected error occurred. Please try again later.", capturedProblem.Detail);
    }
}
