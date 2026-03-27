// =============================================================================
// Cena Platform -- ASP.NET Core Host for Proto.Actor Cluster
// Layer: Infrastructure | Runtime: .NET 9
//
// Configures: Marten (PostgreSQL event store), Proto.Actor cluster,
//             Redis cache, NATS messaging, OpenTelemetry, Serilog, health checks.
// =============================================================================

using Cena.Actors.Api;
using Cena.Actors.Configuration;
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

// ACT-032: Register all domain services for DI
builder.Services.AddSingleton<IMethodologySwitchService, DefaultMethodologySwitchService>();
builder.Services.AddSingleton<IBktService, BktService>();
builder.Services.AddSingleton<IHlrService, HlrService>();
builder.Services.AddSingleton<ICognitiveLoadService, CognitiveLoadService>();
builder.Services.AddSingleton<IFocusDegradationService, FocusDegradationService>();
builder.Services.AddSingleton<IPrerequisiteEnforcementService, PrerequisiteEnforcementService>();
builder.Services.AddSingleton<IDecayPropagationService, DecayPropagationService>();
builder.Services.AddSingleton<OfflineSyncHandler>();
builder.Services.AddHostedService<NatsOutboxPublisher>();

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

    system
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

// ---- Mastery REST API endpoints (MST-017) ----
app.MapMasteryEndpoints();

// ---- Cluster lifecycle ----
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
var actorSystem = app.Services.GetRequiredService<ActorSystem>();
var appLogger = app.Services.GetRequiredService<ILogger<Program>>();

lifetime.ApplicationStarted.Register(async () =>
{
    appLogger.LogInformation("Starting Proto.Actor cluster...");
    await actorSystem.Cluster().StartMemberAsync();
    appLogger.LogInformation("Proto.Actor cluster started. MemberID={MemberId}", actorSystem.Id);
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

// =============================================================================
// DEFAULT METHODOLOGY SWITCH SERVICE (production implementation)
// =============================================================================

/// <summary>
/// Methodology switch service using MCM graph error-type mapping.
/// Maps error types to methodology recommendations with confidence scoring.
/// </summary>
public sealed class DefaultMethodologySwitchService : IMethodologySwitchService
{
    // MCM graph: error type -> ordered methodology preferences
    private static readonly Dictionary<ErrorType, Methodology[]> ErrorToMethodologyMap = new()
    {
        [ErrorType.Conceptual] = [Methodology.Feynman, Methodology.Analogy, Methodology.Socratic, Methodology.WorkedExample],
        [ErrorType.Procedural] = [Methodology.WorkedExample, Methodology.DrillAndPractice, Methodology.BloomsProgression],
        [ErrorType.Motivational] = [Methodology.ProjectBased, Methodology.RetrievalPractice, Methodology.SpacedRepetition],
        [ErrorType.None] = [Methodology.Socratic, Methodology.SpacedRepetition, Methodology.Feynman]
    };

    public Task<DecideSwitchResponse> DecideSwitch(DecideSwitchRequest request)
    {
        var candidateMethods = ErrorToMethodologyMap
            .GetValueOrDefault(request.DominantErrorType, ErrorToMethodologyMap[ErrorType.None])!;

        // Filter out previously attempted methodologies
        var alreadyTried = new HashSet<string>(request.MethodAttemptHistory, StringComparer.OrdinalIgnoreCase);
        alreadyTried.Add(request.CurrentMethodology.ToString());

        var available = candidateMethods
            .Where(m => !alreadyTried.Contains(m.ToString()))
            .ToArray();

        if (available.Length == 0)
        {
            // All methodologies exhausted -- try remaining enum values not yet attempted
            available = Enum.GetValues<Methodology>()
                .Where(m => !alreadyTried.Contains(m.ToString()))
                .ToArray();
        }

        if (available.Length == 0)
        {
            return Task.FromResult(new DecideSwitchResponse(
                ShouldSwitch: false,
                RecommendedMethodology: request.CurrentMethodology,
                Confidence: 0.0,
                AllMethodologiesExhausted: true,
                EscalationAction: "refer_to_human_tutor",
                DecisionTrace: $"All {Enum.GetValues<Methodology>().Length} methodologies exhausted " +
                    $"for concept {request.ConceptId}. Student needs human tutor intervention."));
        }

        // Confidence based on stagnation severity and position in preference list
        double confidence = Math.Max(0.3, 1.0 - (request.StagnationScore * 0.5));
        if (request.ConsecutiveStagnantSessions > 3)
            confidence *= 0.8;

        var recommended = available[0];

        return Task.FromResult(new DecideSwitchResponse(
            ShouldSwitch: true,
            RecommendedMethodology: recommended,
            Confidence: confidence,
            AllMethodologiesExhausted: false,
            EscalationAction: null,
            DecisionTrace: $"Switching from {request.CurrentMethodology} to {recommended} " +
                $"due to {request.DominantErrorType} errors. " +
                $"Stagnation={request.StagnationScore:F3}, " +
                $"Confidence={confidence:F3}, " +
                $"Candidates=[{string.Join(", ", available.Select(m => m.ToString()))}]"));
    }
}
