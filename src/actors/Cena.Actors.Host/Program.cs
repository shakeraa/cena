// =============================================================================
// Cena Platform -- ASP.NET Core Host for Proto.Actor Cluster
// Layer: Infrastructure | Runtime: .NET 9
//
// Configures: Marten (PostgreSQL event store), Proto.Actor cluster,
//             Redis cache, NATS messaging, OpenTelemetry, Serilog, health checks.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Api;
using Cena.Admin.Api;
using Cena.Actors.Configuration;
using Cena.Infrastructure.Seed;
using Cena.Actors.Gateway;
using Cena.Actors.Infrastructure;
using Cena.Actors.Services;
using Cena.Actors.Students;
using Cena.Actors.Sync;
using Marten;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NATS.Client.Core;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Proto;
using Proto.Cluster;
using Proto.Cluster.Partition;
using Proto.Cluster.Testing;
using Proto.DependencyInjection;
using Proto.Remote;
using Proto.Remote.GrpcNet;
using Serilog;
using StackExchange.Redis;

const string ServiceName = "cena-learner-service";

// =============================================================================
// 1. BUILDER CONFIGURATION
// =============================================================================

var builder = WebApplication.CreateBuilder(args);

// ---- Serilog structured logging ----
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration.ReadFrom.Configuration(context.Configuration);
});

// ---- Read configuration ----
var pgConnectionString = builder.Configuration.GetConnectionString("PostgreSQL")
    ?? "Host=localhost;Port=5432;Database=cena;Username=cena;Password=;Include Error Detail=true";
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? "localhost:6379";
var natsUrl = builder.Configuration.GetConnectionString("NATS")
    ?? "nats://localhost:4222";

var clusterName = builder.Configuration.GetValue<string>("Cluster:ClusterName") ?? "cena-cluster";
var advertisedHost = builder.Configuration.GetValue<string>("Cluster:AdvertisedHost") ?? "0.0.0.0";
var advertisedPort = builder.Configuration.GetValue<int>("Cluster:AdvertisedPort", 8090);
var otlpEndpoint = builder.Configuration.GetValue<string>("Cluster:OtlpEndpoint") ?? "http://localhost:4317";
var enableDevLogging = builder.Configuration.GetValue<bool>("Cluster:EnableDevSupervisionLogging", true);

// =============================================================================
// 2. MARTEN (PostgreSQL event store)
// =============================================================================

builder.Services.AddMarten(opts =>
{
    opts.ConfigureCenaEventStore(pgConnectionString);
});

// =============================================================================
// 3. REDIS
// =============================================================================

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<IConnectionMultiplexer>>();
    try
    {
        var multiplexer = ConnectionMultiplexer.Connect(redisConnectionString);
        logger.LogInformation("Connected to Redis at {RedisConnection}", redisConnectionString);
        return multiplexer;
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to connect to Redis at {RedisConnection}. Using in-memory fallback.", redisConnectionString);
        // Return a connection that will retry -- ConnectionMultiplexer handles reconnection
        var options = ConfigurationOptions.Parse(redisConnectionString);
        options.AbortOnConnectFail = false;
        options.ConnectRetry = 3;
        options.ConnectTimeout = 5000;
        return ConnectionMultiplexer.Connect(options);
    }
});

// =============================================================================
// 4. NATS
// =============================================================================

builder.Services.AddSingleton<INatsConnection>(sp =>
{
    var logger = sp.GetRequiredService<ILogger<INatsConnection>>();
    var opts = new NatsOpts
    {
        Url = natsUrl,
        Name = "cena-actors-host"
    };

    logger.LogInformation("Configuring NATS connection to {NatsUrl}", natsUrl);
    return new NatsConnection(opts);
});

// =============================================================================
// 5. DOMAIN SERVICES
// =============================================================================

// Firebase Admin SDK (required by AdminUserService/AdminRoleService)
builder.Services.AddSingleton<Cena.Infrastructure.Firebase.IFirebaseAdminService,
    Cena.Infrastructure.Firebase.FirebaseAdminService>();

// Curriculum graph cache (needed by Mastery REST API endpoints)
builder.Services.AddSingleton<Cena.Actors.Mastery.IConceptGraphCache>(
    Cena.Actors.Simulation.CurriculumSeedData.BuildGraphCache());

