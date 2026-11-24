using System.Net;
using System.Text.Json;
using McpServer.Agents;
using McpServer.Data;
using McpServer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Pgvector.EntityFrameworkCore;
using Xunit;

namespace McpServer.UnitTests.Agents;

public class AgentOrchestratorTests
{
    private readonly Mock<ILogger<AgentOrchestrator>> _mockLogger = new();
    private readonly Mock<ILogger<NvidiaLlmClient>> _mockLlmLogger = new();
    private const string ConnectionString = "Host=localhost;Port=5432;Database=mcptools;Username=mcp;Password=McpS3cur3Pass!";

    private AgentOrchestrator CreateOrchestrator(List<string> llmResponses)
    {
        var services = new ServiceCollection();

        services.AddDbContext<AppDbContext>(opts =>
            opts.UseNpgsql(ConnectionString, o => o.UseVector()));

        var config = new NvidiaConfig
        {
            ApiKey = "test-key",
            BaseUrl = "https://integrate.api.nvidia.com/v1",
            ChatModel = "nvidia/nemotron-3.5-super-vl-120b-a12b"
        };

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var sequence = handlerMock.Protected()
            .SetupSequence<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());

        foreach (var resp in llmResponses)
        {
            var chatResp = new ChatCompletionResponse
            {
                Id = "chatcmpl-" + Guid.NewGuid().ToString("N"),
                Choices = new List<ChatChoiceDto>
                {
                    new()
                    {
                        Index = 0,
                        Message = new ChatResponseMessageDto
                        {
                            Role = "assistant",
                            Content = resp
                        },
                        FinishReason = "stop"
                    }
                }
            };

            var httpMsg = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(chatResp))
            };
            sequence = sequence.ReturnsAsync(httpMsg);
        }

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri(config.BaseUrl.TrimEnd('/') + "/")
        };

        services.AddSingleton(httpClient);
        services.AddSingleton(Options.Create(config));
        services.AddSingleton(_mockLlmLogger.Object);
        services.AddSingleton<NvidiaLlmClient>();
        services.AddSingleton<ToolRegistry>();
        services.AddSingleton<ComplianceRulesEngine>();
        services.AddScoped<VectorSearchService>();
        services.AddScoped<AuditLogService>();
        services.AddScoped<AgentOrchestrator>();
        services.AddLogging();

        var sp = services.BuildServiceProvider();

        return new AgentOrchestrator(
            sp.GetRequiredService<NvidiaLlmClient>(),
            sp.GetRequiredService<ToolRegistry>(),
            sp,
            _mockLogger.Object);
    }

    [Fact]
    public async Task RunAsync_DirectAnswerWithoutTools_ReturnsAnswerInSingleIteration()
    {
        // Arrange
        var expectedAnswer = "Apple is an enterprise technology company recognized globally for iPhone hardware.";
        var orchestrator = CreateOrchestrator(new List<string> { expectedAnswer });

        var req = new AgentRequest { Request = "What is Apple?", MaxIterations = 5 };

        // Act
        var res = await orchestrator.RunAsync(req);

        // Assert
        Assert.True(res.Success);
        Assert.Equal(1, res.Iterations);
        Assert.Empty(res.ExecutionPlan);
        Assert.Equal(expectedAnswer, res.FinalAnswer);
    }

    [Fact]
    public async Task RunAsync_SingleToolCall_ExecutesToolAndReturnsSynthesizedAnswer()
    {
        // Arrange
        var toolCallResponse = """
            I will look up corporate metadata for Apple to answer your question.
            ```tool_call
            {"tool": "lookup_company", "arguments": {"ticker": "AAPL"}}
            ```
            """;
        var finalSynthesis = "Apple Inc. (AAPL) operates in Consumer Electronics under CIK 0000320193.";

        var orchestrator = CreateOrchestrator(new List<string>
        {
            toolCallResponse,
            finalSynthesis
        });

        var req = new AgentRequest { Request = "Tell me Apple's CIK and sector.", MaxIterations = 5 };

        // Act
        var res = await orchestrator.RunAsync(req);

        // Assert
        Assert.True(res.Success);
        Assert.Equal(2, res.Iterations);
        Assert.Single(res.ExecutionPlan);

        var step = res.ExecutionPlan[0];
        Assert.Equal("lookup_company", step.ToolName);
        Assert.Contains("AAPL", step.Arguments);
        Assert.Contains("0000320193", step.Result);
        Assert.True(step.DurationMs >= 0);

        Assert.Equal(finalSynthesis, res.FinalAnswer);
    }

    [Fact]
    public async Task RunAsync_ToolFailure_FeedsErrorBackAndRecoversGracefully()
    {
        // Arrange
        var invalidToolCall = """
            ```tool_call
            {"tool": "unknown_tool_xyz", "arguments": {}}
            ```
            """;
        var recoveryAnswer = "I apologize, that tool does not exist. Here is what I know directly.";

        var orchestrator = CreateOrchestrator(new List<string>
        {
            invalidToolCall,
            recoveryAnswer
        });

        var req = new AgentRequest { Request = "Run diagnostic on XYZ", MaxIterations = 5 };

        // Act
        var res = await orchestrator.RunAsync(req);

        // Assert
        Assert.True(res.Success);
        Assert.Equal(2, res.Iterations);
        Assert.Single(res.ExecutionPlan);
        Assert.Contains("error", res.ExecutionPlan[0].Result);
        Assert.Equal(recoveryAnswer, res.FinalAnswer);
    }

    [Fact]
    public async Task RunAsync_ReachesMaxIterations_SynthesizesForcedFinalAnswer()
    {
        // Arrange
        var toolCall = """
            ```tool_call
            {"tool": "lookup_company", "arguments": {"ticker": "AAPL"}}
            ```
            """;
        var forcedFinal = "Based on repeated observations, Apple Inc. is indexed.";

        // MaxIterations = 2: iteration 1 = tool, iteration 2 = tool, then forced synthesis
        var orchestrator = CreateOrchestrator(new List<string>
        {
            toolCall,
            toolCall,
            forcedFinal
        });

        var req = new AgentRequest { Request = "Continuous loop test", MaxIterations = 2 };

        // Act
        var res = await orchestrator.RunAsync(req);

        // Assert
        Assert.True(res.Success);
        Assert.Equal(2, res.Iterations);
        Assert.Equal(2, res.ExecutionPlan.Count);
        Assert.Equal(forcedFinal, res.FinalAnswer);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RunAsync_EmptyRequest_ThrowsArgumentException(string emptyReq)
    {
        var orchestrator = CreateOrchestrator(new List<string>());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            orchestrator.RunAsync(new AgentRequest { Request = emptyReq }));
    }
}
