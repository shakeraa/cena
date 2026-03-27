// =============================================================================
// Cena Platform -- Admin API Host
// Separate process for admin REST endpoints (BKD-001/002/003/004).
// Connects to same PostgreSQL/Redis/Firebase as Actor Host but runs
// independently — no Proto.Actor cluster membership in this process.
// =============================================================================

using Cena.Actors.Configuration;
using Cena.Admin.Api;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Firebase;
using Cena.Infrastructure.Seed;
using Marten;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ---- Serilog ----
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration.ReadFrom.Configuration(context.Configuration);
});

// ---- Configuration ----
var pgConnectionString = builder.Configuration.GetConnectionString("PostgreSQL")
    ?? "Host=localhost;Port=5432;Database=cena;Username=cena;Password=cena_dev_password;Include Error Detail=true";
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? "localhost:6379";

// ---- Marten (PostgreSQL) — same DB as actor host ----
builder.Services.AddMarten(opts =>
{
    opts.ConfigureCenaEventStore(pgConnectionString);
});

// ---- Redis ----
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var options = ConfigurationOptions.Parse(redisConnectionString);
    options.AbortOnConnectFail = false;
    options.ConnectRetry = 3;
    options.ConnectTimeout = 5000;
    return ConnectionMultiplexer.Connect(options);
});

// ---- Firebase Auth + Authorization (BKD-001) ----
builder.Services.AddHttpContextAccessor();
builder.Services.AddFirebaseAuth(builder.Configuration);
builder.Services.AddCenaAuthorization();

// ---- CORS (BKD-001.4) ----
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5174"];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// ---- Admin Services (ADM-004 through ADM-016) ----
builder.Services.AddSingleton<IFirebaseAdminService, FirebaseAdminService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
builder.Services.AddScoped<IAdminRoleService, AdminRoleService>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
builder.Services.AddScoped<IContentModerationService, ContentModerationService>();
builder.Services.AddScoped<IFocusAnalyticsService, FocusAnalyticsService>();
builder.Services.AddScoped<IMasteryTrackingService, MasteryTrackingService>();
builder.Services.AddScoped<ISystemMonitoringService, SystemMonitoringService>();
builder.Services.AddScoped<IIngestionPipelineService, IngestionPipelineService>();
builder.Services.AddScoped<IQuestionBankService, QuestionBankService>();
builder.Services.AddScoped<IMethodologyAnalyticsService, MethodologyAnalyticsService>();
builder.Services.AddScoped<ICulturalContextService, CulturalContextService>();
builder.Services.AddScoped<IEventStreamService, EventStreamService>();
builder.Services.AddScoped<IOutreachEngagementService, OutreachEngagementService>();

// =============================================================================
// BUILD & PIPELINE
// =============================================================================

var app = builder.Build();

// Middleware order: CORS → Auth → Revocation → Endpoints
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TokenRevocationMiddleware>();

// ---- Health check ----
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "cena-admin-api" }));

// ---- Admin REST API endpoints (ADM-004 through ADM-016) ----
app.MapAdminUserEndpoints();
app.MapAdminRoleEndpoints();
app.MapAdminDashboardEndpoints();
app.MapContentModerationEndpoints();
app.MapFocusAnalyticsEndpoints();
app.MapMasteryTrackingEndpoints();
app.MapSystemMonitoringEndpoints();
app.MapIngestionPipelineEndpoints();
app.MapQuestionBankEndpoints();
app.MapMethodologyAnalyticsEndpoints();
app.MapCulturalContextEndpoints();
app.MapEventStreamEndpoints();
app.MapOutreachEngagementEndpoints();

// ---- Seed predefined roles on startup ----
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var appLogger = app.Services.GetRequiredService<ILogger<Program>>();

lifetime.ApplicationStarted.Register(async () =>
{
    var store = app.Services.GetRequiredService<IDocumentStore>();
    await RoleSeedData.SeedRolesAsync(store, appLogger);
    await UserSeedData.SeedUsersAsync(store, appLogger);
    await UserSeedData.SeedSimulatedStudentsAsync(store, appLogger, totalStudents: 100);
});

app.Run();