// ACT-032: Register all domain services for DI
builder.Services.AddSingleton<IMethodologySwitchService, MethodologySwitchService>();
builder.Services.AddSingleton<IBktService, BktService>();
builder.Services.AddSingleton<IHlrService, HlrService>();
builder.Services.AddSingleton<ICognitiveLoadService, CognitiveLoadService>();
builder.Services.AddSingleton<IFocusDegradationService, FocusDegradationService>();
builder.Services.AddSingleton<IPrerequisiteEnforcementService, PrerequisiteEnforcementService>();
builder.Services.AddSingleton<IDecayPropagationService, DecayPropagationService>();
builder.Services.AddSingleton<OfflineSyncHandler>();
builder.Services.AddHostedService<NatsOutboxPublisher>();

// NATS Bus Router: bridges NATS commands ↔ Proto.Actor virtual actors
builder.Services.AddSingleton<Cena.Actors.Bus.NatsBusRouter>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Cena.Actors.Bus.NatsBusRouter>());

// Quality Gate service (needed by QuestionBankService)
builder.Services.AddSingleton<Cena.Admin.Api.QualityGate.IQualityGateService,
    Cena.Admin.Api.QualityGate.QualityGateService>();

// ADM-004 through ADM-016: Register Admin API services
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
builder.Services.AddScoped<IAdminRoleService, AdminRoleService>();
builder.Services.AddScoped<IContentModerationService, ContentModerationService>();
builder.Services.AddScoped<IFocusAnalyticsService, FocusAnalyticsService>();
builder.Services.AddScoped<IMasteryTrackingService, MasteryTrackingService>();
builder.Services.AddScoped<ISystemMonitoringService, SystemMonitoringService>();
builder.Services.AddScoped<IIngestionPipelineService, IngestionPipelineService>();
builder.Services.AddScoped<IQuestionBankService, QuestionBankService>();
builder.Services.AddScoped<IMethodologyAnalyticsService, MethodologyAnalyticsService>();
builder.Services.AddScoped<Cena.Admin.Api.ICulturalContextService, Cena.Admin.Api.CulturalContextService>();
builder.Services.AddScoped<IEventStreamService, EventStreamService>();
builder.Services.AddScoped<IOutreachEngagementService, OutreachEngagementService>();

// =============================================================================
// 6. PROTO.ACTOR CLUSTER
// =============================================================================

var isDevelopment = builder.Environment.IsDevelopment();

builder.Services.AddSingleton(provider =>
{
    var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
    var logger = loggerFactory.CreateLogger("CenaCluster");

    // Actor System
    var system = new ActorSystem(new ActorSystemConfig
    {
        DeveloperSupervisionLogging = enableDevLogging,
        DeadLetterThrottleInterval = TimeSpan.FromSeconds(10),
        DeadLetterThrottleCount = 5,
        DeadLetterRequestLogging = true,
        DiagnosticsLogLevel = enableDevLogging ? LogLevel.Debug : LogLevel.Warning
    })
    .WithServiceProvider(provider);

    // Remote Configuration — simplified for Proto.Actor 1.8.0
    // In dev: TestProvider handles local clustering without explicit remote config
    // In prod: will use GrpcNet remote (configured via Kubernetes/ECS service discovery)

    // Cluster provider: in-memory for dev, DynamoDB for prod
    IClusterProvider clusterProvider;
    if (isDevelopment)
    {
        clusterProvider = new TestProvider(new TestProviderOptions(), new InMemAgent());
        logger.LogInformation("Using in-memory TestProvider for development cluster");
    }
    else
    {
        // Production: DynamoDB cluster provider configured externally
        // For now, use TestProvider until DynamoDB SDK is wired in
        clusterProvider = new TestProvider(new TestProviderOptions(), new InMemAgent());
        logger.LogWarning(
            "DynamoDB cluster provider not configured. Using in-memory TestProvider. " +
            "Set up AWS DynamoDB credentials for production.");
    }

    // Partition Identity Lookup (consistent hashing on student ID)
    var partitionIdentityLookup = new PartitionIdentityLookup(
        new PartitionConfig
        {
            Mode = PartitionIdentityLookup.Mode.Pull,
            RebalanceRequestTimeout = TimeSpan.FromSeconds(5),
            GetPidTimeout = TimeSpan.FromSeconds(5)
        });

    // Register Virtual Actor Kinds (Grains)
    var studentKind = new ClusterKind("student", Props.FromProducer(() =>
        ActivatorUtilities.CreateInstance<StudentActor>(provider)));

    // Cluster Configuration
    var clusterConfig = ClusterConfig
        .Setup(clusterName, clusterProvider, partitionIdentityLookup)
        .WithClusterKind(studentKind)
        .WithGossipRequestTimeout(TimeSpan.FromSeconds(2))
        .WithActorActivationTimeout(TimeSpan.FromSeconds(10))
        .WithActorRequestTimeout(TimeSpan.FromSeconds(30))
        .WithHeartbeatExpiration(TimeSpan.FromSeconds(30));

    // Remote serialization required by cluster, even in single-node dev.
    system
        .WithRemote(RemoteConfig.BindToLocalhost())
        .WithCluster(clusterConfig);

    logger.LogInformation(
        "Proto.Actor cluster configured. Name={ClusterName}, Host={Host}:{Port}",
        clusterName, advertisedHost, advertisedPort);

    return system;
});

