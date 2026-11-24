using FastEndpoints;
using FluentValidation;

namespace McpServer.Tools.FinancialSearch;

public sealed class SearchFilingsRequest
{
    public required string Query { get; set; }
    public string? Ticker { get; set; }
    public int? FiscalYear { get; set; }
    public int TopK { get; set; } = 5;
}

public sealed class SourceCitation
{
    public required string Ticker { get; set; }
    public required string FilingType { get; set; }
    public required int FiscalYear { get; set; }
    public required string Section { get; set; }
    public required int ChunkIndex { get; set; }
    public required string Excerpt { get; set; }
}

public sealed class SearchFilingsResponse
{
    public required string Answer { get; set; }
    public List<SourceCitation> Sources { get; set; } = new();
    public double Confidence { get; set; }
}

public sealed class SearchFilingsValidator : Validator<SearchFilingsRequest>
{
    public SearchFilingsValidator()
    {
        RuleFor(x => x.Query)
            .NotEmpty().WithMessage("Search query is required.")
            .MaximumLength(1000).WithMessage("Search query cannot exceed 1000 characters.");

        RuleFor(x => x.Ticker)
            .Matches(@"^[A-Z]{1,10}$").When(x => !string.IsNullOrEmpty(x.Ticker))
            .WithMessage("Ticker must be 1 to 10 uppercase alphabetical characters.");

        RuleFor(x => x.TopK)
            .InclusiveBetween(1, 20)
            .WithMessage("topK must be between 1 and 20.");

        RuleFor(x => x.FiscalYear)
            .InclusiveBetween(1990, 2030).When(x => x.FiscalYear.HasValue)
            .WithMessage("fiscalYear must be between 1990 and 2030.");
    }
}
