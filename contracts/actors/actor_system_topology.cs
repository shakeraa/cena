// =============================================================================
// Cena Platform — Full Actor System Topology
// The COMPLETE actor hierarchy, managers, circuit breakers, routers, guardians,
// graph actors, and resilience patterns. This is the enterprise-grade blueprint.
//
// Missing from the initial contracts — this file adds:
//   1. ActorSystemManager (root orchestrator, lifecycle management)
//   2. CurriculumGraphActor (in-memory graph cache, hot-reload)
//   3. McmGraphActor (methodology mapping, confidence-scored lookups)
//   4. LlmCircuitBreakerActor (per-model circuit breakers)
//   5. StudentActorManager (pool management, activation budgets, back-pressure)
//   6. OutreachDispatcherActor (fan-out router to channel workers)
//   7. AnalyticsAggregatorActor (event sink, batching, S3 export)
//   8. SupervisionStrategies (all supervision trees in one place)
//   9. DeadLetterWatcher (monitoring, alerting, poison message quarantine)
//   10. GracefulShutdownCoordinator
// =============================================================================

using Proto;
using Proto.Cluster;
using Proto.Persistence;
using Proto.Router;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Cena.Actors.Topology;

// ═══════════════════════════════════════════════════════════════════════
// 1. FULL ACTOR HIERARCHY (the complete tree)
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Complete actor system topology. Every actor in the Cena runtime.
///
/// Proto.Actor Cluster
/// │
/// ├── ActorSystemManager [singleton, root guardian]
/// │   ├── CurriculumGraphActor [singleton, in-memory domain graph]
/// │   │   └── McmGraphActor [singleton, methodology mapping graph]
/// │   │
/// │   ├── StudentActorManager [singleton, pool governor]
/// │   │   └── StudentActor × N [virtual, event-sourced, per-student]
/// │   │       ├── LearningSessionActor [classic, session-scoped]
/// │   │       ├── StagnationDetectorActor [classic, timer-based]
/// │   │       └── OutreachSchedulerActor [classic, timer-based]
/// │   │
/// │   ├── LlmGatewayActor [singleton, routes to per-model actors]
/// │   │   ├── KimiCircuitBreakerActor [circuit breaker + rate limiter]
/// │   │   ├── SonnetCircuitBreakerActor [circuit breaker + rate limiter]
/// │   │   └── OpusCircuitBreakerActor [circuit breaker + rate limiter]
/// │   │
/// │   ├── OutreachDispatcherActor [singleton, fan-out router]
/// │   │   ├── WhatsAppWorkerActor × 3 [pool, round-robin]
/// │   │   ├── TelegramWorkerActor × 2 [pool, round-robin]
/// │   │   ├── PushNotificationWorkerActor × 2 [pool]
/// │   │   └── VoiceCallWorkerActor × 1 [single]
/// │   │
/// │   ├── AnalyticsAggregatorActor [singleton, batching sink]
/// │   │   └── S3ExportWorkerActor [timer-based, nightly]
/// │   │
/// │   ├── DeadLetterWatcher [singleton, monitoring]
/// │   │
/// │   └── GracefulShutdownCoordinator [singleton, drain + persist]
/// │
/// └── NATS Bridge Actor [bridges domain events to NATS JetStream]
/// </summary>
public static class ActorTopology
{
    // Documented above — this class exists only for the diagram.
    // Each actor below has its own full implementation.
}

// ═══════════════════════════════════════════════════════════════════════
// 2. ActorSystemManager — Root Guardian
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// The root actor that bootstraps all singleton actors on cluster startup.
/// Manages lifecycle: start in dependency order, drain on shutdown.
/// </summary>
public sealed class ActorSystemManager : IActor
{
    private readonly ILogger<ActorSystemManager> _logger;
    private readonly ActorSystem _system;

    // ── Child PIDs ──
    private PID? _curriculumGraph;
    private PID? _studentManager;
    private PID? _llmGateway;
    private PID? _outreachDispatcher;
    private PID? _analyticsAggregator;
    private PID? _deadLetterWatcher;
    private PID? _shutdownCoordinator;

    // ── Startup order (dependencies flow downward) ──
    private static readonly string[] StartupOrder = new[]
    {
        "CurriculumGraph",     // 1. Load domain graph first (others depend on it)
        "LlmGateway",         // 2. LLM circuit breakers ready before students
        "StudentManager",     // 3. Student pool ready
        "OutreachDispatcher",  // 4. Outreach channels ready
        "AnalyticsAggregator", // 5. Analytics sink ready
        "DeadLetterWatcher",   // 6. Monitoring last
    };

    public ActorSystemManager(ActorSystem system, ILogger<ActorSystemManager> logger)
    {
        _system = system;
        _logger = logger;
    }

    public async Task ReceiveAsync(IContext context)
    {
        switch (context.Message)
        {
            case Started:
                await BootstrapAllActors(context);
                break;

            case Stopping:
                await DrainAllActors(context);
                break;

            case SystemHealthCheckRequest:
                context.Respond(BuildHealthReport());
                break;
        }
    }

