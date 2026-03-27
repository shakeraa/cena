// =============================================================================
// Cena Platform -- ActorSystemManager (Root Guardian, Singleton)
// Layer: Actor Topology | Runtime: .NET 9 | Framework: Proto.Actor v1.x
//
// Root guardian singleton actor that bootstraps all singleton actors in
// dependency order:
//   1. CurriculumGraphActor (knowledge graph)
//   2. McmGraphActor (child of CurriculumGraphActor -- spawned automatically)
//   3. LlmGatewayActor (spawns per-model circuit breakers)
//   4. StudentActorManager (activation pool)
//   5. GracefulShutdownCoordinator wiring
//
// Provides health check aggregation across all managed singletons.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Proto;
using Cena.Actors.Graph;
using Cena.Actors.Gateway;
using Cena.Actors.Management;
using Cena.Actors.Infrastructure;

namespace Cena.Actors.Topology;

// =============================================================================
// MESSAGES
// =============================================================================

/// <summary>Query health of all managed singleton actors.</summary>
public sealed record GetSystemHealth;

/// <summary>Aggregated health report for all singleton actors.</summary>
public sealed record SystemHealthReport(
    bool AllHealthy,
    IReadOnlyDictionary<string, SingletonHealth> Singletons,
    DateTimeOffset BootedAt,
    long UptimeSeconds);

/// <summary>Health status of a single managed actor.</summary>
public sealed record SingletonHealth(
    string Name,
    bool IsAlive,
    PID? Pid,
    string Status);

// =============================================================================
// LLM GATEWAY ACTOR (spawns per-model circuit breakers)
// =============================================================================

/// <summary>Query LLM gateway health.</summary>
public sealed record GetGatewayHealth;

/// <summary>LLM gateway health response.</summary>
public sealed record GatewayHealthResponse(
    int CircuitBreakerCount,
    IReadOnlyList<string> ManagedModels);

/// <summary>
/// LLM Gateway actor that manages per-model circuit breakers.
/// Spawns a LlmCircuitBreakerActor for each configured model on startup.
/// Routes LLM requests to the appropriate circuit breaker.
/// </summary>
public sealed class LlmGatewayActor : IActor
{
    private readonly ILogger<LlmGatewayActor> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly Dictionary<string, PID> _circuitBreakers = new(StringComparer.OrdinalIgnoreCase);

    // Default models to spawn circuit breakers for
    private static readonly string[] DefaultModels = { "kimi", "sonnet", "opus" };

    public LlmGatewayActor(ILogger<LlmGatewayActor> logger, ILoggerFactory loggerFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            Started           => OnStarted(context),
            Stopping          => OnStopping(context),
            RequestPermission q => ForwardToModel(context, q),
            ReportSuccess cmd   => ForwardSuccessToModel(context, cmd),
            ReportFailure cmd   => ForwardFailureToModel(context, cmd),
            GetGatewayHealth    => HandleGetHealth(context),
            _ => Task.CompletedTask
        };
    }

    private Task OnStarted(IContext context)
    {
        _logger.LogInformation("LlmGatewayActor starting. Spawning per-model circuit breakers...");

        foreach (var modelName in DefaultModels)
        {
            var config = CircuitBreakerConfig.ForModel(modelName);
            var cbLogger = _loggerFactory.CreateLogger<LlmCircuitBreakerActor>();
            var props = Props.FromProducer(() => new LlmCircuitBreakerActor(config, cbLogger));
            var pid = context.SpawnNamed(props, $"cb-{modelName}");
            _circuitBreakers[modelName] = pid;

            _logger.LogInformation(
                "  Circuit breaker spawned: model={Model}, maxFailures={MaxFailures}, openDuration={Duration}s",
                modelName, config.MaxFailures, config.OpenDuration.TotalSeconds);
        }

        _logger.LogInformation(
            "LlmGatewayActor ready. {Count} circuit breakers active.",
            _circuitBreakers.Count);

        return Task.CompletedTask;
    }

    private Task OnStopping(IContext context)
    {
        _logger.LogInformation("LlmGatewayActor stopping. Stopping {Count} circuit breakers...", _circuitBreakers.Count);
        foreach (var (model, pid) in _circuitBreakers)
        {
            context.Stop(pid);
        }
        _circuitBreakers.Clear();
        return Task.CompletedTask;
    }

    private Task ForwardToModel(IContext context, RequestPermission q)
    {
        if (_circuitBreakers.TryGetValue(q.ModelName, out var pid))
        {
            context.Forward(pid);
        }
        else if (_circuitBreakers.Count > 0)
        {
            context.Forward(_circuitBreakers.Values.First());
        }
        else
        {
            _logger.LogWarning("No circuit breakers available for request {RequestId}", q.RequestId);
        }
        return Task.CompletedTask;
    }

    private Task ForwardSuccessToModel(IContext context, ReportSuccess cmd)
    {
        if (_circuitBreakers.TryGetValue(cmd.ModelName, out var pid))
            context.Send(pid, cmd);
        else
            _logger.LogWarning("No circuit breaker for model {Model} to report success", cmd.ModelName);
        return Task.CompletedTask;
    }

    private Task ForwardFailureToModel(IContext context, ReportFailure cmd)
    {
        if (_circuitBreakers.TryGetValue(cmd.ModelName, out var pid))
            context.Send(pid, cmd);
        else
            _logger.LogWarning("No circuit breaker for model {Model} to report failure", cmd.ModelName);
        return Task.CompletedTask;
    }

    private Task HandleGetHealth(IContext context)
    {
        context.Respond(new GatewayHealthResponse(
            CircuitBreakerCount: _circuitBreakers.Count,
            ManagedModels: _circuitBreakers.Keys.ToList()));
        return Task.CompletedTask;
    }
}

