using FastEndpoints;
using FluentValidation;

namespace McpServer.Tools.AuditReportGenerator;

public sealed class GenerateAuditReportRequest
{
    public required string Ticker { get; set; }
    public int FiscalYear { get; set; }
    public required string ReportType { get; set; }
}

public sealed class ReportSection
{
    public required string Heading { get; set; }
    public required string Content { get; set; }
    public List<string> KeyFindings { get; set; } = new();
}

public sealed class GenerateAuditReportResponse
{
    public required string Ticker { get; set; }
    public required string ReportType { get; set; }
    public required string Title { get; set; }
    public List<ReportSection> Sections { get; set; } = new();
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}

public sealed class GenerateAuditReportValidator : Validator<GenerateAuditReportRequest>
{
    private static readonly string[] AllowedTypes = { "executive-summary", "risk-analysis", "full" };

    public GenerateAuditReportValidator()
    {
        RuleFor(x => x.Ticker)
            .NotEmpty().WithMessage("Ticker is required.")
            .Matches(@"^[A-Z]{1,10}$").WithMessage("Ticker must be 1 to 10 uppercase alphabetical characters.");

        RuleFor(x => x.FiscalYear)
            .InclusiveBetween(1990, 2030)
            .WithMessage("fiscalYear must be between 1990 and 2030.");

        RuleFor(x => x.ReportType)
            .NotEmpty().WithMessage("ReportType is required.")
            .Must(t => AllowedTypes.Contains(t?.ToLowerInvariant()))
            .WithMessage($"ReportType must be one of: {string.Join(", ", AllowedTypes)}.");
    }
}