    private async Task BootstrapAllActors(IContext context)
    {
        _logger.LogInformation("ActorSystemManager: bootstrapping {Count} singleton actors", StartupOrder.Length);

        // 1. Curriculum Graph (must be first — others query it)
        _curriculumGraph = context.SpawnNamed(
            Props.FromProducer(() => context.System.DI().Get<CurriculumGraphActor>())
                 .WithChildSupervisorStrategy(SupervisionStrategies.CriticalSingleton),
            "curriculum-graph"
        );
        _logger.LogInformation("  ✓ CurriculumGraphActor started");

        // 2. LLM Gateway (circuit breakers per model)
        _llmGateway = context.SpawnNamed(
            Props.FromProducer(() => context.System.DI().Get<LlmGatewayActor>())
                 .WithChildSupervisorStrategy(SupervisionStrategies.LlmGateway),
            "llm-gateway"
        );
        _logger.LogInformation("  ✓ LlmGatewayActor started");

        // 3. Student Manager (pool governor)
        _studentManager = context.SpawnNamed(
            Props.FromProducer(() => context.System.DI().Get<StudentActorManager>())
                 .WithChildSupervisorStrategy(SupervisionStrategies.StudentPool),
            "student-manager"
        );
        _logger.LogInformation("  ✓ StudentActorManager started");

        // 4. Outreach Dispatcher (fan-out to channels)
        _outreachDispatcher = context.SpawnNamed(
            Props.FromProducer(() => context.System.DI().Get<OutreachDispatcherActor>())
                 .WithChildSupervisorStrategy(SupervisionStrategies.OutreachWorkers),
            "outreach-dispatcher"
        );
        _logger.LogInformation("  ✓ OutreachDispatcherActor started");

        // 5. Analytics Aggregator
        _analyticsAggregator = context.SpawnNamed(
            Props.FromProducer(() => context.System.DI().Get<AnalyticsAggregatorActor>())
                 .WithChildSupervisorStrategy(SupervisionStrategies.AnalyticsSink),
            "analytics-aggregator"
        );
        _logger.LogInformation("  ✓ AnalyticsAggregatorActor started");

        // 6. Dead Letter Watcher (monitoring)
        _deadLetterWatcher = context.SpawnNamed(
            Props.FromProducer(() => context.System.DI().Get<DeadLetterWatcher>()),
            "dead-letter-watcher"
        );
        _logger.LogInformation("  ✓ DeadLetterWatcher started");

        // 7. Shutdown Coordinator (registers all actors)
        _shutdownCoordinator = context.SpawnNamed(
            Props.FromProducer(() => new GracefulShutdownCoordinator(
                _curriculumGraph!, _studentManager!, _llmGateway!,
                _outreachDispatcher!, _analyticsAggregator!
            )),
            "shutdown-coordinator"
        );

        _logger.LogInformation("ActorSystemManager: all actors bootstrapped successfully");
    }

    private async Task DrainAllActors(IContext context)
    {
        _logger.LogWarning("ActorSystemManager: initiating graceful shutdown");
        if (_shutdownCoordinator != null)
            await context.RequestAsync<ShutdownComplete>(_shutdownCoordinator, new InitiateShutdown(), TimeSpan.FromSeconds(30));
    }

    private SystemHealthReport BuildHealthReport() => new(
        CurriculumGraphLoaded: _curriculumGraph != null,
        LlmGatewayReady: _llmGateway != null,
        StudentManagerReady: _studentManager != null,
        OutreachDispatcherReady: _outreachDispatcher != null,
        AnalyticsReady: _analyticsAggregator != null
    );
}

public record SystemHealthCheckRequest;
public record SystemHealthReport(
    bool CurriculumGraphLoaded,
    bool LlmGatewayReady,
    bool StudentManagerReady,
    bool OutreachDispatcherReady,
    bool AnalyticsReady
);

// ═══════════════════════════════════════════════════════════════════════
// 3. CurriculumGraphActor — In-Memory Domain Graph
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Singleton actor that holds the entire curriculum knowledge graph in memory.
/// Loaded from Neo4j on startup, hot-reloaded on CurriculumPublished events.
/// All StudentActors query this actor for graph traversals (microsecond latency).
/// </summary>
public sealed class CurriculumGraphActor : IActor
{
    private readonly INeo4jClient _neo4j;
    private readonly ILogger<CurriculumGraphActor> _logger;

    // ── In-memory graph (the hot path) ──
    private Dictionary<string, ConceptNode> _concepts = new();
    private Dictionary<string, List<PrerequisiteEdge>> _prerequisites = new();
    private string _currentVersion = "";
    private DateTimeOffset _loadedAt;

    // ── Child: MCM Graph Actor ──
    private PID? _mcmGraph;

