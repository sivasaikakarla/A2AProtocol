using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;
using Xunit;

namespace McpServer.Security.IntegrationTests;

public sealed class LiveRedisEncryptedCacheTests
{
    private const string Valid32ByteHexKey = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task LiveRedis_EncryptsAtRest_AndDecryptsAccurately()
    {
        using var encryptor = new AesGcmEncryptor(Valid32ByteHexKey);
        using var redis = ConnectionMultiplexer.Connect("localhost:6380");
        var cache = new EncryptedTokenCache(redis, encryptor, NullLogger<EncryptedTokenCache>.Instance);

        const string tokenKey = "agent-session-live";
        const string plainToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.sensitive-live-payload";

        // 1. Set token in cache
        await cache.SetAsync(tokenKey, plainToken, TimeSpan.FromMinutes(5));

        // 2. Retrieve through cache interface (decrypts)
        var decrypted = await cache.GetAsync(tokenKey);
        Assert.Equal(plainToken, decrypted);

        // 3. Inspect raw value in Redis directly
        var db = redis.GetDatabase();
        var rawRedisValue = await db.StringGetAsync(EncryptedTokenCache.KeyPrefix + tokenKey);
        Assert.True(rawRedisValue.HasValue);
        Assert.DoesNotContain("sensitive-live-payload", rawRedisValue.ToString());

        // 4. Verify raw value is valid Base64
        var rawBytes = Convert.FromBase64String(rawRedisValue.ToString());
        Assert.True(rawBytes.Length > AesGcmEncryptor.NonceSizeBytes + AesGcmEncryptor.TagSizeBytes);

        // Clean up
        await cache.RemoveAsync(tokenKey);
        var afterRemoval = await cache.GetAsync(tokenKey);
        Assert.Null(afterRemoval);
    }
}
