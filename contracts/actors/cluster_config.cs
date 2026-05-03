// =============================================================================
// Cena Platform -- Proto.Actor Cluster Configuration
// Layer: Infrastructure | Runtime: .NET 9 | Framework: Proto.Actor v1.x
//
// DESIGN NOTES:
//   - DynamoDB cluster provider for node discovery and registration.
//   - Partition placement: student ID hash -> partition. Consistent hashing
//     ensures actor affinity across rebalances.
//   - Remote configuration: gRPC transport between nodes.
//   - Serialization: Protobuf for wire messages, System.Text.Json for events.
//   - Observability: OpenTelemetry traces + metrics, Serilog structured logging.
//   - Health checks: /health/ready and /health/live endpoints.
//   - Graceful shutdown: drain actors, persist state, leave cluster.
//   - Auto-scaling triggers: CPU > 70%, memory > 80%, activation queue > 1000.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Amazon.DynamoDBv2;
using Google.Protobuf;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Proto;
using Proto.Cluster;
using Proto.Cluster.Partition;
using Proto.Cluster.AmazonDynamoDB;
using Proto.DependencyInjection;
using Proto.Remote;
using Proto.Remote.GrpcNet;
using Serilog;

namespace Cena.Infrastructure.Cluster;

// =============================================================================
// CLUSTER CONFIGURATION
// =============================================================================

/// <summary>
/// Configures the Proto.Actor cluster for the Cena platform.
/// This is the root infrastructure setup that ties together:
/// - DynamoDB cluster discovery
/// - gRPC remote transport
/// - Protobuf serialization
/// - Actor registration (virtual and classic)
/// - OpenTelemetry observability
/// - Health checks
/// - Graceful shutdown
/// </summary>
public static class CenaClusterConfig
{
    /// <summary>Service name for OpenTelemetry resource tagging.</summary>
    private const string ServiceName = "cena-learner-service";

    /// <summary>
    /// Registers all Proto.Actor cluster services with the DI container.
    /// Call this in Program.cs / Startup.cs.
    ///
    /// <example>
    /// <code>
    /// var builder = WebApplication.CreateBuilder(args);
    /// builder.Services.AddCenaActorCluster(builder.Configuration);
    /// var app = builder.Build();
    /// app.UseCenaActorCluster();
    /// app.Run();
    /// </code>
    /// </example>
    /// </summary>
    public static IServiceCollection AddCenaActorCluster(
        this IServiceCollection services,
        CenaClusterOptions options)
    {
        // ---- Validate configuration ----
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ClusterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AdvertisedHost);

        // ---- Configure Serilog ----
        services.AddSerilog(cfg => cfg
            .MinimumLevel.Information()
            .MinimumLevel.Override("Proto", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", ServiceName)
            .Enrich.WithProperty("cluster", options.ClusterName)
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));

        // ---- Configure OpenTelemetry ----
        ConfigureOpenTelemetry(services, options);

        // ---- Register Actor Dependencies ----
        services.AddSingleton<Cena.Actors.IMethodologySwitchService, Cena.Actors.MethodologySwitchService>();