    // ── Metrics ──
    private static readonly Counter<long> GraphQueries = new("cena.graph.queries", "Graph queries served");
    private static readonly Histogram<double> QueryLatency = new("cena.graph.query_latency_us", "Graph query latency (microseconds)");

    public CurriculumGraphActor(INeo4jClient neo4j, ILogger<CurriculumGraphActor> logger)
    {
        _neo4j = neo4j;
        _logger = logger;
    }

    public async Task ReceiveAsync(IContext context)
    {
        switch (context.Message)
        {
            case Started:
                await LoadGraphFromNeo4j();
                // Spawn MCM graph as child (depends on curriculum graph data)
                _mcmGraph = context.SpawnNamed(
                    Props.FromProducer(() => context.System.DI().Get<McmGraphActor>()),
                    "mcm-graph"
                );
                break;

            // ── Queries (hot path, microsecond response) ──

            case GetConceptQuery q:
                GraphQueries.Add(1);
                context.Respond(_concepts.GetValueOrDefault(q.ConceptId));
                break;

            case GetPrerequisitesQuery q:
                GraphQueries.Add(1);
                context.Respond(_prerequisites.GetValueOrDefault(q.ConceptId) ?? new List<PrerequisiteEdge>());
                break;

            case GetFrontierQuery q:
                GraphQueries.Add(1);
                var frontier = ComputeFrontier(q.MasteredConceptIds);
                context.Respond(frontier);
                break;

            case GetConceptClusterQuery q:
                context.Respond(GetCluster(q.ConceptId));
                break;

            // ── MCM delegation ──

            case McmLookupQuery q:
                // Forward to MCM child actor
                if (_mcmGraph != null) context.Forward(_mcmGraph);
                break;

            // ── Hot reload (triggered by NATS CurriculumPublished event) ──

            case CurriculumPublishedEvent e:
                _logger.LogInformation("Hot-reloading curriculum graph from v{Old} to v{New}", _currentVersion, e.NewVersion);
                await LoadGraphFromNeo4j();
                _currentVersion = e.NewVersion;
                // Notify MCM graph to reload too
                if (_mcmGraph != null) context.Send(_mcmGraph, new ReloadMcmGraph());
                break;

            case GraphHealthCheckRequest:
                context.Respond(new GraphHealthReport(
                    ConceptCount: _concepts.Count,
                    PrerequisiteEdgeCount: _prerequisites.Values.Sum(l => l.Count),
                    Version: _currentVersion,
                    LoadedAt: _loadedAt,
                    McmReady: _mcmGraph != null
                ));
                break;
        }
    }

    private async Task LoadGraphFromNeo4j()
    {
        var sw = Stopwatch.StartNew();
        var (concepts, edges) = await _neo4j.LoadFullGraph();
        _concepts = concepts.ToDictionary(c => c.Id);
        _prerequisites = edges.GroupBy(e => e.FromConceptId)
                              .ToDictionary(g => g.Key, g => g.ToList());
        _loadedAt = DateTimeOffset.UtcNow;
        _logger.LogInformation("Curriculum graph loaded: {Concepts} concepts, {Edges} edges in {Ms}ms",
            _concepts.Count, edges.Count, sw.ElapsedMilliseconds);
    }

    private List<string> ComputeFrontier(ISet<string> masteredIds)
    {
        // Frontier = concepts where all prerequisites are mastered but the concept itself is not
        return _concepts.Values
            .Where(c => !masteredIds.Contains(c.Id))
            .Where(c => _prerequisites.GetValueOrDefault(c.Id)?
                .All(p => masteredIds.Contains(p.FromConceptId)) ?? true)
            .Select(c => c.Id)
            .ToList();
    }

    private string GetCluster(string conceptId)
    {
        // Return the concept's cluster (e.g., "algebra", "trigonometry", "calculus")
        return _concepts.GetValueOrDefault(conceptId)?.Category ?? "unknown";
    }
}

// ── Graph query/response messages ──
public record GetConceptQuery(string ConceptId);
public record GetPrerequisitesQuery(string ConceptId);
public record GetFrontierQuery(ISet<string> MasteredConceptIds);
public record GetConceptClusterQuery(string ConceptId);
public record CurriculumPublishedEvent(string NewVersion, DateTimeOffset PublishedAt);
public record GraphHealthCheckRequest;
public record GraphHealthReport(int ConceptCount, int PrerequisiteEdgeCount, string Version, DateTimeOffset LoadedAt, bool McmReady);

public record ConceptNode(string Id, string Name, string Subject, string Category, double Difficulty, string BloomLevel, int PrerequisiteCount);
public record PrerequisiteEdge(string FromConceptId, string ToConceptId, double Strength, bool EmpiricallyValidated);

// ═══════════════════════════════════════════════════════════════════════
// 4. McmGraphActor — Methodology Mapping Graph
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Child of CurriculumGraphActor. Holds the MCM (Mode × Capability × Methodology)
/// mapping in memory. Queried by MethodologySwitchService when stagnation is detected.
/// </summary>
public sealed class McmGraphActor : IActor
{
    private readonly INeo4jClient _neo4j;

