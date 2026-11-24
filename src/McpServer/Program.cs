using System.Text;
using FastEndpoints;
using McpServer.Authentication;
using McpServer.ErrorHandling;
using McpServer.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Configure JWT Options
var jwtSection = builder.Configuration.GetSection(JwtConfiguration.SectionName);
builder.Services.Configure<JwtConfiguration>(jwtSection);
var jwtConfig = jwtSection.Get<JwtConfiguration>()
    ?? throw new InvalidOperationException("Jwt configuration section is missing or invalid.");

// Register Token Service
builder.Services.AddSingleton<TokenService>();

// Configure AES-256-GCM Encryption & Redis Token Cache
var encryptionKey = builder.Configuration["Encryption:Key"]
    ?? throw new InvalidOperationException("Encryption:Key configuration is required.");
builder.Services.AddSingleton(new AesGcmEncryptor(encryptionKey));

var redisConnectionString = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<Program>>();
    try
    {
        return ConnectionMultiplexer.Connect(redisConnectionString);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not establish initial connection to Redis at {ConnectionString}. Lazy retry will apply.", redisConnectionString);
        var config = ConfigurationOptions.Parse(redisConnectionString);
        config.AbortOnConnectFail = false;
        return ConnectionMultiplexer.Connect(config);
    }
});
builder.Services.AddSingleton<ITokenCache, EncryptedTokenCache>();

// Configure Authentication & JWT Bearer
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false; // Dev environment friendly
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtConfig.Issuer,
        ValidAudience = jwtConfig.Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtConfig.SecretKey)),
        ClockSkew = TimeSpan.FromSeconds(30)
    };
});

builder.Services.AddAuthorization();

// Add FastEndpoints
builder.Services.AddFastEndpoints();

// Configure Health Checks
builder.Services.AddHealthChecks();

// Configure RFC 7807 Problem Details and Global Exception Handling
builder.Services.AddProblemDetails(options =>
{
    options.CustomizeProblemDetails = ctx =>
    {
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
        ctx.ProblemDetails.Extensions["timestamp"] = DateTime.UtcNow.ToString("O");
    };
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

// Intercept all unhandled exceptions and format as RFC 7807 Problem Details
app.UseExceptionHandler();

app.UseSerilogRequestLogging();

// Custom JWT Middleware that honors ExcludedPaths (/health, /auth/token, /, etc.)
app.UseMiddleware<JwtMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.UseFastEndpoints();

// Health check endpoint returning 200 OK
app.MapHealthChecks("/health");

app.Run();

public partial class Program { }
