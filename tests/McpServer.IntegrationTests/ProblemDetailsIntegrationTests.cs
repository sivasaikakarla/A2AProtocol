using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace McpServer.IntegrationTests;

public class ProblemDetailsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ProblemDetailsIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetError_ToolNotFound_Returns404ProblemDetails()
    {
        // Act
        var response = await _client.GetAsync("/api/test/error?type=not-found");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        Assert.Equal(404, root.GetProperty("status").GetInt32());
        Assert.Equal("https://mcpserver.dev/problems/tool-not-found", root.GetProperty("type").GetString());
        Assert.Equal("ToolNotFound", root.GetProperty("title").GetString());
        Assert.Contains("financial_search", root.GetProperty("detail").GetString());
        Assert.True(root.TryGetProperty("traceId", out _));
        Assert.True(root.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public async Task GetError_SchemaValidation_Returns422WithValidationErrors()
    {
        // Act
        var response = await _client.GetAsync("/api/test/error?type=validation");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        Assert.Equal(422, root.GetProperty("status").GetInt32());
        Assert.Equal("https://mcpserver.dev/problems/schema-validation-failed", root.GetProperty("type").GetString());
        Assert.True(root.TryGetProperty("validationErrors", out var validationErrors));
        Assert.True(validationErrors.TryGetProperty("query", out _));
        Assert.True(validationErrors.TryGetProperty("topK", out _));
    }

    [Fact]
    public async Task GetError_LlmProvider_Returns502BadGateway()
    {
        // Act
        var response = await _client.GetAsync("/api/test/error?type=llm");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        Assert.Equal(502, root.GetProperty("status").GetInt32());
        Assert.Equal("https://mcpserver.dev/problems/llm-provider-error", root.GetProperty("type").GetString());
        Assert.Contains("NVIDIA NIM", root.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task GetError_Timeout_Returns504GatewayTimeout()
    {
        // Act
        var response = await _client.GetAsync("/api/test/error?type=timeout");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        Assert.Equal(504, root.GetProperty("status").GetInt32());
        Assert.Equal("https://mcpserver.dev/problems/tool-execution-timeout", root.GetProperty("type").GetString());
    }

    [Fact]
    public async Task GetError_Forbidden_Returns403Forbidden()
    {
        // Act
        var response = await _client.GetAsync("/api/test/error?type=forbidden");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        Assert.Equal(403, root.GetProperty("status").GetInt32());
        Assert.Equal("https://mcpserver.dev/problems/tool-access-denied", root.GetProperty("type").GetString());
    }

    [Fact]
    public async Task GetError_UnhandledException_SanitizesMessageAndReturns500WithoutLeaking()
    {
        // Act
        var response = await _client.GetAsync("/api/test/error?type=generic");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        Assert.Equal(500, root.GetProperty("status").GetInt32());
        Assert.Equal("https://mcpserver.dev/problems/internal-error", root.GetProperty("type").GetString());
        Assert.Equal("Internal Server Error", root.GetProperty("title").GetString());
        Assert.Equal("An unexpected error occurred. Please try again later.", root.GetProperty("detail").GetString());
        // Internal exception message must NOT be leaked
        Assert.DoesNotContain("An unhandled system failure occurred during processing", content);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_Returns401ProblemDetails()
    {
        // Act
        var response = await _client.GetAsync("/api/secure/ping");
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var jsonDoc = JsonDocument.Parse(content);
        var root = jsonDoc.RootElement;

        Assert.Equal(401, root.GetProperty("status").GetInt32());
        Assert.Equal("https://mcpserver.dev/problems/unauthorized", root.GetProperty("type").GetString());
        Assert.Equal("Unauthorized", root.GetProperty("title").GetString());
        Assert.True(root.TryGetProperty("traceId", out _));
        Assert.True(root.TryGetProperty("timestamp", out _));
    }
}