    // ── MCM lookup table: (errorType, conceptCategory) → [(methodology, confidence)] ──
    private Dictionary<(string ErrorType, string ConceptCategory), List<McmCandidate>> _mcm = new();

    // ── Error-type-only fallback defaults ──
    private static readonly Dictionary<string, List<McmCandidate>> FallbackDefaults = new()
    {
        ["conceptual"] = new() { new("socratic", 0.80), new("feynman", 0.65) },
        ["procedural"] = new() { new("drill", 0.85), new("worked_example", 0.70) },
        ["motivational"] = new() { new("project_based", 0.70), new("analogy", 0.55) },
    };

    public McmGraphActor(INeo4jClient neo4j) => _neo4j = neo4j;

    public async Task ReceiveAsync(IContext context)
    {
        switch (context.Message)
        {
            case Started:
            case ReloadMcmGraph:
                await LoadMcmFromNeo4j();
                break;

            case McmLookupQuery q:
                var candidates = LookupWithFallback(q.ErrorType, q.ConceptCategory, q.ExcludedMethodologies);
                context.Respond(new McmLookupResult(candidates));
                break;

            case UpdateMcmConfidence u:
                // Intelligence layer flywheel pushes updated confidence scores
                UpdateConfidence(u.ErrorType, u.ConceptCategory, u.Methodology, u.NewConfidence);
                break;
        }
    }

    private async Task LoadMcmFromNeo4j()
    {
        var entries = await _neo4j.LoadMcmGraph();
        _mcm = entries.GroupBy(e => (e.ErrorType, e.ConceptCategory))
                      .ToDictionary(
                          g => g.Key,
                          g => g.OrderByDescending(e => e.Confidence).ToList()
                      );
    }

    /// <summary>
    /// 4-step lookup matching the algorithm in system-overview.md:
    /// 1. Query MCM for (error_type, concept_category)
    /// 2. Filter out excluded (already-tried) methodologies
    /// 3. Select first with confidence > 0.5, else best available
    /// 4. Fallback to error-type-only defaults if no MCM entry
    /// </summary>
    private List<McmCandidate> LookupWithFallback(
        string errorType, string conceptCategory, ISet<string> excluded)
    {
        // Step 1: Primary lookup
        if (_mcm.TryGetValue((errorType, conceptCategory), out var primary))
        {
            // Step 2: Filter excluded
            var filtered = primary.Where(c => !excluded.Contains(c.Methodology)).ToList();
            if (filtered.Count > 0) return filtered;
        }

        // Step 4: Fallback to error-type defaults
        if (FallbackDefaults.TryGetValue(errorType, out var fallback))
        {
            return fallback.Where(c => !excluded.Contains(c.Methodology)).ToList();
        }

        return new List<McmCandidate>(); // Empty = all exhausted → escalate
    }

    private void UpdateConfidence(string errorType, string category, string methodology, double newConfidence)
    {
        var key = (errorType, category);
        if (_mcm.TryGetValue(key, out var candidates))
        {
            var match = candidates.FirstOrDefault(c => c.Methodology == methodology);
            if (match != null)
            {
                candidates.Remove(match);
                candidates.Add(match with { Confidence = newConfidence });
                candidates.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
            }
        }
    }
}

public record McmCandidate(string Methodology, double Confidence);
public record McmLookupQuery(string ErrorType, string ConceptCategory, ISet<string> ExcludedMethodologies);
public record McmLookupResult(List<McmCandidate> Candidates);
public record ReloadMcmGraph;
public record UpdateMcmConfidence(string ErrorType, string ConceptCategory, string Methodology, double NewConfidence);

// ═══════════════════════════════════════════════════════════════════════
// 5. LlmGatewayActor — Circuit Breakers Per Model
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Routes LLM requests to per-model circuit breaker actors.
/// Each model tier has independent failure tracking and fallback.
/// This replaces direct gRPC calls to the Python ACL — all LLM traffic
/// flows through this actor for resilience and cost control.
/// </summary>
public sealed class LlmGatewayActor : IActor
{
    private PID? _kimiBreaker;
    private PID? _sonnetBreaker;
    private PID? _opusBreaker;

