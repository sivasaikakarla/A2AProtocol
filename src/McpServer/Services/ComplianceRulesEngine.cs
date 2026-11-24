namespace McpServer.Services;

public sealed class ComplianceRuleDefinition
{
    public required string RuleId { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string TargetSection { get; set; }
    public required List<string> RequiredKeywords { get; set; }
}

public sealed class ComplianceCheckItem
{
    public required string RuleId { get; set; }
    public required string Title { get; set; }
    public required string Status { get; set; } // PASS, PARTIAL, FAIL
    public required string Finding { get; set; }
    public required string Recommendation { get; set; }
}

public sealed class ComplianceEvaluationResult
{
    public required string Framework { get; set; }
    public required string OverallStatus { get; set; } // PASS, PARTIAL, FAIL
    public required double Score { get; set; }
    public required List<ComplianceCheckItem> Checks { get; set; }
    public required string Summary { get; set; }
}

/// <summary>
/// Domain engine that manages regulatory compliance frameworks (SOX-404, SEC-Risk-Disclosure, Basel-III)
/// and assesses corporate filings against compliance rules.
/// </summary>
public sealed class ComplianceRulesEngine
{
    public static readonly IReadOnlyList<string> SupportedFrameworks = new[]
    {
        "SOX-404",
        "SEC-Risk-Disclosure",
        "Basel-III"
    };

    private static readonly Dictionary<string, List<ComplianceRuleDefinition>> FrameworkRules = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SOX-404"] = new()
        {
            new()
            {
                RuleId = "SOX-404-01",
                Title = "Management Assessment of Internal Control Over Financial Reporting (ICFR)",
                Description = "Verification that management assessed and certified the effectiveness of internal financial reporting controls.",
                TargetSection = "Item 9A",
                RequiredKeywords = new() { "internal control", "financial reporting", "effectiveness", "framework" }
            },
            new()
            {
                RuleId = "SOX-404-02",
                Title = "Independent Registered Public Accounting Firm Attestation",
                Description = "Auditor attestation report expressing an opinion on the effectiveness of ICFR.",
                TargetSection = "Item 9A",
                RequiredKeywords = new() { "independent registered public accounting firm", "attestation", "audit opinion" }
            },
            new()
            {
                RuleId = "SOX-404-03",
                Title = "Material Weakness & Significant Deficiency Disclosures",
                Description = "Explicit statement regarding whether any material weaknesses were identified during the fiscal period.",
                TargetSection = "Item 9A",
                RequiredKeywords = new() { "material weakness", "deficiency", "remediation" }
            }
        },
        ["SEC-Risk-Disclosure"] = new()
        {
            new()
            {
                RuleId = "SEC-RD-01",
                Title = "Cybersecurity Incident & Threat Governance",
                Description = "Disclosure of cybersecurity risks, board oversight, and response protocols under SEC Item 106.",
                TargetSection = "Item 1C",
                RequiredKeywords = new() { "cybersecurity", "threat", "board", "incident", "vulnerability" }
            },
            new()
            {
                RuleId = "SEC-RD-02",
                Title = "Supply Chain & Single-Source Concentration Risk",
                Description = "Identification and evaluation of single-source manufacturing and global supply disruption risks.",
                TargetSection = "Item 1A",
                RequiredKeywords = new() { "supplier", "supply chain", "single-source", "manufacturing" }
            },
            new()
            {
                RuleId = "SEC-RD-03",
                Title = "Geopolitical & Export Control Regulations",
                Description = "Evaluation of cross-border trade restrictions, tariffs, and export control regulations.",
                TargetSection = "Item 1A",
                RequiredKeywords = new() { "export control", "trade", "tariffs", "geopolitical", "regulations" }
            }
        },
        ["Basel-III"] = new()
        {
            new()
            {
                RuleId = "B3-01",
                Title = "Common Equity Tier 1 (CET1) Capital Ratio",
                Description = "Capital adequacy ratio measuring core equity capital against total risk-weighted assets.",
                TargetSection = "Item 7",
                RequiredKeywords = new() { "tier 1", "capital ratio", "risk-weighted assets", "cet1" }
            },
            new()
            {
                RuleId = "B3-02",
                Title = "Liquidity Coverage Ratio (LCR) Resilience",
                Description = "Proportion of highly liquid assets held to survive a 30-day severe liquidity stress scenario.",
                TargetSection = "Item 7",
                RequiredKeywords = new() { "liquidity coverage ratio", "high-quality liquid assets", "stress" }
            },
            new()
            {
                RuleId = "B3-03",
                Title = "Credit Risk Exposure & Counterparty Default Safeguards",
                Description = "Provisions for loan losses, expected credit loss (CECL), and counterparty credit risk mitigation.",
                TargetSection = "Item 8",
                RequiredKeywords = new() { "credit loss", "counterparty", "allowance", "credit risk" }
            }
        }
    };