// =============================================================================
// 7. OPENTELEMETRY
// =============================================================================

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: ServiceName,
            serviceVersion: "1.0.0",
            serviceInstanceId: Environment.MachineName))
    .WithTracing(tracing => tracing
        .AddSource("Cena.Actors.StudentActor")
        .AddSource("Cena.Actors.LearningSessionActor")
        .AddSource("Cena.Actors.StagnationDetectorActor")
        .AddSource("Cena.Actors.OutreachSchedulerActor")
        .AddSource("Proto.Actor")
        .AddAspNetCoreInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint)))
    .WithMetrics(metrics => metrics
        .AddMeter("Cena.Actors.StudentActor")
        .AddMeter("Cena.Actors.LearningSessionActor")
        .AddMeter("Cena.Actors.LlmCircuitBreaker")
        .AddMeter("Cena.Actors.CurriculumGraph")
        .AddMeter("Cena.Actors.DeadLetterWatcher")
        .AddMeter("Cena.Infrastructure.NatsOutbox")
        .AddMeter("Cena.Actors.Decay")
        .AddMeter("Cena.Actors.Focus")
        .AddMeter("Cena.Actors.HealthAggregator")
        .AddAspNetCoreInstrumentation()
        .AddRuntimeInstrumentation()
        .AddProcessInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint))
        .AddPrometheusExporter());

// =============================================================================
// 8. HEALTH CHECKS
// =============================================================================

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy("Host is running"), tags: ["live"])
    .AddCheck<ProtoActorHealthCheck>("proto-actor-cluster", tags: ["ready"]);

// =============================================================================
// BUILD & CONFIGURE PIPELINE
// =============================================================================

var app = builder.Build();

// ---- Prometheus metrics endpoint (RES-002) ----
app.MapPrometheusScrapingEndpoint();

// ---- Health check endpoints ----
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

// ---- Actor stats endpoint (real-time from NatsBusRouter) ----
app.MapGet("/api/actors/stats", (Cena.Actors.Bus.NatsBusRouter router) =>
{
    var actors = router.ActiveActors.Values
        .OrderByDescending(a => a.LastActivity)
        .Select(a => new
        {
            studentId = a.StudentId,
            sessionId = a.SessionId,
            messagesProcessed = a.MessagesProcessed,
            totalAttempts = a.TotalAttempts,
            correctAttempts = a.CorrectAttempts,
            accuracy = a.TotalAttempts > 0 ? Math.Round((double)a.CorrectAttempts / a.TotalAttempts * 100, 1) : 0,
            lastActivity = a.LastActivity,
            activatedAt = a.ActivatedAt,
            uptimeSeconds = (DateTimeOffset.UtcNow - a.ActivatedAt).TotalSeconds,
            status = a.Status
        });

    return Results.Ok(new
    {
        timestamp = DateTimeOffset.UtcNow,
        commandsRouted = router.CommandsRouted,
        eventsPublished = router.EventsPublished,
        sessionsStarted = router.SessionsStarted,
        errorsCount = router.ErrorsCount,
        activeActorCount = router.ActiveActors.Count,
        actors = actors
    });
}).WithName("GetActorStats");