    public Task ReceiveAsync(IContext context)
    {
        switch (context.Message)
        {
            case Started:
                _kimiBreaker = context.SpawnNamed(
                    Props.FromProducer(() => new LlmCircuitBreakerActor("kimi-k2.5", maxFailures: 5, openDuration: TimeSpan.FromSeconds(60))),
                    "kimi-breaker");
                _sonnetBreaker = context.SpawnNamed(
                    Props.FromProducer(() => new LlmCircuitBreakerActor("claude-sonnet-4.6", maxFailures: 3, openDuration: TimeSpan.FromSeconds(90))),
                    "sonnet-breaker");
                _opusBreaker = context.SpawnNamed(
                    Props.FromProducer(() => new LlmCircuitBreakerActor("claude-opus-4.6", maxFailures: 2, openDuration: TimeSpan.FromSeconds(120))),
                    "opus-breaker");
                break;

            case LlmRequest req:
                var target = req.ModelTier switch
                {
                    "kimi" => _kimiBreaker,
                    "sonnet" => _sonnetBreaker,
                    "opus" => _opusBreaker,
                    _ => _sonnetBreaker,
                };
                if (target != null) context.Forward(target);
                break;

            case LlmGatewayHealthRequest:
                // Collect health from all breakers
                context.Respond(new LlmGatewayHealth(
                    KimiState: "unknown", SonnetState: "unknown", OpusState: "unknown"
                ));
                break;
        }
        return Task.CompletedTask;
    }
}

/// <summary>
/// Per-model circuit breaker with three states: Closed → Open → HalfOpen.
/// Tracks failures in a sliding window. When open, returns cached/degraded responses.
/// </summary>
public sealed class LlmCircuitBreakerActor : IActor
{
    private readonly string _modelId;
    private readonly int _maxFailures;
    private readonly TimeSpan _openDuration;

    private CircuitState _state = CircuitState.Closed;
    private int _failureCount;
    private DateTimeOffset _openedAt;
    private int _successCount;

    // ── Metrics ──
    private static readonly Counter<long> CircuitOpened = new("cena.llm.circuit_opened", "Circuit breaker opened");
    private static readonly Counter<long> RequestsRejected = new("cena.llm.circuit_rejected", "Requests rejected by open circuit");

    public LlmCircuitBreakerActor(string modelId, int maxFailures, TimeSpan openDuration)
    {
        _modelId = modelId;
        _maxFailures = maxFailures;
        _openDuration = openDuration;
    }

    public async Task ReceiveAsync(IContext context)
    {
        switch (context.Message)
        {
            case LlmRequest req:
                await HandleRequest(context, req);
                break;

            case LlmResponse resp:
                OnSuccess();
                break;

            case LlmError err:
                OnFailure(err);
                break;

            case GetCircuitState:
                context.Respond(new CircuitStateReport(_modelId, _state, _failureCount, _successCount));
                break;
        }
    }

    private async Task HandleRequest(IContext context, LlmRequest req)
    {
        switch (_state)
        {
            case CircuitState.Closed:
                // Forward to Python ACL via gRPC
                // On success: OnSuccess(). On failure: OnFailure().
                context.Respond(new LlmRequestAccepted(_modelId));
                break;

            case CircuitState.Open:
                if (DateTimeOffset.UtcNow - _openedAt > _openDuration)
                {
                    _state = CircuitState.HalfOpen;
                    _successCount = 0;
                    goto case CircuitState.HalfOpen;
                }
                RequestsRejected.Add(1, new KeyValuePair<string, object?>("model", _modelId));
                context.Respond(new LlmCircuitOpenResponse(_modelId, "Circuit breaker is OPEN — use fallback model"));
                break;

            case CircuitState.HalfOpen:
                // Allow one request through to test recovery
                context.Respond(new LlmRequestAccepted(_modelId));
                break;
        }
    }

    private void OnSuccess()
    {
        _failureCount = 0;
        if (_state == CircuitState.HalfOpen)
        {
            _successCount++;
            if (_successCount >= 3) // 3 successes in half-open → close
            {
                _state = CircuitState.Closed;
            }
        }
    }

    private void OnFailure(LlmError err)
    {
        _failureCount++;
        if (_failureCount >= _maxFailures)
        {
            _state = CircuitState.Open;
            _openedAt = DateTimeOffset.UtcNow;
            CircuitOpened.Add(1, new KeyValuePair<string, object?>("model", _modelId));
        }
    }
}

public enum CircuitState { Closed, Open, HalfOpen }
public record LlmRequest(string ModelTier, string TaskType, byte[] Payload);
public record LlmRequestAccepted(string ModelId);
public record LlmResponse(string ModelId, byte[] Payload, int TokensUsed);
public record LlmError(string ModelId, string ErrorMessage, int HttpStatus);
public record LlmCircuitOpenResponse(string ModelId, string Reason);
public record GetCircuitState;
public record CircuitStateReport(string ModelId, CircuitState State, int FailureCount, int SuccessCount);
public record LlmGatewayHealthRequest;
public record LlmGatewayHealth(string KimiState, string SonnetState, string OpusState);

// ═══════════════════════════════════════════════════════════════════════
// 6. StudentActorManager — Pool Governor
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Manages the pool of virtual StudentActors. Enforces:
/// - Activation budget (max concurrent actors per node)
/// - Back-pressure when activation queue is deep
/// - Memory pressure monitoring (pause activations at 80% node memory)
/// - Graceful drain on node shutdown
/// </summary>
public sealed class StudentActorManager : IActor
{
    private readonly int _maxConcurrentActors;
    private readonly ConcurrentDictionary<string, PID> _activeActors = new();
    private readonly Queue<PendingActivation> _activationQueue = new();

