using FastEndpoints;
using FluentValidation;

namespace McpServer.Tools.CompanyLookup;

public sealed class LookupCompanyRequest
{
    public required string Ticker { get; set; }
}

public sealed class LookupCompanyResponse
{
    public required string Ticker { get; set; }
    public required string CompanyName { get; set; }
    public required string Sector { get; set; }
    public required string Industry { get; set; }
    public required string Cik { get; set; }
    public string? Description { get; set; }
    public int AvailableFilings { get; set; }
    public List<int> AvailableFiscalYears { get; set; } = new();
}

public sealed class LookupCompanyValidator : Validator<LookupCompanyRequest>
{
    public LookupCompanyValidator()
    {
        RuleFor(x => x.Ticker)
            .NotEmpty().WithMessage("Ticker is required.")
            .Matches(@"^[A-Z]{1,10}$").WithMessage("Ticker must be 1 to 10 uppercase alphabetical characters.");
    }
}
