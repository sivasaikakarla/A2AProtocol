using FastEndpoints;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Add FastEndpoints
builder.Services.AddFastEndpoints();

// Configure Health Checks
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseFastEndpoints();

// Health check endpoint returning 200 OK
app.MapHealthChecks("/health");

app.Run();