// ---- Mastery REST API endpoints (MST-017) ----
app.MapMasteryEndpoints();

// ---- Admin REST API endpoints (ADM-004 through ADM-016) ----
app.MapAdminDashboardEndpoints();
app.MapAdminUserEndpoints();
app.MapAdminRoleEndpoints();
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

// ---- Cluster lifecycle ----
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var actorSystem = app.Services.GetRequiredService<ActorSystem>();
var appLogger = app.Services.GetRequiredService<ILogger<Program>>();

lifetime.ApplicationStarted.Register(async () =>
{
    appLogger.LogInformation("Starting Proto.Actor cluster...");
    await actorSystem.Cluster().StartMemberAsync();
    appLogger.LogInformation("Proto.Actor cluster started. MemberID={MemberId}", actorSystem.Id);

    // RES-003: Spawn Redis circuit breaker actor at root level
    var redisCbProps = Props.FromProducer(() =>
        new LlmCircuitBreakerActor(
            CircuitBreakerConfig.Redis,
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<LlmCircuitBreakerActor>(),
            app.Services.GetRequiredService<IMeterFactory>()));
    var redisCbPid = actorSystem.Root.SpawnNamed(redisCbProps, "circuit-breaker-redis");
    appLogger.LogInformation("RES-003: Redis circuit breaker spawned at {Pid}", redisCbPid);

    // RES-005: Spawn Health Aggregator singleton
    var healthProps = Props.FromProducer(() =>
        new HealthAggregatorActor(
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<HealthAggregatorActor>(),
            app.Services.GetRequiredService<IMeterFactory>()));
    var healthPid = actorSystem.Root.SpawnNamed(healthProps, "health-aggregator");
    // Register the Redis CB for health polling
    actorSystem.Root.Send(healthPid, new HealthAggregatorActor.RegisterHealthSources(
        new Dictionary<string, PID> { ["redis"] = redisCbPid },
        ManagerPid: null)); // Manager PID can be registered later when available
    appLogger.LogInformation("RES-005: Health aggregator spawned at {Pid}", healthPid);

    // RES-010: Spawn Feature Flag singleton
    var ffProps = Props.FromProducer(() =>
        new FeatureFlagActor(
            app.Services.GetRequiredService<ILoggerFactory>().CreateLogger<FeatureFlagActor>()));
    var ffPid = actorSystem.Root.SpawnNamed(ffProps, "feature-flags");
    appLogger.LogInformation("RES-010: Feature flag service spawned at {Pid}", ffPid);

    // Seed all demo data via single entry point
    var store = app.Services.GetRequiredService<IDocumentStore>();
    await DatabaseSeeder.SeedAllAsync(store, appLogger);
});

lifetime.ApplicationStopping.Register(async () =>
{
    appLogger.LogInformation("Initiating graceful cluster shutdown...");
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try
    {
        await actorSystem.Cluster().ShutdownAsync(graceful: true);
        appLogger.LogInformation("Cluster shut down gracefully.");
    }
    catch (OperationCanceledException)
    {
        appLogger.LogWarning("Graceful shutdown timed out. Forcing shutdown.");
        await actorSystem.Cluster().ShutdownAsync(graceful: false);
    }
    catch (Exception ex)
    {
        appLogger.LogError(ex, "Error during shutdown. Forcing shutdown.");
        await actorSystem.Cluster().ShutdownAsync(graceful: false);
    }
});

app.Run();

// =============================================================================
// HEALTH CHECK: Proto.Actor cluster readiness
// =============================================================================

public sealed class ProtoActorHealthCheck : IHealthCheck
{
    private readonly ActorSystem _system;

    public ProtoActorHealthCheck(ActorSystem system) => _system = system;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var members = _system.Cluster().MemberList.GetAllMembers();
            if (members.Length == 0)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "No cluster members found. Cluster may not be formed."));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Cluster active. Members: {members.Length}, NodeId: {_system.Id}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Cluster health check failed", ex));
        }
    }
}

