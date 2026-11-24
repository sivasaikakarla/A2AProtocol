using McpServer.Tools.FinancialSearch;
using Xunit;

namespace McpServer.UnitTests.Tools;

public class SearchFilingsValidatorTests
{
    private readonly SearchFilingsValidator _validator = new();

    [Fact]
    public void Validate_ValidRequest_Passes()
    {
        var request = new SearchFilingsRequest
        {
            Query = "What were the operating segments?",
            Ticker = "AAPL",
            FiscalYear = 2024,
            TopK = 5
        };

        var result = _validator.Validate(request);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_EmptyQuery_Fails(string query)
    {
        var request = new SearchFilingsRequest
        {
            Query = query,
            Ticker = "AAPL",
            TopK = 5
        };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchFilingsRequest.Query));
    }

    [Theory]
    [InlineData("aapl")] // lowercase
    [InlineData("AAPL123")] // numbers
    [InlineData("AAPL_TOOLONGTICKER")] // too long
    public void Validate_InvalidTicker_Fails(string ticker)
    {
        var request = new SearchFilingsRequest
        {
            Query = "Valid search query",
            Ticker = ticker,
            TopK = 5
        };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchFilingsRequest.Ticker));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public void Validate_TopKOutOfRange_Fails(int topK)
    {
        var request = new SearchFilingsRequest
        {
            Query = "Valid search query",
            Ticker = "AAPL",
            TopK = topK
        };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchFilingsRequest.TopK));
    }

    [Theory]
    [InlineData(1980)]
    [InlineData(2050)]
    public void Validate_FiscalYearOutOfRange_Fails(int fiscalYear)
    {
        var request = new SearchFilingsRequest
        {
            Query = "Valid search query",
            Ticker = "AAPL",
            FiscalYear = fiscalYear,
            TopK = 5
        };

        var result = _validator.Validate(request);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(SearchFilingsRequest.FiscalYear));
    }
}
