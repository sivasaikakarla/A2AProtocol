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

namespace McpServer.IntegrationTests;

public class McpToolExecutionTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private string _jwtToken = string.Empty;

    public McpToolExecutionTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        var tokenRes = await _client.PostAsJsonAsync("/auth/token", new
        {
            clientId = "mcp-tool-execution-suite",
            roles = new[] { "tool-executor", "admin" }
        });
        tokenRes.EnsureSuccessStatusCode();
        var doc = JsonDocument.Parse(await tokenRes.Content.ReadAsStringAsync());
        _jwtToken = doc.RootElement.GetProperty("token").GetString()!;

        // Ensure sample company exists in database
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (await db.Companies.FindAsync("MSFT") == null)
        {
            db.Companies.Add(new Company
            {
                Ticker = "MSFT",
                CompanyName = "Microsoft Corporation",
                Sector = "Technology",
                Industry = "Software—Infrastructure",
                Cik = "0000789019",
                Description = "Develops software, services, devices and solutions."
            });
            await db.SaveChangesAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpRequestMessage CreateAuthRequest(HttpMethod method, string url, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _jwtToken);
        if (body != null)
        {
            req.Content = JsonContent.Create(body);
        }
        return req;
    }

    [Fact]
    public async Task LookupCompanyTool_ViaHttp_ReturnsValidMetadataJson()
    {
        var req = CreateAuthRequest(HttpMethod.Post, "/api/tools/lookup-company", new { ticker = "MSFT" });
        var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<LookupCompanyResponse>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(json);
        Assert.Equal("MSFT", json.Ticker);
        Assert.Equal("Microsoft Corporation", json.CompanyName);
        Assert.Equal("0000789019", json.Cik);
    }

    [Fact]
    public async Task SearchFilingsTool_ViaHttp_ReturnsAnswerAndSources()
    {
        var req = CreateAuthRequest(HttpMethod.Post, "/api/tools/search-filings", new
        {
            query = "cloud services and intelligent cloud revenue",
            ticker = "MSFT",
            fiscalYear = 2024,
            topK = 3
        });
        var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<SearchFilingsResponse>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(json);
        Assert.False(string.IsNullOrWhiteSpace(json.Answer));
    }

    [Fact]
    public async Task CheckComplianceTool_ViaHttp_ReturnsAuditorEvaluation()
    {
        var req = CreateAuthRequest(HttpMethod.Post, "/api/tools/check-compliance", new
        {
            ticker = "MSFT",
            fiscalYear = 2024,
            framework = "SOX-404"
        });
        var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<CheckComplianceResponse>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(json);
        Assert.Equal("MSFT", json.Ticker);
        Assert.Equal("SOX-404", json.Framework);
        Assert.NotEmpty(json.Checks);
    }

    [Fact]
    public async Task GenerateAuditReportTool_ViaHttp_ReturnsSections()
    {
        var req = CreateAuthRequest(HttpMethod.Post, "/api/tools/generate-report", new
        {
            ticker = "MSFT",
            fiscalYear = 2024,
            reportType = "executive-summary"
        });
        var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadFromJsonAsync<GenerateAuditReportResponse>(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        Assert.NotNull(json);
        Assert.Equal("MSFT", json.Ticker);
        Assert.NotEmpty(json.Sections);
    }
}