// =============================================================================
// ACTOR SYSTEM MANAGER (Root Guardian)
// =============================================================================

/// <summary>
/// Root guardian singleton actor that bootstraps and manages all top-level
/// singleton actors in the Cena actor system.
///
/// Boot order (dependency-driven):
///   1. CurriculumGraphActor — knowledge graph (McmGraphActor spawned as child)
///   2. LlmGatewayActor — LLM circuit breakers (depends on nothing)
///   3. StudentActorManager — student activation pool
///   4. GracefulShutdownCoordinator wiring — connects manager PID
///
/// Provides aggregated health check across all managed singletons.
/// </summary>
public sealed class ActorSystemManager : IActor
{
    private readonly INeo4jGraphRepository _graphRepository;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ActorSystemManager> _logger;
    private readonly GracefulShutdownCoordinator? _shutdownCoordinator;

    // ── Managed singleton PIDs ──
    private PID? _curriculumGraphPid;
    private PID? _llmGatewayPid;
    private PID? _studentActorManagerPid;

    // ── Boot tracking ──
    private DateTimeOffset _bootedAt;
    private bool _isBooted;

    // ── Telemetry ──
    private static readonly Meter Meter = new("Cena.Actors.Topology", "1.0.0");
    private static readonly Counter<long> BootCounter =
        Meter.CreateCounter<long>("cena.topology.boots_total", description: "Total system boots");
    private static readonly Histogram<double> BootDurationMs =
        Meter.CreateHistogram<double>("cena.topology.boot_duration_ms", description: "System boot duration");

