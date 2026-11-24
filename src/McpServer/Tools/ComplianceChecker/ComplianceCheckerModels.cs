using FastEndpoints;
using FluentValidation;
using McpServer.Services;

namespace McpServer.Tools.ComplianceChecker;

public sealed class CheckComplianceRequest
{
    public required string Ticker { get; set; }
    public int FiscalYear { get; set; }
    public required string Framework { get; set; }
}

public sealed class ComplianceCheckResult
{
    public required string RuleId { get; set; }
    public required string Title { get; set; }
    public required string Status { get; set; } // PASS, PARTIAL, FAIL
    public required string Finding { get; set; }
    public required string Recommendation { get; set; }
}

public sealed class CheckComplianceResponse
{
    public required string Ticker { get; set; }
    public required string Framework { get; set; }
    public int FiscalYear { get; set; }
    public required string OverallStatus { get; set; } // PASS, PARTIAL, FAIL
    public double Score { get; set; }
    public List<ComplianceCheckResult> Checks { get; set; } = new();
    public required string Summary { get; set; }
}

public sealed class CheckComplianceValidator : Validator<CheckComplianceRequest>
{
    public CheckComplianceValidator()
    {
        RuleFor(x => x.Ticker)
            .NotEmpty().WithMessage("Ticker is required.")
            .Matches(@"^[A-Z]{1,10}$").WithMessage("Ticker must be 1 to 10 uppercase alphabetical characters.");

        RuleFor(x => x.FiscalYear)
            .InclusiveBetween(1990, 2030)
            .WithMessage("fiscalYear must be between 1990 and 2030.");

        RuleFor(x => x.Framework)
            .NotEmpty().WithMessage("Framework is required.")
            .Must(f => ComplianceRulesEngine.SupportedFrameworks.Any(sf => string.Equals(sf, f, StringComparison.OrdinalIgnoreCase)))
            .WithMessage($"Framework must be one of: {string.Join(", ", ComplianceRulesEngine.SupportedFrameworks)}.");
    }
}
