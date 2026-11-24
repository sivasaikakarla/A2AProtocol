namespace McpServer.Authentication;

/// <summary>
/// Strongly-typed configuration for JWT authentication.
/// Binds to the "Jwt" section of appsettings.json.
/// </summary>
public sealed class JwtConfiguration
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// The symmetric secret key used to sign and validate tokens (minimum 32 characters).
    /// </summary>
    public required string SecretKey { get; init; }

    /// <summary>
    /// Expected issuer claim ("iss").
    /// </summary>
    public required string Issuer { get; init; }

    /// <summary>
    /// Expected audience claim ("aud").
    /// </summary>
    public required string Audience { get; init; }

    /// <summary>
    /// Expiry duration in minutes. Defaults to 60.
    /// </summary>
    public int ExpiryMinutes { get; init; } = 60;

    /// <summary>
    /// List of URL path prefixes that bypass authentication (e.g. /health, /auth/token, /swagger).
    /// </summary>
    public string[] ExcludedPaths { get; init; } = [
        "/health",
        "/auth/token",
        "/"
    ];
}
