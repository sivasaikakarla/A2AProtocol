using McpServer.Agents;
using McpServer.ErrorHandling;
using McpServer.Services;
using Xunit;

namespace McpServer.UnitTests.Tools;

public class ToolRegistryAndRulesEngineTests
{
    [Fact]
    public void ToolRegistry_ContainsAllFourEnterpriseTools()
    {
        var registry = new ToolRegistry();
        var tools = registry.GetAllTools();

        Assert.Equal(4, tools.Count);
        Assert.NotNull(registry.GetTool("search_financial_filings"));
        Assert.NotNull(registry.GetTool("check_compliance"));
        Assert.NotNull(registry.GetTool("generate_audit_report"));
        Assert.NotNull(registry.GetTool("lookup_company"));
    }

    [Fact]
    public void ToolRegistry_GetRequiredTool_UnknownTool_ThrowsToolNotFoundException()
    {
        var registry = new ToolRegistry();

        var ex = Assert.Throws<ToolNotFoundException>(() =>
            registry.GetRequiredTool("non_existent_tool"));

        Assert.Equal("non_existent_tool", ex.ToolName);
        Assert.Equal(404, ex.StatusCode);
    }

    [Fact]
    public void ComplianceRulesEngine_EvaluatesSox404_ReturnsPassWhenKeywordsPresent()
    {
        var engine = new ComplianceRulesEngine();
        var sampleText = """
            Item 9A. Controls and Procedures.
            Management assessed the effectiveness of our internal control over financial reporting as of fiscal year end,
            using the criteria set forth in Internal Control - Integrated Framework issued by COSO.
            Our independent registered public accounting firm has issued an attestation audit opinion on ICFR.
            No material weakness or significant deficiency was identified, and no remediation is required.
            """;

        var result = engine.Evaluate("SOX-404", sampleText);

        Assert.Equal("SOX-404", result.Framework);
        Assert.Equal("PASS", result.OverallStatus);
        Assert.Equal(100.0, result.Score);
        Assert.All(result.Checks, c => Assert.Equal("PASS", c.Status));
    }

    [Fact]
    public void ComplianceRulesEngine_EvaluatesUnsupportedFramework_ReturnsFail()
    {
        var engine = new ComplianceRulesEngine();

        var result = engine.Evaluate("UNKNOWN-FRAMEWORK", "Some text");

        Assert.Equal("FAIL", result.OverallStatus);
        Assert.Equal(0.0, result.Score);
        Assert.Empty(result.Checks);
    }
}
