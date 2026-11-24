using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using StackExchange.Redis;
using Xunit;

namespace McpServer.Security.Tests;

public sealed class EncryptedTokenCacheTests
{
    private const string Valid32ByteHexKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Cache_SetAndGet_ReturnsDecryptedToken()
    {
        // Arrange
        var encryptor = new AesGcmEncryptor(Valid32ByteHexKey);
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDb = new Mock<IDatabase>(MockBehavior.Loose);

        const string tokenKey = "agent-token-key";
        const string rawToken = "raw-jwt-or-bearer-token-string";
        var encryptedToken = encryptor.Encrypt(rawToken);

        mockDb.SetReturnsDefault<Task<bool>>(Task.FromResult(true));
        mockDb.SetReturnsDefault<Task<RedisValue>>(Task.FromResult(new RedisValue(encryptedToken)));
        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);

        var cache = new EncryptedTokenCache(mockRedis.Object, encryptor, NullLogger<EncryptedTokenCache>.Instance);

        // Act
        await cache.SetAsync(tokenKey, rawToken, TimeSpan.FromMinutes(10));
        var retrieved = await cache.GetAsync(tokenKey);

        // Assert
        Assert.Equal(rawToken, retrieved);

        // Verify StringSetAsync was called with the key containing KeyPrefix and an encrypted value
        mockDb.Verify(db => db.StringSetAsync(
            It.Is<RedisKey>(k => k.ToString().Contains(tokenKey)),
            It.Is<RedisValue>(v => v.ToString() != rawToken),
            It.IsAny<TimeSpan?>(),
            It.IsAny<bool>(),
            It.IsAny<When>(),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task Cache_WhenKeyNotFound_ReturnsNull()
    {
        // Arrange
        var encryptor = new AesGcmEncryptor(Valid32ByteHexKey);
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDb = new Mock<IDatabase>(MockBehavior.Loose);

        mockDb.SetReturnsDefault<Task<RedisValue>>(Task.FromResult(RedisValue.Null));
        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);

        var cache = new EncryptedTokenCache(mockRedis.Object, encryptor, NullLogger<EncryptedTokenCache>.Instance);

        // Act
        var result = await cache.GetAsync("non-existent-key");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task Cache_WhenDataIsCorrupt_LogsWarningRemovesEntryAndReturnsNull()
    {
        // Arrange
        var encryptor = new AesGcmEncryptor(Valid32ByteHexKey);
        var mockRedis = new Mock<IConnectionMultiplexer>();
        var mockDb = new Mock<IDatabase>(MockBehavior.Loose);

        // Corrupt data (not valid encrypted Base64)
        mockDb.SetReturnsDefault<Task<RedisValue>>(Task.FromResult(new RedisValue("this-is-not-valid-aes-gcm-data")));
        mockRedis.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(mockDb.Object);

        var cache = new EncryptedTokenCache(mockRedis.Object, encryptor, NullLogger<EncryptedTokenCache>.Instance);

        // Act
        var result = await cache.GetAsync("corrupted-token-key");

        // Assert
        Assert.Null(result);
        // Verify KeyDeleteAsync was triggered to evict corrupted entry
        mockDb.Verify(db => db.KeyDeleteAsync(
            It.Is<RedisKey>(k => k.ToString().Contains("corrupted-token-key")),
            It.IsAny<CommandFlags>()), Times.Once);
    }
}
