using System.Security.Cryptography;
using StackExchange.Redis;

namespace McpServer.Security;

/// <summary>
/// Redis-backed implementation of ITokenCache.
/// Encrypts all tokens at rest using AES-256-GCM before writing to Redis.
/// Even if Redis memory or snapshots are compromised, tokens cannot be decrypted without the private key.
/// </summary>
public sealed class EncryptedTokenCache : ITokenCache
{
    public const string KeyPrefix = "mcp:token:";

    private readonly IConnectionMultiplexer _redis;
    private readonly AesGcmEncryptor _encryptor;
    private readonly ILogger<EncryptedTokenCache> _logger;

    public EncryptedTokenCache(
        IConnectionMultiplexer redis,
        AesGcmEncryptor encryptor,
        ILogger<EncryptedTokenCache> logger)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _encryptor = encryptor ?? throw new ArgumentNullException(nameof(encryptor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SetAsync(string key, string token, TimeSpan expiry, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Token cannot be null or empty.", nameof(token));

        var db = _redis.GetDatabase();
        var fullKey = $"{KeyPrefix}{key.Trim()}";

        // Encrypt the token using AES-256-GCM
        var encryptedBlob = _encryptor.Encrypt(token);

        await db.StringSetAsync(fullKey, encryptedBlob, expiry, false, When.Always, CommandFlags.None);
        _logger.LogDebug("Securely stored encrypted token for key '{Key}' with expiry {Expiry}.", key, expiry);
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Cache key cannot be null or empty.", nameof(key));

        var db = _redis.GetDatabase();
        var fullKey = $"{KeyPrefix}{key.Trim()}";

        var encryptedValue = await db.StringGetAsync(fullKey);
        if (encryptedValue.IsNullOrEmpty)
        {
            _logger.LogDebug("Cache miss for key '{Key}'.", key);
            return null;
        }

        try
        {
            var decryptedToken = _encryptor.Decrypt(encryptedValue.ToString());
            return decryptedToken;
        }
        catch (CryptographicException ex)
        {
            _logger.LogWarning(ex, "Failed to decrypt token for key '{Key}'. Data may be tampered or key rotated. Removing corrupt entry.", key);
            await RemoveAsync(key, ct);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error decrypting cached token for key '{Key}'.", key);
            return null;
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        var db = _redis.GetDatabase();
        var fullKey = $"{KeyPrefix}{key.Trim()}";
        await db.KeyDeleteAsync(fullKey);
        _logger.LogDebug("Evicted token for key '{Key}'.", key);
    }
}