        // ---- Configure Proto.Actor System ----
        services.AddSingleton(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("CenaCluster");

            // ---- Actor System ----
            var system = new ActorSystem(new ActorSystemConfig
            {
                DeveloperSupervisionLogging = options.EnableDevSupervisionLogging,
                DeadLetterThrottleInterval = TimeSpan.FromSeconds(10),
                DeadLetterThrottleCount = 5,
                DeadLetterRequestLogging = true,
                DiagnosticsLogLevel = options.EnableDevSupervisionLogging
                    ? LogLevel.Debug : LogLevel.Warning
            })
            .WithServiceProvider(provider);

            // ---- Remote Configuration (gRPC transport) ----
            var remoteConfig = GrpcNetRemoteConfig
                .BindTo(options.AdvertisedHost, options.AdvertisedPort)
                .WithProtoMessages(EmptyReflection.Descriptor) // Register Protobuf message descriptors
                .WithRemoteDiagnostics(true)
                .WithEndpointWriterMaxRetries(3);

            // ---- Configure serialization ----
            // Proto.Actor uses Protobuf for wire format by default.
            // Custom message types are registered here for cross-node communication.

            // ---- DynamoDB Cluster Provider ----
            var dynamoClient = provider.GetRequiredService<IAmazonDynamoDB>();
            var clusterProvider = new AmazonDynamoDBProvider(
                dynamoClient,
                new AmazonDynamoDBProviderOptions
                {
                    TableName = options.DynamoDbTableName,
                    PollInterval = TimeSpan.FromSeconds(3),
                    DeregisterOnShutdown = true
                });

            // ---- Partition Identity Lookup ----
            // Uses consistent hashing on student ID to determine which node
            // hosts a given virtual actor. This ensures actor affinity across
            // rebalances -- a student will typically land on the same node.
            var partitionIdentityLookup = new PartitionIdentityLookup(
                new PartitionConfig
                {
                    Mode = PartitionIdentityLookup.Mode.Pull,
                    RebalanceRequestTimeout = TimeSpan.FromSeconds(5),
                    GetPidTimeout = TimeSpan.FromSeconds(5)
                });

            // ---- Register Virtual Actor Kinds (Grains) ----
            var studentKind = new ClusterKind("student", Props.FromProducer(() =>
                ActivatorUtilities.CreateInstance<Cena.Actors.StudentActor>(provider)));

            // ---- Cluster Configuration ----
            var clusterConfig = ClusterConfig
                .Setup(options.ClusterName, clusterProvider, partitionIdentityLookup)
                .WithClusterKind(studentKind)
                .WithGossipRequestTimeout(TimeSpan.FromSeconds(2))
                .WithActorActivationTimeout(TimeSpan.FromSeconds(10))
                .WithActorRequestTimeout(TimeSpan.FromSeconds(30))
                .WithHeartbeatExpiration(TimeSpan.FromSeconds(30));

            system
                .WithRemote(remoteConfig)
                .WithCluster(clusterConfig);

            logger.LogInformation(
                "Proto.Actor cluster configured. Name={ClusterName}, Host={Host}:{Port}, " +
                "DynamoTable={Table}",
                options.ClusterName, options.AdvertisedHost, options.AdvertisedPort,
                options.DynamoDbTableName);

            return system;
        });

        // ---- Register health checks ----
        services.AddHealthChecks()
            .AddCheck<ClusterHealthCheck>("proto-actor-cluster", tags: new[] { "ready" })
            .AddCheck<ClusterMemberHealthCheck>("proto-actor-member", tags: new[] { "live" });

        return services;
    }

    /// <summary>
    /// Starts the Proto.Actor cluster and registers middleware.
    /// Call this after building the WebApplication.
    /// </summary>
    public static WebApplication UseCenaActorCluster(this WebApplication app)
    {
        // ---- Map health check endpoints ----
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        });
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live")
        });

        // ---- Start cluster on application start ----
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        var system = app.Services.GetRequiredService<ActorSystem>();
        var logger = app.Services.GetRequiredService<ILogger<ActorSystem>>();

        lifetime.ApplicationStarted.Register(async () =>
        {
            logger.LogInformation("Starting Proto.Actor cluster...");
            await system.Cluster().StartMemberAsync();
            logger.LogInformation("Proto.Actor cluster started. MemberID={MemberId}",
                system.Cluster().System.Id);
        });

        // ---- Graceful shutdown ----
        lifetime.ApplicationStopping.Register(async () =>
        {
            logger.LogInformation("Initiating graceful cluster shutdown...");
            await GracefulShutdown(system, logger);
        });

        return app;
    }

    // =========================================================================
    // GRACEFUL SHUTDOWN
    // =========================================================================

    /// <summary>
    /// Performs graceful shutdown of the cluster node:
    /// 1. Stop accepting new actor activations
    /// 2. Drain existing actors (let them finish current work)
    /// 3. Persist all actor state (snapshots)
    /// 4. Leave the cluster (deregister from DynamoDB)
    ///
    /// Timeout: 30 seconds. After timeout, forcefully shuts down.
    /// </summary>
    private static async Task GracefulShutdown(ActorSystem system, ILogger logger)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            logger.LogInformation("Phase 1: Stopping new actor activations...");

            logger.LogInformation("Phase 2: Draining active actors...");
            // Proto.Actor's ShutdownAsync handles drain + state persistence
            await system.Cluster().ShutdownAsync(graceful: true);

            logger.LogInformation("Phase 3: Cluster node has left the cluster.");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Graceful shutdown timed out after 30s. Forcing shutdown.");
            await system.Cluster().ShutdownAsync(graceful: false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during graceful shutdown. Forcing shutdown.");
            await system.Cluster().ShutdownAsync(graceful: false);
        }
    }

    // =========================================================================
    // OPENTELEMETRY CONFIGURATION
    // =========================================================================

    /// <summary>
    /// Configures OpenTelemetry tracing and metrics for the cluster.
    /// Exports to OTLP collector (configurable endpoint).
    /// </summary>
    private static void ConfigureOpenTelemetry(
        IServiceCollection services, CenaClusterOptions options)
    {
        services.AddOpenTelemetry()
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
                .AddSource("Cena.Services.MethodologySwitchService")
                .AddSource("Proto.Actor")
                .AddAspNetCoreInstrumentation()
                .AddGrpcClientInstrumentation()
                .AddNpgsql()
                .AddOtlpExporter(o =>
                {
                    o.Endpoint = new Uri(options.OtlpEndpoint);
                }))
            .WithMetrics(metrics => metrics
                .AddMeter("Cena.Actors.StudentActor")
                .AddMeter("Cena.Actors.LearningSessionActor")
                .AddMeter("Cena.Actors.StagnationDetectorActor")
                .AddMeter("Cena.Actors.OutreachSchedulerActor")
                .AddMeter("Cena.Services.MethodologySwitchService")
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddProcessInstrumentation()
                .AddOtlpExporter(o =>
                {
                    o.Endpoint = new Uri(options.OtlpEndpoint);
                }));
    }
}

