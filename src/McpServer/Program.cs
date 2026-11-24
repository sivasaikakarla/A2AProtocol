using System.Text;
using FastEndpoints;
using McpServer.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;

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

var app = builder.Build();

app.UseSerilogRequestLogging();

app.UseAuthentication();
app.UseAuthorization();

app.UseFastEndpoints();

// Health check endpoint returning 200 OK
app.MapHealthChecks("/health");

app.Run();
