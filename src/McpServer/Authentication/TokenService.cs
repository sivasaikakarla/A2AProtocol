using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace McpServer.Authentication;

/// <summary>
/// Service responsible for generating signed JWTs for client authentication.
/// In production, tokens may originate from corporate CIAM/IdPs (e.g. Azure AD/Auth0).
/// This service provides standard token issuance for internal agents, clients, and tests.
/// </summary>
public sealed class TokenService
{
    private readonly JwtConfiguration _config;

    public TokenService(IOptions<JwtConfiguration> config)
    {
        _config = config.Value;
    }

    /// <summary>
    /// Generates a signed JWT token for the specified client identity and roles.
    /// </summary>
    /// <param name="clientId">Unique client identifier (e.g. "copilot-agent-1").</param>
    /// <param name="roles">Assigned authorization roles (e.g. "tool-executor").</param>
    /// <returns>A compact serialized JWT string.</returns>
    public string GenerateToken(string clientId, params string[] roles)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            throw new ArgumentException("Client ID cannot be null or empty.", nameof(clientId));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, clientId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new("client_id", clientId)
        };

        foreach (var role in roles)
        {
            if (!string.IsNullOrWhiteSpace(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        var expires = DateTime.UtcNow.AddMinutes(_config.ExpiryMinutes);

        var token = new JwtSecurityToken(
            issuer: _config.Issuer,
            audience: _config.Audience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddSeconds(-10), // Small clock skew tolerance
            expires: expires,
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