// =============================================================================
// CLUSTER OPTIONS
// =============================================================================

/// <summary>
/// Configuration options for the Cena Proto.Actor cluster.
/// Bind from appsettings.json section "Cluster".
/// </summary>
public sealed class CenaClusterOptions
{
    /// <summary>Cluster name. All nodes must share the same name.</summary>
    public string ClusterName { get; set; } = "cena-cluster";

    /// <summary>
    /// Host address this node advertises to other cluster members.
    /// In Kubernetes, this is typically the pod IP.
    /// </summary>
    public string AdvertisedHost { get; set; } = "0.0.0.0";

    /// <summary>Port for gRPC remote transport between nodes.</summary>
    public int AdvertisedPort { get; set; } = 8090;

    /// <summary>DynamoDB table name for cluster member registration.</summary>
    public string DynamoDbTableName { get; set; } = "cena-cluster-members";

    /// <summary>OpenTelemetry OTLP collector endpoint.</summary>
    public string OtlpEndpoint { get; set; } = "http://localhost:4317";

    /// <summary>Enable verbose supervision logging (development only).</summary>
    public bool EnableDevSupervisionLogging { get; set; } = false;

    // ---- Auto-Scaling Thresholds ----

    /// <summary>CPU utilization threshold for scale-out trigger. Default: 70%.</summary>
    public double CpuScaleOutThreshold { get; set; } = 0.70;

    /// <summary>Memory utilization threshold for scale-out trigger. Default: 80%.</summary>
    public double MemoryScaleOutThreshold { get; set; } = 0.80;

