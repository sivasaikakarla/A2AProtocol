using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using McpServer.Data;
using McpServer.Data.Entities;
using McpServer.Tools.AuditReportGenerator;
using McpServer.Tools.CompanyLookup;
using McpServer.Tools.ComplianceChecker;
using McpServer.Tools.FinancialSearch;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace McpServer.IntegrationTests.Tools;

public class EnterpriseToolEndpointsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private string _validJwt = string.Empty;

    public EnterpriseToolEndpointsIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        // Obtain valid JWT with role "tool-executor"
        var tokenResponse = await _client.PostAsJsonAsync("/auth/token", new
        {
            clientId = "integration-test-suite",
            roles = new[] { "tool-executor" }
        });

        tokenResponse.EnsureSuccessStatusCode();
        var jsonDoc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        _validJwt = jsonDoc.RootElement.GetProperty("token").GetString()!;

        // Seed a test company in the database
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
                Description = "Designs, manufactures, and markets smartphones, personal computers, and services."
            });
            await db.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _validJwt);
        if (body != null)
        {
            request.Content = JsonContent.Create(body);
        }
        return request;
    }

    [Fact]
    public async Task ToolEndpoints_WithoutToken_Returns401Unauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/tools/lookup-company", new { ticker = "AAPL" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task LookupCompany_ExistingCompany_Returns200WithMetadata()
    {
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/tools/lookup-company", new
        {
            ticker = "AAPL"
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var data = await response.Content.ReadFromJsonAsync<LookupCompanyResponse>();
        Assert.NotNull(data);
        Assert.Equal("AAPL", data.Ticker);
        Assert.Equal("Apple Inc.", data.CompanyName);
        Assert.Equal("Consumer Electronics", data.Industry);
        Assert.Equal("0000320193", data.Cik);
    }

    [Fact]
    public async Task LookupCompany_InvalidTicker_Returns422ValidationProblem()
    {
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/tools/lookup-company", new
        {
            ticker = "invalid_lowercase"
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Ticker", content);
    }

    [Fact]
    public async Task LookupCompany_NonExistentCompany_Returns404ProblemDetails()
    {
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/tools/lookup-company", new
        {
            ticker = "NONEXIST"
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task SearchFilings_ValidationFailure_Returns422ProblemDetails()
    {
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/tools/search-filings", new
        {
            query = "", // Empty query fails validation
            topK = 50   // topK > 20 fails validation
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("query", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("topK", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckCompliance_InvalidFramework_Returns422ProblemDetails()
    {
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/tools/check-compliance", new
        {
            ticker = "AAPL",
            fiscalYear = 2024,
            framework = "NON-EXISTENT-REGULATION"
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Framework", content);
    }

    [Fact]
    public async Task GenerateAuditReport_InvalidType_Returns422ProblemDetails()
    {
        var request = CreateAuthenticatedRequest(HttpMethod.Post, "/api/tools/generate-report", new
        {
            ticker = "AAPL",
            fiscalYear = 2024,
            reportType = "invalid-type"
        });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("ReportType", content);
    }
}
