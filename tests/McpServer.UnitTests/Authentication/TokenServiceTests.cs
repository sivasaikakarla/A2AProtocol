using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using McpServer.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace McpServer.UnitTests.Authentication;

public sealed class TokenServiceTests
{
    private readonly JwtConfiguration _config;
    private readonly TokenService _tokenService;

    public TokenServiceTests()
    {
        _config = new JwtConfiguration
        {
            SecretKey = "unit-test-secret-key-that-is-at-least-32-chars-long!",
            Issuer = "test-issuer",
            Audience = "test-audience",
            ExpiryMinutes = 30
        };

        var options = Options.Create(_config);
        _tokenService = new TokenService(options);
    }

    [Fact]
    public void GenerateToken_ReturnsValidJwt_WithExpectedClaims()
    {
        // Arrange
        const string clientId = "test-agent-1";
        var roles = new[] { "tool-executor", "admin" };

        // Act
        var tokenString = _tokenService.GenerateToken(clientId, roles);

        // Assert
        Assert.NotNull(tokenString);
        Assert.NotEmpty(tokenString);

        var handler = new JwtSecurityTokenHandler();
        Assert.True(handler.CanReadToken(tokenString));

        var jwt = handler.ReadJwtToken(tokenString);
        Assert.Equal(_config.Issuer, jwt.Issuer);
        Assert.Contains(_config.Audience, jwt.Audiences);
        Assert.Equal(clientId, jwt.Subject);

        var roleClaims = jwt.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();
        Assert.Contains("tool-executor", roleClaims);
        Assert.Contains("admin", roleClaims);

        var clientIdClaim = jwt.Claims.FirstOrDefault(c => c.Type == "client_id")?.Value;
        Assert.Equal(clientId, clientIdClaim);
    }

    [Fact]
    public void GenerateToken_TokenCanBeValidatedByTokenValidationParameters()
    {
        // Arrange
        const string clientId = "validated-client";
        var tokenString = _tokenService.GenerateToken(clientId, "tool-executor");

        var tokenValidationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _config.Issuer,
            ValidAudience = _config.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config.SecretKey)),
            ClockSkew = TimeSpan.Zero
        };

        var handler = new JwtSecurityTokenHandler();

        // Act
        var principal = handler.ValidateToken(tokenString, tokenValidationParams, out var validatedToken);

        // Assert
        Assert.NotNull(principal);
        Assert.NotNull(validatedToken);
        Assert.Equal(clientId, principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.True(principal.IsInRole("tool-executor"));
    }

    [Fact]
    public void GenerateToken_ThrowsException_WhenClientIdIsEmpty()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _tokenService.GenerateToken(""));
        Assert.Throws<ArgumentException>(() => _tokenService.GenerateToken("   "));
    }
}