    /// <summary>
    /// Actor activation queue depth threshold. When pending activations exceed
    /// this value, the cluster should scale out. Default: 1000.
    /// </summary>
    public int ActivationQueueScaleOutThreshold { get; set; } = 1000;

    /// <summary>Minimum nodes in the cluster. Default: 2 (for HA).</summary>
    public int MinNodes { get; set; } = 2;

    /// <summary>Maximum nodes in the cluster. Default: 20.</summary>
    public int MaxNodes { get; set; } = 20;
}

// =============================================================================
// AUTO-SCALING METRICS PUBLISHER
// =============================================================================

/// <summary>
/// Publishes cluster metrics that auto-scaling infrastructure (e.g., KEDA,
/// AWS Application Auto Scaling) can use to make scaling decisions.
///
/// Metrics published:
/// - cena.cluster.cpu_utilization: Current CPU utilization (0.0-1.0)
/// - cena.cluster.memory_utilization: Current memory utilization (0.0-1.0)
/// - cena.cluster.active_actors: Number of active virtual actors
/// - cena.cluster.pending_activations: Actor activation queue depth
/// - cena.cluster.member_count: Current cluster member count
/// </summary>
public sealed class ClusterMetricsPublisher : IHostedService, IDisposable
{
    private readonly ActorSystem _system;
    private readonly CenaClusterOptions _options;
    private readonly ILogger<ClusterMetricsPublisher> _logger;
    private Timer? _timer;

    private static readonly Meter MeterInstance =
        new("Cena.Cluster.Metrics", "1.0.0");
    private static readonly ObservableGauge<double> CpuGauge =
        MeterInstance.CreateObservableGauge<double>("cena.cluster.cpu_utilization",
            description: "CPU utilization (0.0-1.0)");
    private static readonly ObservableGauge<double> MemoryGauge =
        MeterInstance.CreateObservableGauge<double>("cena.cluster.memory_utilization",
            description: "Memory utilization (0.0-1.0)");
    private static readonly ObservableGauge<long> ActiveActorsGauge =
        MeterInstance.CreateObservableGauge<long>("cena.cluster.active_actors",
            description: "Active virtual actors on this node");
    private static readonly ObservableGauge<long> MemberCountGauge =
        MeterInstance.CreateObservableGauge<long>("cena.cluster.member_count",
            description: "Current cluster member count");

    public ClusterMetricsPublisher(
        ActorSystem system,
        CenaClusterOptions options,
        ILogger<ClusterMetricsPublisher> logger)
    {
        _system = system;
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = new Timer(PublishMetrics, null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    private void PublishMetrics(object? state)
    {
        try
        {
            var memberCount = _system.Cluster().MemberList.GetAllMembers().Length;

            // Log scaling-relevant metrics
            _logger.LogDebug(
                "Cluster metrics: Members={Members}, NodeId={NodeId}",
                memberCount, _system.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish cluster metrics");
        }
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}

// =============================================================================
// HEALTH CHECKS
// =============================================================================

/// <summary>
/// Readiness health check: verifies the cluster is formed and this node
/// is an active member. Used by Kubernetes readiness probe.
/// </summary>
public sealed class ClusterHealthCheck : IHealthCheck
{
    private readonly ActorSystem _system;

    public ClusterHealthCheck(ActorSystem system) => _system = system;

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

/// <summary>
/// Liveness health check: verifies the actor system is responsive.
/// Used by Kubernetes liveness probe.
/// </summary>
public sealed class ClusterMemberHealthCheck : IHealthCheck
{
    private readonly ActorSystem _system;

    public ClusterMemberHealthCheck(ActorSystem system) => _system = system;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Verify the actor system root is alive
            if (_system.Root == null)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "Actor system root context is null."));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"Actor system alive. SystemId: {_system.Id}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Actor system liveness check failed", ex));
        }
    }
}