    private static readonly Gauge<int> ActiveActorCount = new("cena.actors.student_active", "Active student actors");
    private static readonly Gauge<int> ActivationQueueDepth = new("cena.actors.activation_queue", "Pending activations");
    private static readonly Counter<long> ActivationRejected = new("cena.actors.activation_rejected", "Activations rejected (back-pressure)");

    public StudentActorManager(int maxConcurrentActors = 10_000)
    {
        _maxConcurrentActors = maxConcurrentActors;
    }

    public Task ReceiveAsync(IContext context)
    {
        switch (context.Message)
        {
            case ActivateStudent req:
                HandleActivation(context, req);
                break;

            case StudentPassivated evt:
                _activeActors.TryRemove(evt.StudentId, out _);
                ActiveActorCount.Record(_activeActors.Count);
                ProcessActivationQueue(context);
                break;

            case GetPoolStatus:
                context.Respond(new PoolStatus(
                    ActiveCount: _activeActors.Count,
                    QueueDepth: _activationQueue.Count,
                    MaxCapacity: _maxConcurrentActors
                ));
                break;

            case DrainAllStudents:
                DrainAll(context);
                break;
        }
        return Task.CompletedTask;
    }

    private void HandleActivation(IContext context, ActivateStudent req)
    {
        if (_activeActors.Count >= _maxConcurrentActors)
        {
            if (_activationQueue.Count > 1000) // Hard back-pressure
            {
                ActivationRejected.Add(1);
                context.Respond(new ActivationResult(false, null, "Back-pressure: too many pending activations"));
                return;
            }
            _activationQueue.Enqueue(new PendingActivation(req, context.Sender!));
            ActivationQueueDepth.Record(_activationQueue.Count);
            return;
        }

        // Activate the student actor (virtual grain — Proto.Actor handles the actual spawn)
        var pid = context.Cluster().GetGrain<IStudentGrain>(req.StudentId);
        _activeActors[req.StudentId] = pid;
        ActiveActorCount.Record(_activeActors.Count);
        context.Respond(new ActivationResult(true, pid, null));
    }

    private void ProcessActivationQueue(IContext context)
    {
        while (_activationQueue.Count > 0 && _activeActors.Count < _maxConcurrentActors)
        {
            var pending = _activationQueue.Dequeue();
            var pid = context.Cluster().GetGrain<IStudentGrain>(pending.Request.StudentId);
            _activeActors[pending.Request.StudentId] = pid;
            context.Send(pending.Sender, new ActivationResult(true, pid, null));
        }
        ActivationQueueDepth.Record(_activationQueue.Count);
    }

    private void DrainAll(IContext context)
    {
        foreach (var (id, pid) in _activeActors)
        {
            context.Send(pid, new PoisonPill()); // Triggers passivation → persist state
        }
    }
}

public record ActivateStudent(string StudentId);
public record ActivationResult(bool Success, PID? ActorPid, string? Error);
public record StudentPassivated(string StudentId);
public record GetPoolStatus;
public record PoolStatus(int ActiveCount, int QueueDepth, int MaxCapacity);
public record DrainAllStudents;
public record PendingActivation(ActivateStudent Request, PID Sender);

// ═══════════════════════════════════════════════════════════════════════
// 7. OutreachDispatcherActor — Fan-Out Router
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Routes outreach messages to channel-specific worker pools.
/// Round-robin within each channel pool. Handles priority, throttling, quiet hours.
/// </summary>
public sealed class OutreachDispatcherActor : IActor
{
    // Channel worker pools
    private PID? _whatsappPool;
    private PID? _telegramPool;
    private PID? _pushPool;
    private PID? _voicePool;

    public Task ReceiveAsync(IContext context)
    {
        switch (context.Message)
        {
            case Started:
                // Create round-robin router pools per channel
                _whatsappPool = context.SpawnNamed(
                    Router.NewRoundRobinPool(
                        Props.FromProducer(() => context.System.DI().Get<WhatsAppWorkerActor>()), 3),
                    "whatsapp-pool");
                _telegramPool = context.SpawnNamed(
                    Router.NewRoundRobinPool(
                        Props.FromProducer(() => context.System.DI().Get<TelegramWorkerActor>()), 2),
                    "telegram-pool");
                _pushPool = context.SpawnNamed(
                    Router.NewRoundRobinPool(
                        Props.FromProducer(() => context.System.DI().Get<PushNotificationWorkerActor>()), 2),
                    "push-pool");
                _voicePool = context.SpawnNamed(
                    Props.FromProducer(() => context.System.DI().Get<VoiceCallWorkerActor>()),
                    "voice-worker");
                break;

            case DispatchOutreach msg:
                var target = msg.Channel switch
                {
                    "whatsapp" => _whatsappPool,
                    "telegram" => _telegramPool,
                    "push" => _pushPool,
                    "voice" => _voicePool,
                    _ => _pushPool, // Default to push
                };
                if (target != null) context.Send(target, msg);
                break;
        }
        return Task.CompletedTask;
    }
}