    public bool IsFrameworkSupported(string framework) =>
        SupportedFrameworks.Any(f => string.Equals(f, framework, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ComplianceRuleDefinition> GetRules(string framework)
    {
        if (FrameworkRules.TryGetValue(framework, out var rules))
        {
            return rules;
        }

        return Array.Empty<ComplianceRuleDefinition>();
    }

    /// <summary>
    /// Evaluates text chunks against framework compliance rules using keyword heuristics and scoring.
    /// </summary>
    public ComplianceEvaluationResult Evaluate(string framework, string aggregatedFilingText)
    {
        var rules = GetRules(framework);
        if (rules.Count == 0)
        {
            return new ComplianceEvaluationResult
            {
                Framework = framework,
                OverallStatus = "FAIL",
                Score = 0.0,
                Checks = new List<ComplianceCheckItem>(),
                Summary = $"Unsupported compliance framework '{framework}'."
            };
        }

        var checks = new List<ComplianceCheckItem>();
        int passCount = 0;
        int partialCount = 0;

        foreach (var rule in rules)
        {
            int matchedKeywords = rule.RequiredKeywords.Count(kw =>
                aggregatedFilingText.Contains(kw, StringComparison.OrdinalIgnoreCase));

            double ratio = (double)matchedKeywords / rule.RequiredKeywords.Count;

            string status;
            string finding;
            string recommendation;

            if (ratio >= 0.75)
            {
                status = "PASS";
                passCount++;
                finding = $"Sufficient disclosure identified for {rule.Title}. Matched {matchedKeywords}/{rule.RequiredKeywords.Count} compliance indicators.";
                recommendation = "Continue maintaining current reporting rigor.";
            }
            else if (ratio >= 0.33)
            {
                status = "PARTIAL";
                partialCount++;
                finding = $"Partial disclosure found. Matched {matchedKeywords}/{rule.RequiredKeywords.Count} indicators for {rule.Title}.";
                recommendation = $"Enhance explicit disclosures in {rule.TargetSection} concerning {string.Join(", ", rule.RequiredKeywords)}.";
            }
            else
            {
                status = "FAIL";
                finding = $"Material gap identified. Insufficient disclosures regarding {rule.Title} (Matched {matchedKeywords}/{rule.RequiredKeywords.Count}).";
                recommendation = $"Urgent audit remediation required. Integrate formal disclosure into {rule.TargetSection}.";
            }

            checks.Add(new ComplianceCheckItem
            {
                RuleId = rule.RuleId,
                Title = rule.Title,
                Status = status,
                Finding = finding,
                Recommendation = recommendation
            });
        }

        double score = Math.Round(((passCount * 1.0) + (partialCount * 0.5)) / rules.Count * 100, 1);
        string overallStatus = passCount == rules.Count ? "PASS" : (passCount > 0 || partialCount > 0 ? "PARTIAL" : "FAIL");

        var summary = $"Framework {framework} evaluation yielded an overall compliance status of {overallStatus} ({score}% compliance score) across {rules.Count} audit checkpoints.";

        return new ComplianceEvaluationResult
        {
            Framework = framework,
            OverallStatus = overallStatus,
            Score = score,
            Checks = checks,
            Summary = summary
        };
    }
}
