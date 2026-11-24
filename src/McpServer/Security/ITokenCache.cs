namespace McpServer.Security;

/// <summary>
/// Abstraction interface for caching tokens and sensitive credentials securely.
/// </summary>
public interface ITokenCache
{
    /// <summary>
    /// Encrypts and stores a token in the cache with a specified expiration time-to-live.
    /// </summary>
    Task SetAsync(string key, string token, TimeSpan expiry, CancellationToken ct = default);

    /// <summary>
    /// Retrieves and decrypts a cached token. Returns null if not found or expired.
    /// </summary>
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Removes a token from the cache.
    /// </summary>
    Task RemoveAsync(string key, CancellationToken ct = default);
}
