// =============================================================================
// Cena Platform -- Admin API Host (DB-06 Phase 1 Scaffold)
// Admin-facing REST endpoints for content management, moderation, analytics.
// Phase 1: Skeleton only — endpoints migrate in DB-06b.
// =============================================================================

using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ---- Serilog ----
builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
});

// ---- Health checks ----
builder.Services.AddHealthChecks();

// ---- CORS (minimal default) ----
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

// ---- Middleware pipeline ----
app.UseCors();

// ---- Health endpoints ----
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

// ---- Root endpoint ----
app.MapGet("/", () => Results.Ok(new 
{ 
    service = "Cena Admin API Host", 
    status = "scaffold (DB-06 Phase 1)",
    timestamp = DateTimeOffset.UtcNow
}));

app.Run();
