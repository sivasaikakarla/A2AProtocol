using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using McpServer.ErrorHandling;

namespace McpServer.Agents;

public sealed class AgentRequest
{
    public required string Request { get; set; }
    public int MaxIterations { get; set; } = 10;
}

public sealed class ExecutionStep
{
    public int Iteration { get; set; }
    public required string ToolName { get; set; }
    public string? Arguments { get; set; }
    public string? Result { get; set; }
    public long DurationMs { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

public sealed class AgentResponse
{
    public required string Request { get; set; }
    public required string FinalAnswer { get; set; }
    public List<ExecutionStep> ExecutionPlan { get; set; } = new();
    public int Iterations { get; set; }
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Autonomous ReAct agent orchestrator that coordinates multi-turn reasoning and tool invocation
/// using NVIDIA NIM LLM models and the enterprise MCP tool registry.
/// </summary>
public sealed class AgentOrchestrator
{
    private readonly NvidiaLlmClient _llmClient;
    private readonly ToolRegistry _toolRegistry;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AgentOrchestrator> _logger;

    // Regex patterns to capture tool calls from LLM reasoning output
    private static readonly Regex FencedToolCallRegex = new(
        @"```(?:tool_call|json)?\s*(\{\s*""tool""\s*:\s*"".*?"".*?\})\s*```",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    private static readonly Regex RawToolCallRegex = new(
        @"(\{\s*""tool""\s*:\s*""(?<tool>[^""]+)""\s*,\s*""arguments""\s*:\s*(?<args>\{.*?\})\s*\})",
        RegexOptions.Compiled | RegexOptions.Singleline);

    public AgentOrchestrator(
        NvidiaLlmClient llmClient,
        ToolRegistry toolRegistry,
        IServiceProvider serviceProvider,
        ILogger<AgentOrchestrator> logger)
    {
        _llmClient = llmClient ?? throw new ArgumentNullException(nameof(llmClient));
        _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the autonomous ReAct reasoning and tool invocation loop.
    /// </summary>
    public async Task<AgentResponse> RunAsync(AgentRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Request))
        {
            throw new ArgumentException("Request cannot be null or empty.", nameof(req));
        }

        var maxIterations = Math.Clamp(req.MaxIterations, 1, 10);
        var executionPlan = new List<ExecutionStep>();

        var systemPrompt = "You are an autonomous Enterprise Financial and Regulatory Compliance Agent.\n" +
            "Your task is to answer user requests accurately by executing enterprise MCP tools.\n\n" +
            _toolRegistry.GetToolDescriptions() + "\n\n" +
            """
            Operating Instructions:
            1. Analyze the user request. Break complex questions (like comparisons or multi-faceted compliance audits) into step-by-step tool calls.
            2. When you need information, emit a tool call in the exact format:
            ```tool_call
            {"tool": "tool_name", "arguments": {"param1": "value1"}}
            ```
            3. After each tool execution, you will receive the observation result.
            4. When you have collected all required information, provide a comprehensive, well-structured final answer with citations and metrics.
            5. IMPORTANT: In your final answer, DO NOT include any ```tool_call block.
            """;

        var conversation = new List<ChatMessageDto>
        {
            new() { Role = "system", Content = systemPrompt },
            new() { Role = "user", Content = req.Request }
        };

        int iteration = 0;
        string finalAnswer = string.Empty;

        _logger.LogInformation("Starting agent orchestration loop for request: '{Request}' (Max iterations: {Max})",
            req.Request, maxIterations);

        while (iteration < maxIterations)
        {
            iteration++;
            _logger.LogDebug("Agent loop iteration {Iteration}/{Max}", iteration, maxIterations);

            string llmOutput;
            try
            {
                llmOutput = await _llmClient.ChatMessagesAsync(conversation, ct: ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LLM failed during agent iteration {Iteration}", iteration);
                return new AgentResponse
                {
                    Request = req.Request,
                    FinalAnswer = "An error occurred communicating with the AI reasoning model.",
                    ExecutionPlan = executionPlan,
                    Iterations = iteration,
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }

            // Append assistant response to history
            conversation.Add(new ChatMessageDto { Role = "assistant", Content = llmOutput });

            // Extract tool call if present
            var toolCall = ParseToolCall(llmOutput);

            if (toolCall == null)
            {
                // No tool call requested — LLM produced final response
                _logger.LogInformation("Agent produced final answer on iteration {Iteration}.", iteration);
                finalAnswer = CleanFinalAnswer(llmOutput);
                break;
            }

            // Execute the tool
            _logger.LogInformation("Executing tool '{ToolName}' requested by agent on iteration {Iteration}.",
                toolCall.Value.ToolName, iteration);

            var sw = Stopwatch.StartNew();
            string toolResult;
            try
            {
                toolResult = await _toolRegistry.ExecuteToolAsync(
                    toolCall.Value.ToolName,
                    toolCall.Value.Arguments,
                    _serviceProvider,
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Tool execution failed for '{ToolName}'. Returning error to LLM context.",
                    toolCall.Value.ToolName);
                toolResult = JsonSerializer.Serialize(new
                {
                    error = true,
                    tool = toolCall.Value.ToolName,
                    message = ex.Message
                });
            }
            sw.Stop();

            var step = new ExecutionStep
            {
                Iteration = iteration,
                ToolName = toolCall.Value.ToolName,
                Arguments = toolCall.Value.Arguments.GetRawText(),
                Result = toolResult.Length > 2000 ? toolResult[..2000] + "... (truncated)" : toolResult,
                DurationMs = sw.ElapsedMilliseconds,
                Timestamp = DateTime.UtcNow
            };
            executionPlan.Add(step);

            // Feed observation back to LLM for next turn
            var observationMessage = $"""
                [Tool Observation for `{toolCall.Value.ToolName}`]:
                {toolResult}

                Based on this result, either call the next required tool or provide your final comprehensive answer.
                """;

            conversation.Add(new ChatMessageDto { Role = "user", Content = observationMessage });
        }

        // If loop finished because it reached maxIterations without final answer, synthesize one
        if (string.IsNullOrWhiteSpace(finalAnswer))
        {
            _logger.LogWarning("Agent reached max iterations ({Max}). Synthesizing final answer from observations.",
                maxIterations);

            var forcedFinalPrompt = "You have reached the maximum allowed tool iterations. Please synthesize a final, comprehensive answer to the original user request based on all the observations gathered so far. Do not call any further tools.";
            conversation.Add(new ChatMessageDto { Role = "user", Content = forcedFinalPrompt });

            try
            {
                var summary = await _llmClient.ChatMessagesAsync(conversation, ct: ct);
                finalAnswer = CleanFinalAnswer(summary);
            }
            catch (Exception ex)
            {
                finalAnswer = $"Completed {executionPlan.Count} tool calls, but failed to synthesize final summary: {ex.Message}";
            }
        }

        return new AgentResponse
        {
            Request = req.Request,
            FinalAnswer = finalAnswer,
            ExecutionPlan = executionPlan,
            Iterations = iteration,
            Success = true
        };
    }

    private static (string ToolName, JsonElement Arguments)? ParseToolCall(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Try matching ```tool_call ... ``` first
        var fencedMatch = FencedToolCallRegex.Match(text);
        string jsonCandidate = string.Empty;

        if (fencedMatch.Success)
        {
            jsonCandidate = fencedMatch.Groups[1].Value.Trim();
        }
        else
        {
            // Try raw JSON object with "tool" and "arguments"
            var rawMatch = RawToolCallRegex.Match(text);
            if (rawMatch.Success)
            {
                jsonCandidate = rawMatch.Value.Trim();
            }
        }

        if (string.IsNullOrEmpty(jsonCandidate))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(jsonCandidate);
            var root = doc.RootElement;

            if (root.TryGetProperty("tool", out var toolProp) &&
                root.TryGetProperty("arguments", out var argsProp))
            {
                var toolName = toolProp.GetString();
                if (!string.IsNullOrWhiteSpace(toolName))
                {
                    return (toolName, argsProp.Clone());
                }
            }
        }
        catch (JsonException)
        {
            // Invalid JSON inside tool call block
        }

        return null;
    }

    private static string CleanFinalAnswer(string text)
    {
        // Strip any residual tool_call code blocks if model accidentally appended one
        var cleaned = FencedToolCallRegex.Replace(text, string.Empty).Trim();
        return cleaned;
    }
}
