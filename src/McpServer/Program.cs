using System.Text;
using FastEndpoints;
using FastEndpoints.Mcp;
using McpServer.Agents;
using McpServer.Authentication;
using McpServer.Data;
using McpServer.ErrorHandling;
using McpServer.Security;
using McpServer.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using StackExchange.Redis;

// Load local .env variables into environment if present in current or parent directories
var currentDir = new DirectoryInfo(Directory.GetCurrentDirectory());
string? envFilePath = null;
while (currentDir != null)
{
    var candidate = Path.Combine(currentDir.FullName, ".env");
    if (File.Exists(candidate))
    {
        envFilePath = candidate;
        break;
    }
    currentDir = currentDir.Parent;
}

if (envFilePath != null)
{
    foreach (var line in File.ReadAllLines(envFilePath))
    {
        var trimmed = line.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;
        var parts = trimmed.Split('=', 2);
        if (parts.Length == 2)
        {
            var key = parts[0].Trim();
            var val = parts[1].Trim().Trim('"').Trim('\'');
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                Environment.SetEnvironmentVariable(key, val);
            }
        }
    }
}

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

// Configure PostgreSQL DbContext with pgvector
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("DefaultConnection connection string is required.");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(connectionString, o => o.UseVector());
});

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

// Configure NVIDIA NIM AI Client & Vector Search
var nvidiaSection = builder.Configuration.GetSection(NvidiaConfig.SectionName);
builder.Services.Configure<NvidiaConfig>(options =>
{
    nvidiaSection.Bind(options);
    var envKey = Environment.GetEnvironmentVariable("NVIDIA_API_KEY");
    if (!string.IsNullOrWhiteSpace(envKey))
    {
        options.ApiKey = envKey;
    }
    var envChat = Environment.GetEnvironmentVariable("NVIDIA_CHAT_MODEL");
    if (!string.IsNullOrWhiteSpace(envChat))
    {
        options.ChatModel = envChat;
    }
    var envEmbed = Environment.GetEnvironmentVariable("NVIDIA_EMBEDDING_MODEL");
    if (!string.IsNullOrWhiteSpace(envEmbed))
    {
        options.EmbeddingModel = envEmbed;
    }
});

builder.Services.AddHttpClient<NvidiaLlmClient>();
builder.Services.AddScoped<VectorSearchService>();

// Register MCP Enterprise Services
builder.Services.AddSingleton<ComplianceRulesEngine>();
builder.Services.AddSingleton<ToolRegistry>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<AgentOrchestrator>();

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

// Add FastEndpoints and Model Context Protocol (MCP) bridge
builder.Services.AddFastEndpoints();
builder.Services.AddMcp(options =>
{
    // Allow external MCP clients to see all registered enterprise tools
    options.ToolVisibilityFilter = (_, _, _) => true;
});

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

app.UseFastEndpoints(c =>
{
    c.Errors.StatusCode = StatusCodes.Status422UnprocessableEntity;
    c.Errors.UseProblemDetails();
});

// Expose standard Model Context Protocol (MCP) Streamable HTTP endpoint at /mcp
app.UseMcp("/mcp");

// Health check endpoint returning 200 OK
app.MapHealthChecks("/health");

// Apply pending EF Core database migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        logger.LogInformation("Applying pending PostgreSQL database migrations...");
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to apply database migrations on startup.");
        throw;
    }
}

app.Run();

public partial class Program { }
