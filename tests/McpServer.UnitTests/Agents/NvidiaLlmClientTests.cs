using System.Net;
using System.Text.Json;
using McpServer.Agents;
using McpServer.ErrorHandling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace McpServer.UnitTests.Agents;

public class NvidiaLlmClientTests
{
    private readonly Mock<ILogger<NvidiaLlmClient>> _mockLogger = new();

    private (NvidiaLlmClient client, Mock<HttpMessageHandler> handlerMock) CreateClient(
        HttpResponseMessage[] responses,
        NvidiaConfig? customConfig = null)
    {
        var config = customConfig ?? new NvidiaConfig
        {
            ApiKey = "test-nvapi-key-123",
            BaseUrl = "https://integrate.api.nvidia.com/v1",
            ChatModel = "nvidia/nemotron-3.5-super-vl-120b-a12b",
            EmbeddingModel = "nvidia/nemotron-3.5-embed-1b",
            FallbackModel = "nvidia/nemotron-3-ultra-550b-a55b",
            MaxTokens = 100,
            Temperature = 0.2,
            EmbeddingDimensions = 1024
        };

        var handlerMock = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var sequence = handlerMock.Protected()
            .SetupSequence<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>());

        foreach (var response in responses)
        {
            sequence = sequence.ReturnsAsync(response);
        }

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri(config.BaseUrl.TrimEnd('/') + "/")
        };

        var options = Options.Create(config);
        var client = new NvidiaLlmClient(httpClient, options, _mockLogger.Object);

        return (client, handlerMock);
    }

    [Fact]
    public async Task ChatAsync_SuccessfulResponse_ReturnsAssistantContent()
    {
        // Arrange
        var jsonResponse = JsonSerializer.Serialize(new ChatCompletionResponse
        {
            Id = "chat-1",
            Choices = new List<ChatChoiceDto>
            {
                new()
                {
                    Index = 0,
                    Message = new ChatResponseMessageDto
                    {
                        Role = "assistant",
                        Content = "NVIDIA Corporation (NVDA) reported fiscal 2024 revenue of $60.9B."
                    }
                }
            }
        });

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
        };

        var (client, _) = CreateClient(new[] { httpResponse });

        // Act
        var result = await client.ChatAsync("You are a financial analyst.", "Analyze NVDA revenue.");

        // Assert
        Assert.Equal("NVIDIA Corporation (NVDA) reported fiscal 2024 revenue of $60.9B.", result);
    }

    [Fact]
    public async Task ChatAsync_EmptyContentWithReasoning_ReturnsReasoningContent()
    {
        // Arrange
        var jsonResponse = JsonSerializer.Serialize(new ChatCompletionResponse
        {
            Id = "chat-2",
            Choices = new List<ChatChoiceDto>
            {
                new()
                {
                    Index = 0,
                    Message = new ChatResponseMessageDto
                    {
                        Role = "assistant",
                        Content = null,
                        ReasoningContent = "Calculated GAAP operating margin of 54.1%."
                    }
                }
            }
        });

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
        };

        var (client, _) = CreateClient(new[] { httpResponse });

        // Act
        var result = await client.ChatAsync("System", "Calculate margin.");

        // Assert
        Assert.Equal("Calculated GAAP operating margin of 54.1%.", result);
    }

    [Fact]
    public async Task ChatAsync_429RateLimit_FallsBackToSecondaryModel()
    {
        // Arrange: First call returns 429 Too Many Requests, second call succeeds with fallback model
        var rateLimitResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("Rate limit exceeded for primary model", System.Text.Encoding.UTF8, "application/json")
        };

        var fallbackJsonResponse = JsonSerializer.Serialize(new ChatCompletionResponse
        {
            Id = "chat-fallback-1",
            Choices = new List<ChatChoiceDto>
            {
                new()
                {
                    Index = 0,
                    Message = new ChatResponseMessageDto
                    {
                        Role = "assistant",
                        Content = "Fallback response from secondary model."
                    }
                }
            }
        });

        var successResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(fallbackJsonResponse, System.Text.Encoding.UTF8, "application/json")
        };

        var (client, handlerMock) = CreateClient(new[] { rateLimitResponse, successResponse });

        // Act
        var result = await client.ChatAsync("System", "Perform query.");

        // Assert
        Assert.Equal("Fallback response from secondary model.", result);
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Exactly(2),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task ChatAsync_NonRecoverableError_ThrowsLlmProviderException()
    {
        // Arrange: 500 Internal Server Error
        var errorResponse = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Internal server fault at NIM gateway", System.Text.Encoding.UTF8, "application/json")
        };

        var (client, _) = CreateClient(new[] { errorResponse });

        // Act & Assert
        var ex = await Assert.ThrowsAsync<LlmProviderException>(() =>
            client.ChatAsync("System", "Hello"));

        Assert.Contains("500", ex.Message);
    }

    [Fact]
    public async Task EmbedAsync_Returns1024DimFloatArrays()
    {
        // Arrange
        var mockEmbedding1 = Enumerable.Range(0, 1024).Select(i => (float)i / 1024f).ToArray();
        var mockEmbedding2 = Enumerable.Range(0, 1024).Select(i => 1.0f - ((float)i / 1024f)).ToArray();

        var jsonResponse = JsonSerializer.Serialize(new EmbeddingResponse
        {
            Data = new List<EmbeddingItemDto>
            {
                new() { Index = 0, Embedding = mockEmbedding1 },
                new() { Index = 1, Embedding = mockEmbedding2 }
            }
        });

        var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, System.Text.Encoding.UTF8, "application/json")
        };

        var (client, _) = CreateClient(new[] { httpResponse });

        // Act
        var result = await client.EmbedAsync(new[] { "First text passage", "Second text passage" });

        // Assert
        Assert.Equal(2, result.Length);
        Assert.Equal(1024, result[0].Length);
        Assert.Equal(1024, result[1].Length);
        Assert.Equal(mockEmbedding1[0], result[0][0]);
    }

    [Fact]
    public async Task EmbedAsync_EmptyInputList_ReturnsEmptyArrayWithoutCallingHttp()
    {
        // Arrange
        var (client, handlerMock) = CreateClient(Array.Empty<HttpResponseMessage>());

        // Act
        var result = await client.EmbedAsync(Array.Empty<string>());

        // Assert
        Assert.Empty(result);
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }
}