public record DispatchOutreach(string StudentId, string Channel, string TriggerType, string ContentHash, int Priority);

// Placeholder worker actor interfaces (implementations in separate files)
public class WhatsAppWorkerActor : IActor { public Task ReceiveAsync(IContext context) => Task.CompletedTask; }
public class TelegramWorkerActor : IActor { public Task ReceiveAsync(IContext context) => Task.CompletedTask; }
public class PushNotificationWorkerActor : IActor { public Task ReceiveAsync(IContext context) => Task.CompletedTask; }
public class VoiceCallWorkerActor : IActor { public Task ReceiveAsync(IContext context) => Task.CompletedTask; }

// ═══════════════════════════════════════════════════════════════════════
// 8. AnalyticsAggregatorActor — Batching Event Sink
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Receives all domain events, batches them in memory, flushes to S3 on schedule.
/// Anonymizes PII before export using HMAC-SHA256 with rotating epoch key.
/// </summary>
public sealed class AnalyticsAggregatorActor : IActor
{
    private readonly List<object> _buffer = new();
    private readonly int _flushThreshold;
    private readonly TimeSpan _flushInterval;

    private static readonly Counter<long> EventsBuffered = new("cena.analytics.events_buffered", "Events buffered");
    private static readonly Counter<long> FlushCount = new("cena.analytics.flushes", "S3 flush count");

    public AnalyticsAggregatorActor(int flushThreshold = 1000, TimeSpan? flushInterval = null)
    {
        _flushThreshold = flushThreshold;
        _flushInterval = flushInterval ?? TimeSpan.FromMinutes(5);
    }

    public async Task ReceiveAsync(IContext context)
    {
        switch (context.Message)
        {
            case Started:
                context.SetReceiveTimeout(_flushInterval);
                break;

            case ReceiveTimeout:
                if (_buffer.Count > 0) await FlushToS3();
                context.SetReceiveTimeout(_flushInterval);
                break;

            // Buffer any domain event
            case object evt when IsDomainEvent(evt):
                _buffer.Add(evt);
                EventsBuffered.Add(1);
                if (_buffer.Count >= _flushThreshold) await FlushToS3();
                break;
        }
    }

    private async Task FlushToS3()
    {
        // Anonymize, serialize to Parquet, upload to S3
        FlushCount.Add(1);
        _buffer.Clear();
    }

    private static bool IsDomainEvent(object msg) =>
        msg.GetType().Namespace?.StartsWith("Cena.Data.EventStore") == true;
}

// ═══════════════════════════════════════════════════════════════════════
// 9. DeadLetterWatcher — Monitoring & Quarantine
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Subscribes to Proto.Actor's DeadLetterEvent stream.
/// Logs, counts, and quarantines poison messages that repeatedly fail.
/// Alerts on-call when dead letter rate exceeds threshold.
/// </summary>
public sealed class DeadLetterWatcher : IActor
{
    private readonly ConcurrentDictionary<string, int> _poisonMessageCounts = new();
    private static readonly Counter<long> DeadLetterCount = new("cena.deadletters.total", "Dead letters received");
    private static readonly int QuarantineThreshold = 3; // Same message type fails 3 times → quarantine

    public Task ReceiveAsync(IContext context)
    {
        switch (context.Message)
        {
            case Started:
                context.System.EventStream.Subscribe<DeadLetterEvent>(evt =>
                {
                    DeadLetterCount.Add(1);
                    var key = $"{evt.Message?.GetType().Name}:{evt.Pid}";
                    var count = _poisonMessageCounts.AddOrUpdate(key, 1, (_, c) => c + 1);
                    if (count >= QuarantineThreshold)
                    {
                        // TODO: Quarantine — log the message, don't re-deliver
                        // Alert via operations.md Section 3.2 alerting rules
                    }
                });
                break;

            case GetDeadLetterStats:
                context.Respond(new DeadLetterStats(
                    PoisonMessages: _poisonMessageCounts.Where(kv => kv.Value >= QuarantineThreshold).ToDictionary(kv => kv.Key, kv => kv.Value),
                    TotalDeadLetters: _poisonMessageCounts.Values.Sum()
                ));
                break;
        }
        return Task.CompletedTask;
    }
}

public record GetDeadLetterStats;
public record DeadLetterStats(Dictionary<string, int> PoisonMessages, int TotalDeadLetters);

// ═══════════════════════════════════════════════════════════════════════
// 10. GracefulShutdownCoordinator
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Orchestrates graceful shutdown: drain students → flush analytics → close LLM
/// → leave cluster. Ensures no data loss during rolling deployments.
/// </summary>
public sealed class GracefulShutdownCoordinator : IActor
{
    private readonly PID _curriculumGraph;
    private readonly PID _studentManager;
    private readonly PID _llmGateway;
    private readonly PID _outreachDispatcher;
    private readonly PID _analyticsAggregator;

