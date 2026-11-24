using McpServer.Agents;
using McpServer.Data;
using McpServer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace McpServer.UnitTests.Services;

public class VectorSearchServiceTests
{
    private readonly Mock<ILogger<VectorSearchService>> _mockLogger = new();
    private readonly Mock<ILogger<NvidiaLlmClient>> _mockLlmLogger = new();

    [Fact]
    public async Task SearchAsync_EmptyQuery_ReturnsEmptyListWithoutCallingNvidia()
    {
        // Arrange
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=test")
            .Options;
        using var db = new AppDbContext(dbOptions);

        var config = Options.Create(new NvidiaConfig { ApiKey = "fake" });
        var httpClient = new HttpClient();
        var llmClient = new NvidiaLlmClient(httpClient, config, _mockLlmLogger.Object);

        var service = new VectorSearchService(db, llmClient, _mockLogger.Object);

        // Act
        var result = await service.SearchAsync(string.Empty);

        // Assert
        Assert.Empty(result);
    }
}
