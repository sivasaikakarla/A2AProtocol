using McpServer.Tools.AuditReportGenerator;
using McpServer.Tools.CompanyLookup;
using McpServer.Tools.ComplianceChecker;
using McpServer.Tools.FinancialSearch;
using Xunit;

namespace McpServer.UnitTests.Tools;

public class ToolValidatorsTests
{
    [Fact]
    public void SearchFilingsValidator_ValidRequest_PassesValidation()
    {
        var validator = new SearchFilingsValidator();
        var request = new SearchFilingsRequest
        {
            Query = "What were data center revenues?",
            Ticker = "NVDA",
            FiscalYear = 2024,
            TopK = 5
        };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("", "Search query is required.")]
    [InlineData("   ", "Search query is required.")]
    public void SearchFilingsValidator_EmptyQuery_FailsValidation(string query, string expectedError)
    {
        var validator = new SearchFilingsValidator();
        var request = new SearchFilingsRequest { Query = query, TopK = 5 };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains(expectedError));
    }

    [Fact]
    public void SearchFilingsValidator_QueryExceeding1000Chars_FailsValidation()
    {
        var validator = new SearchFilingsValidator();
        var request = new SearchFilingsRequest
        {
            Query = new string('A', 1001),
            TopK = 5
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("cannot exceed 1000"));
    }

    [Theory]
    [InlineData("nvda")] // Lowercase
    [InlineData("NVDA123!")] // Special chars
    [InlineData("VERYLONGTICKERNAME")] // Exceeds 10 chars
    public void SearchFilingsValidator_InvalidTicker_FailsValidation(string ticker)
    {
        var validator = new SearchFilingsValidator();
        var request = new SearchFilingsRequest
        {
            Query = "Valid query",
            Ticker = ticker,
            TopK = 5
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Ticker");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(21)]
    [InlineData(-1)]
    public void SearchFilingsValidator_InvalidTopK_FailsValidation(int topK)
    {
        var validator = new SearchFilingsValidator();
        var request = new SearchFilingsRequest { Query = "Valid query", TopK = topK };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "TopK");
    }

    [Fact]
    public void CheckComplianceValidator_ValidRequest_PassesValidation()
    {
        var validator = new CheckComplianceValidator();
        var request = new CheckComplianceRequest
        {
            Ticker = "AAPL",
            FiscalYear = 2024,
            Framework = "SOX-404"
        };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("HIPAA")]
    [InlineData("GDPR")]
    [InlineData("PCI-DSS")]
    public void CheckComplianceValidator_UnsupportedFramework_FailsValidation(string framework)
    {
        var validator = new CheckComplianceValidator();
        var request = new CheckComplianceRequest
        {
            Ticker = "AAPL",
            FiscalYear = 2024,
            Framework = framework
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Framework");
    }

    [Fact]
    public void GenerateAuditReportValidator_ValidRequest_PassesValidation()
    {
        var validator = new GenerateAuditReportValidator();
        var request = new GenerateAuditReportRequest
        {
            Ticker = "MSFT",
            FiscalYear = 2023,
            ReportType = "executive-summary"
        };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void GenerateAuditReportValidator_InvalidReportType_FailsValidation()
    {
        var validator = new GenerateAuditReportValidator();
        var request = new GenerateAuditReportRequest
        {
            Ticker = "MSFT",
            FiscalYear = 2023,
            ReportType = "unsupported-custom-type"
        };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ReportType");
    }

    [Fact]
    public void LookupCompanyValidator_ValidTicker_PassesValidation()
    {
        var validator = new LookupCompanyValidator();
        var request = new LookupCompanyRequest { Ticker = "GOOGL" };

        var result = validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid_ticker")]
    public void LookupCompanyValidator_InvalidTicker_FailsValidation(string ticker)
    {
        var validator = new LookupCompanyValidator();
        var request = new LookupCompanyRequest { Ticker = ticker };

        var result = validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Ticker");
    }
}
