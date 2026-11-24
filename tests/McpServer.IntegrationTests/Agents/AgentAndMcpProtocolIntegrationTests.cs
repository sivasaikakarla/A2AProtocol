using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using McpServer.Agents;
using McpServer.Data;
using McpServer.Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace McpServer.IntegrationTests.Agents;

public class AgentAndMcpProtocolIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private string _validJwt = string.Empty;
    private string _forbiddenJwt = string.Empty;

    public AgentAndMcpProtocolIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        // Obtain valid JWT with role "tool-executor"
        var tokenResponse = await _client.PostAsJsonAsync("/auth/token", new
        {
            clientId = "agent-integration-suite",
            roles = new[] { "tool-executor" }
        });
        tokenResponse.EnsureSuccessStatusCode();
        var jsonDoc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        _validJwt = jsonDoc.RootElement.GetProperty("token").GetString()!;

        // Obtain JWT with non-authorized role
        var forbiddenResponse = await _client.PostAsJsonAsync("/auth/token", new
        {
            clientId = "unauthorized-viewer",
            roles = new[] { "viewer-only" }
        });
        forbiddenResponse.EnsureSuccessStatusCode();
        var forbiddenJson = JsonDocument.Parse(await forbiddenResponse.Content.ReadAsStringAsync());
        _forbiddenJwt = forbiddenJson.RootElement.GetProperty("token").GetString()!;

        // Ensure Apple metadata is present in database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = await db.Companies.FindAsync("AAPL");
        if (existing == null)
        {
            db.Companies.Add(new Company
            {
                Ticker = "AAPL",
                CompanyName = "Apple Inc.",
                Sector = "Technology",
                Industry = "Consumer Electronics",
                Cik = "0000320193",
                Description = "Designs and manufactures consumer electronics and smartphones."
            });
            await db.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpRequestMessage CreateAuthenticatedRequest(string jwt, HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
        if (body != null)
        {
            request.Content = JsonContent.Create(body);
        }
        return request;
    }

    [Fact]
    public async Task RunAgent_WithoutAuthorizationHeader_Returns401Unauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/agent/run", new
        {
            request = "What are Apple's risk factors?"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task RunAgent_WithForbiddenRole_Returns403Forbidden()
    {
        var request = CreateAuthenticatedRequest(
            _forbiddenJwt,
            HttpMethod.Post,
            "/api/agent/run",
            new { request = "What are Apple's risk factors?" });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RunAgent_WithEmptyRequest_Returns422UnprocessableEntity()
    {
        var request = CreateAuthenticatedRequest(
            _validJwt,
            HttpMethod.Post,
            "/api/agent/run",
            new { request = "", maxIterations = 5 });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Request is required", content);
    }

    [Fact]
    public async Task RunAgent_WithValidRequest_ExecutesMultiTurnAndReturnsAnswer()
    {
        var request = CreateAuthenticatedRequest(
            _validJwt,
            HttpMethod.Post,
            "/api/agent/run",
            new
            {
                request = "Lookup company metadata for AAPL and report its CIK number.",
                maxIterations = 5
            });

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var agentResponse = await response.Content.ReadFromJsonAsync<AgentResponse>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(agentResponse);
        Assert.True(agentResponse.Success);
        Assert.False(string.IsNullOrWhiteSpace(agentResponse.FinalAnswer));
        Assert.True(agentResponse.Iterations >= 1);
    }

    [Fact]
    public async Task McpEndpoint_IsReachable_HandlesMcpDiscovery()
    {
        // MCP Streamable HTTP transport handshake or options/post
        var req = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Content = JsonContent.Create(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new
                {
                    name = "claude-client",
                    version = "1.0.0"
                }
            }
        });

        var response = await _client.SendAsync(req);

        // Endpoint is mapped and not 404
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
