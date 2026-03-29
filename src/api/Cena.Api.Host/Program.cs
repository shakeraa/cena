// =============================================================================
// Cena Platform -- Admin API Host
// Separate process for admin REST endpoints (BKD-001/002/003/004).
// Connects to same PostgreSQL/Redis/Firebase as Actor Host but runs
// independently — no Proto.Actor cluster membership in this process.
// =============================================================================

using Cena.Actors.Configuration;
using Cena.Admin.Api;
using Cena.Infrastructure.Auth;
using Cena.Infrastructure.Configuration;
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
var pgConnectionString = CenaConnectionStrings.GetPostgres(builder.Configuration, builder.Environment);
var redisConnectionString = CenaConnectionStrings.GetRedis(builder.Configuration, builder.Environment);

// ---- Marten (PostgreSQL) — same DB as actor host ----
builder.Services.AddMarten(opts =>
{
    opts.ConfigureCenaEventStore(pgConnectionString);
});

// ---- Redis ----
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var options = ConfigurationOptions.Parse(redisConnectionString);
    options.Password = builder.Configuration["Redis:Password"]
        ?? Environment.GetEnvironmentVariable("REDIS_PASSWORD")
        ?? (builder.Environment.IsDevelopment() ? "cena_dev_redis" : null);
    options.AbortOnConnectFail = false;
    options.ConnectRetry = 3;
    options.ConnectTimeout = 5000;
    return ConnectionMultiplexer.Connect(options);
});

// ---- NATS (event bus for real-time dashboard) ----
var natsUrl = builder.Configuration.GetConnectionString("NATS") ?? "nats://localhost:4222";
builder.Services.AddSingleton<NATS.Client.Core.INatsConnection>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<NATS.Client.Core.INatsConnection>>();
    var opts = new NATS.Client.Core.NatsOpts { Url = natsUrl, Name = "cena-admin-api" };
    logger.LogInformation("Configuring NATS connection to {NatsUrl}", natsUrl);
    return new NATS.Client.Core.NatsConnection(opts);
});
builder.Services.AddSingleton<NatsEventSubscriber>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<NatsEventSubscriber>());

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
            .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH")
            .WithHeaders("Authorization", "Content-Type", "Accept", "X-Requested-With")
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
builder.Services.AddSingleton<Cena.Admin.Api.QualityGate.IQualityGateService>(sp =>
    new Cena.Admin.Api.QualityGate.QualityGateService(
        configuration: sp.GetRequiredService<IConfiguration>(),
        logger: sp.GetRequiredService<ILogger<Cena.Admin.Api.QualityGate.QualityGateService>>()));
builder.Services.AddSingleton<IAiGenerationService, AiGenerationService>();
builder.Services.AddScoped<IQuestionBankService, QuestionBankService>();
builder.Services.AddScoped<IMethodologyAnalyticsService, MethodologyAnalyticsService>();
builder.Services.AddScoped<ICulturalContextService, CulturalContextService>();
builder.Services.AddScoped<IEventStreamService, EventStreamService>();
builder.Services.AddScoped<IOutreachEngagementService, OutreachEngagementService>();

// ---- SAI Admin Services (ADM-017 through ADM-023) ----
builder.Services.AddScoped<ITutoringAdminService, TutoringAdminService>();
builder.Services.AddScoped<IExplanationCacheAdminService, ExplanationCacheAdminService>();
builder.Services.AddScoped<IExperimentAdminService, ExperimentAdminService>();
builder.Services.AddScoped<IEmbeddingAdminService, EmbeddingAdminService>();
builder.Services.AddScoped<ITokenBudgetAdminService, TokenBudgetAdminService>();

// =============================================================================
// BUILD & PIPELINE
// =============================================================================

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();

// ---- REV-004: Security response headers ----
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["X-XSS-Protection"] = "0"; // Disabled per OWASP (modern browsers handle CSP)
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    headers["Content-Security-Policy"] =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none';";

    if (!context.Request.Path.StartsWithSegments("/health"))
    {
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    }

    await next();
});

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
app.MapAiGenerationEndpoints();

// ---- SAI Admin endpoints (ADM-017 through ADM-023) ----
app.MapTutoringAdminEndpoints();
app.MapExplanationCacheEndpoints();
app.MapExperimentAdminEndpoints();
app.MapEmbeddingAdminEndpoints();
app.MapTokenBudgetEndpoints();

// ---- Seed predefined roles on startup ----
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var appLogger = app.Services.GetRequiredService<ILogger<Program>>();

lifetime.ApplicationStarted.Register(async () =>
{
    var store = app.Services.GetRequiredService<IDocumentStore>();
    await DatabaseSeeder.SeedAllAsync(store, appLogger, 300,
        (s, l) => SimulationEventSeeder.SeedSimulationEventsAsync(s, l),
        QuestionBankSeedData.SeedQuestionsAsync);
});

app.Run();