    public ActorSystemManager(
        INeo4jGraphRepository graphRepository,
        ILoggerFactory loggerFactory,
        GracefulShutdownCoordinator? shutdownCoordinator = null)
    {
        _graphRepository = graphRepository ?? throw new ArgumentNullException(nameof(graphRepository));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<ActorSystemManager>();
        _shutdownCoordinator = shutdownCoordinator;
    }

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            Started          => OnStarted(context),
            Stopping         => OnStopping(context),
            GetSystemHealth  => HandleGetSystemHealth(context),
            _ => Task.CompletedTask
        };
    }

    // =========================================================================
    // ORDERED STARTUP
    // =========================================================================

    private Task OnStarted(IContext context)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("=== ActorSystemManager booting ===");

        // ── Step 1: CurriculumGraphActor (+ McmGraphActor as child) ──
        _logger.LogInformation("[Boot 1/4] Spawning CurriculumGraphActor...");
        var graphLogger = _loggerFactory.CreateLogger<CurriculumGraphActor>();
        var graphProps = Props.FromProducer(() =>
            new CurriculumGraphActor(_graphRepository, graphLogger, _loggerFactory));
        _curriculumGraphPid = context.SpawnNamed(graphProps, "curriculum-graph");
        _logger.LogInformation(
            "[Boot 1/4] CurriculumGraphActor spawned. PID={Pid} (McmGraphActor spawns as child)",
            _curriculumGraphPid);

        // ── Step 2: LlmGatewayActor (spawns per-model circuit breakers) ──
        _logger.LogInformation("[Boot 2/4] Spawning LlmGatewayActor...");
        var gatewayLogger = _loggerFactory.CreateLogger<LlmGatewayActor>();
        var gatewayProps = Props.FromProducer(() =>
            new LlmGatewayActor(gatewayLogger, _loggerFactory));
        _llmGatewayPid = context.SpawnNamed(gatewayProps, "llm-gateway");
        _logger.LogInformation(
            "[Boot 2/4] LlmGatewayActor spawned. PID={Pid}",
            _llmGatewayPid);

        // ── Step 3: StudentActorManager ──
        _logger.LogInformation("[Boot 3/4] Spawning StudentActorManager...");
        var managerLogger = _loggerFactory.CreateLogger<StudentActorManager>();
        var managerProps = Props.FromProducer(() =>
            new StudentActorManager(managerLogger));
        _studentActorManagerPid = context.SpawnNamed(managerProps, "student-manager");
        _logger.LogInformation(
            "[Boot 3/4] StudentActorManager spawned. PID={Pid}",
            _studentActorManagerPid);

        // ── Step 4: Wire GracefulShutdownCoordinator ──
        _logger.LogInformation("[Boot 4/4] Wiring GracefulShutdownCoordinator...");
        if (_shutdownCoordinator != null && _studentActorManagerPid != null)
        {
            _shutdownCoordinator.RegisterManagerPid(_studentActorManagerPid);
            _logger.LogInformation(
                "[Boot 4/4] GracefulShutdownCoordinator wired to StudentActorManager.");
        }
        else
        {
            _logger.LogWarning(
                "[Boot 4/4] GracefulShutdownCoordinator not available. " +
                "Graceful shutdown will proceed without manager coordination.");
        }

        sw.Stop();
        _bootedAt = DateTimeOffset.UtcNow;
        _isBooted = true;
        BootCounter.Add(1);
        BootDurationMs.Record(sw.ElapsedMilliseconds);

        _logger.LogInformation(
            "=== ActorSystemManager boot complete. Duration={Duration}ms ===",
            sw.ElapsedMilliseconds);

        return Task.CompletedTask;
    }

    // =========================================================================
    // ORDERED SHUTDOWN
    // =========================================================================

    private Task OnStopping(IContext context)
    {
        _logger.LogInformation("=== ActorSystemManager shutting down ===");

        // Stop in reverse order of boot
        if (_studentActorManagerPid != null)
        {
            _logger.LogInformation("Stopping StudentActorManager...");
            context.Stop(_studentActorManagerPid);
        }

        if (_llmGatewayPid != null)
        {
            _logger.LogInformation("Stopping LlmGatewayActor...");
            context.Stop(_llmGatewayPid);
        }

        if (_curriculumGraphPid != null)
        {
            _logger.LogInformation("Stopping CurriculumGraphActor...");
            context.Stop(_curriculumGraphPid);
        }

        _isBooted = false;
        _logger.LogInformation("=== ActorSystemManager shutdown complete ===");
        return Task.CompletedTask;
    }

    // =========================================================================
    // HEALTH CHECK
    // =========================================================================

    private Task HandleGetSystemHealth(IContext context)
    {
        var singletons = new Dictionary<string, SingletonHealth>
        {
            ["CurriculumGraphActor"] = BuildSingletonHealth(
                "CurriculumGraphActor", _curriculumGraphPid),
            ["LlmGatewayActor"] = BuildSingletonHealth(
                "LlmGatewayActor", _llmGatewayPid),
            ["StudentActorManager"] = BuildSingletonHealth(
                "StudentActorManager", _studentActorManagerPid)
        };

        bool allHealthy = _isBooted && singletons.Values.All(s => s.IsAlive);
        long uptimeSeconds = _isBooted
            ? (long)(DateTimeOffset.UtcNow - _bootedAt).TotalSeconds
            : 0;

        context.Respond(new SystemHealthReport(
            AllHealthy: allHealthy,
            Singletons: singletons,
            BootedAt: _bootedAt,
            UptimeSeconds: uptimeSeconds));

        return Task.CompletedTask;
    }

    private static SingletonHealth BuildSingletonHealth(string name, PID? pid)
    {
        if (pid == null)
        {
            return new SingletonHealth(
                Name: name,
                IsAlive: false,
                Pid: null,
                Status: "not_spawned");
        }

        return new SingletonHealth(
            Name: name,
            IsAlive: true,
            Pid: pid,
            Status: "running");
    }
}