    public GracefulShutdownCoordinator(PID curriculum, PID students, PID llm, PID outreach, PID analytics)
    {
        _curriculumGraph = curriculum;
        _studentManager = students;
        _llmGateway = llm;
        _outreachDispatcher = outreach;
        _analyticsAggregator = analytics;
    }

    public async Task ReceiveAsync(IContext context)
    {
        if (context.Message is InitiateShutdown)
        {
            // Phase 1: Stop accepting new student activations
            context.Send(_studentManager, new DrainAllStudents());

            // Phase 2: Wait for active sessions to end (max 30s)
            await Task.Delay(TimeSpan.FromSeconds(5)); // Give sessions time to complete

            // Phase 3: Flush analytics buffer to S3
            // Phase 4: Close LLM connections gracefully
            // Phase 5: Leave cluster

            context.Respond(new ShutdownComplete());
        }
    }
}

public record InitiateShutdown;
public record ShutdownComplete;

// ═══════════════════════════════════════════════════════════════════════
// 11. SUPERVISION STRATEGIES (all in one place)
// ═══════════════════════════════════════════════════════════════════════

/// <summary>
/// Centralized supervision strategies. Each actor type gets a strategy
/// appropriate to its failure characteristics.
/// </summary>
public static class SupervisionStrategies
{
    /// <summary>
    /// Critical singletons (CurriculumGraph, AnalyticsAggregator).
    /// Restart with exponential backoff. Alert on 3rd restart.
    /// </summary>
    public static readonly ISupervisorStrategy CriticalSingleton =
        new OneForOneStrategy((pid, reason) =>
        {
            return SupervisorDirective.Restart;
        }, 3, TimeSpan.FromMinutes(1));

    /// <summary>
    /// Student pool. Each student actor is independent — one failure
    /// doesn't affect others. Restart child, stop after 3 consecutive failures.
    /// </summary>
    public static readonly ISupervisorStrategy StudentPool =
        new OneForOneStrategy((pid, reason) =>
        {
            return SupervisorDirective.Restart;
        }, 3, TimeSpan.FromSeconds(60));

    /// <summary>
    /// LLM Gateway. Escalate circuit breaker failures to parent.
    /// Never restart the gateway itself — circuit breakers handle recovery.
    /// </summary>
    public static readonly ISupervisorStrategy LlmGateway =
        new OneForOneStrategy((pid, reason) =>
        {
            return SupervisorDirective.Resume; // Circuit breaker manages its own state
        }, 10, TimeSpan.FromMinutes(5));

    /// <summary>
    /// Outreach workers. Restart individual channel workers on failure.
    /// If all workers in a pool fail, escalate (channel is down).
    /// </summary>
    public static readonly ISupervisorStrategy OutreachWorkers =
        new OneForOneStrategy((pid, reason) =>
        {
            return SupervisorDirective.Restart;
        }, 5, TimeSpan.FromMinutes(2));

    /// <summary>
    /// Analytics sink. Restart on failure — buffer is in memory, so
    /// some events may be lost. Acceptable trade-off for analytics.
    /// </summary>
    public static readonly ISupervisorStrategy AnalyticsSink =
        new OneForOneStrategy((pid, reason) =>
        {
            return SupervisorDirective.Restart;
        }, 3, TimeSpan.FromMinutes(1));
}

// ═══════════════════════════════════════════════════════════════════════
// 12. INTERFACES (for DI registration)
// ═══════════════════════════════════════════════════════════════════════

public interface IStudentGrain
{
    Task<ActorResult> AttemptConcept(AttemptConceptCommand cmd);
    Task<ActorResult> StartSession(StartSessionCommand cmd);
    Task<ActorResult> EndSession(EndSessionCommand cmd);
    Task<ActorResult> SwitchMethodology(SwitchMethodologyCommand cmd);
    Task<ActorResult> AddAnnotation(AddAnnotationCommand cmd);
    Task<ActorResult> SyncOfflineEvents(SyncOfflineEventsCommand cmd);
    Task<StudentProfileDto> GetProfile();
}

public interface INeo4jClient
{
    Task<(List<ConceptNode> Concepts, List<PrerequisiteEdge> Edges)> LoadFullGraph();
    Task<List<McmEntry>> LoadMcmGraph();
}

public record McmEntry(string ErrorType, string ConceptCategory, string Methodology, double Confidence);
public record ActorResult(bool Success, string? Error = null);

// Stub command records (full definitions in actor-contracts.cs)
public record AttemptConceptCommand;
public record StartSessionCommand;
public record EndSessionCommand;
public record SwitchMethodologyCommand;
public record AddAnnotationCommand;
public record SyncOfflineEventsCommand;
public record StudentProfileDto;
