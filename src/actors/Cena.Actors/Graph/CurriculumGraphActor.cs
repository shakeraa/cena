// =============================================================================
// Cena Platform -- CurriculumGraphActor (Singleton, Knowledge Graph)
// Layer: Actor Model | Runtime: .NET 9 | Framework: Proto.Actor v1.x
//
// Singleton actor holding the full curriculum knowledge graph in memory.
// Loads from Neo4j on startup, hot-reloads on CurriculumPublished message.
// Spawns McmGraphActor as child for MCM lookups.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Proto;

namespace Cena.Actors.Graph;

// =============================================================================
// REPOSITORY INTERFACE -- injected, backed by Neo4j
// =============================================================================

/// <summary>
/// Abstraction over Neo4j for loading the curriculum knowledge graph.
/// Implementations handle Cypher queries, connection pooling, and retry logic.
/// </summary>
public interface INeo4jGraphRepository
{
    /// <summary>Load all concepts from the curriculum graph.</summary>
    Task<IReadOnlyList<ConceptNode>> LoadAllConceptsAsync(CancellationToken ct = default);

    /// <summary>Load all prerequisite edges between concepts.</summary>
    Task<IReadOnlyList<PrerequisiteEdge>> LoadAllEdgesAsync(CancellationToken ct = default);

    /// <summary>Load MCM (MethodologyConceptMap) entries for methodology recommendations.</summary>
    Task<IReadOnlyList<McmEntry>> LoadMcmEntriesAsync(CancellationToken ct = default);

    /// <summary>Get the current graph version identifier.</summary>
    Task<string> GetGraphVersionAsync(CancellationToken ct = default);
}

// =============================================================================
// GRAPH DATA TYPES
// =============================================================================

public sealed record ConceptNode(
    string ConceptId,
    string Name,
    string Category,
    string Subject,
    int DifficultyLevel,
    IReadOnlyList<string> Tags);

public sealed record PrerequisiteEdge(
    string FromConceptId,
    string ToConceptId,
    double Weight);

public sealed record McmEntry(
    string ErrorType,
    string ConceptCategory,
    string Methodology,
    double Confidence);

// =============================================================================
// IN-MEMORY GRAPH SNAPSHOT (immutable for atomic swaps)
// =============================================================================

/// <summary>
/// Immutable snapshot of the entire curriculum graph. Swapped atomically on reload.
/// Precomputed adjacency lists for O(1) lookups.
/// </summary>
public sealed class CurriculumGraphSnapshot
{
    public string Version { get; }
    public IReadOnlyDictionary<string, ConceptNode> Concepts { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<PrerequisiteEdge>> PrerequisitesByTarget { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<PrerequisiteEdge>> DependentsBySource { get; }
    public int EdgeCount { get; }

    public CurriculumGraphSnapshot(
        string version,
        IReadOnlyList<ConceptNode> concepts,
        IReadOnlyList<PrerequisiteEdge> edges)
    {
        Version = version;

        var conceptDict = new Dictionary<string, ConceptNode>(concepts.Count);
        foreach (var c in concepts)
            conceptDict[c.ConceptId] = c;
        Concepts = conceptDict;

        var byTarget = new Dictionary<string, List<PrerequisiteEdge>>();
        var bySource = new Dictionary<string, List<PrerequisiteEdge>>();

        foreach (var edge in edges)
        {
            if (!byTarget.TryGetValue(edge.ToConceptId, out var targetList))
            {
                targetList = new List<PrerequisiteEdge>();
                byTarget[edge.ToConceptId] = targetList;
            }
            targetList.Add(edge);

            if (!bySource.TryGetValue(edge.FromConceptId, out var sourceList))
            {
                sourceList = new List<PrerequisiteEdge>();
                bySource[edge.FromConceptId] = sourceList;
            }
            sourceList.Add(edge);
        }

        PrerequisitesByTarget = byTarget.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<PrerequisiteEdge>)kv.Value.AsReadOnly());
        DependentsBySource = bySource.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<PrerequisiteEdge>)kv.Value.AsReadOnly());
        EdgeCount = edges.Count;
    }
}

// =============================================================================
// MESSAGES
// =============================================================================

/// <summary>Query for a single concept by ID.</summary>
public sealed record GetConcept(string ConceptId);

/// <summary>Query for all prerequisites of a concept.</summary>
public sealed record GetPrerequisites(string ConceptId);

/// <summary>
/// Query for the learning frontier: concepts whose prerequisites are all mastered
/// but which are not yet mastered themselves.
/// </summary>
public sealed record GetFrontier(IReadOnlyDictionary<string, double> MasteryMap, double MasteryThreshold = 0.85);

/// <summary>
/// Query for concepts in the same cluster (category + subject).
/// </summary>
public sealed record GetConceptCluster(string Category, string Subject);

/// <summary>
/// Notification that a new curriculum version has been published.
/// Triggers hot-reload of the graph from Neo4j.
/// </summary>
public sealed record CurriculumPublished(string PublishedBy, string NewVersion);

/// <summary>Query for the graph's health metrics.</summary>
public sealed record GetGraphHealth;

// ---- Responses ----

public sealed record GetConceptResponse(bool Found, ConceptNode? Concept);

public sealed record GetPrerequisitesResponse(
    string ConceptId,
    IReadOnlyList<PrerequisiteEdge> Prerequisites);

public sealed record GetFrontierResponse(IReadOnlyList<ConceptNode> FrontierConcepts);

public sealed record GetConceptClusterResponse(
    string Category,
    string Subject,
    IReadOnlyList<ConceptNode> Concepts);

public sealed record GraphHealthResponse(
    int ConceptCount,
    int EdgeCount,
    string Version,
    DateTimeOffset LastLoadedAt,
    bool IsLoaded);

// =============================================================================
// ACTOR
// =============================================================================

/// <summary>
/// Singleton actor holding the full curriculum knowledge graph in memory.
/// Loads from Neo4j on startup. Hot-reloads on CurriculumPublished.
/// Spawns McmGraphActor as child for MCM lookups.
/// </summary>
public sealed class CurriculumGraphActor : IActor
{
    private readonly INeo4jGraphRepository _repository;
    private readonly ILogger<CurriculumGraphActor> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IMeterFactory _meterFactory;

    private CurriculumGraphSnapshot _graph = null!;
    private DateTimeOffset _lastLoadedAt;
    private PID? _mcmChild;

    // ── Telemetry (ACT-031: instance-based) ──
    private readonly ActivitySource _activitySource;
    private readonly Counter<long> _reloadCounter;
    private readonly Histogram<double> _loadDurationMs;

    public CurriculumGraphActor(
        INeo4jGraphRepository repository,
        ILogger<CurriculumGraphActor> logger,
        ILoggerFactory loggerFactory,
        IMeterFactory meterFactory)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _meterFactory = meterFactory;

        _activitySource = new ActivitySource("Cena.Actors.CurriculumGraph", "1.0.0");
        var meter = meterFactory.Create("Cena.Actors.CurriculumGraph", "1.0.0");
        _reloadCounter = meter.CreateCounter<long>("cena.graph.reloads_total", description: "Total graph reloads");
        _loadDurationMs = meter.CreateHistogram<double>("cena.graph.load_duration_ms", description: "Graph load duration in ms");
    }

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            Started              => OnStarted(context),
            Stopping             => OnStopping(context),
            GetConcept q         => HandleGetConcept(context, q),
            GetPrerequisites q   => HandleGetPrerequisites(context, q),
            GetFrontier q        => HandleGetFrontier(context, q),
            GetConceptCluster q  => HandleGetConceptCluster(context, q),
            CurriculumPublished m=> HandleCurriculumPublished(context, m),
            GetGraphHealth       => HandleGetGraphHealth(context),
            // Forward MCM messages to child
            McmLookup q          => ForwardToMcmChild(context, q),
            UpdateMcmConfidence q=> ForwardToMcmChild(context, q),
            _ => Task.CompletedTask
        };
    }

    // ── Lifecycle ──

    private async Task OnStarted(IContext context)
    {
        _logger.LogInformation("CurriculumGraphActor starting. Loading graph from Neo4j...");

        await LoadGraphFromRepository();

        // Spawn McmGraphActor as child
        var mcmLogger = _loggerFactory.CreateLogger<McmGraphActor>();
        var mcmProps = Props.FromProducer(() => new McmGraphActor(_repository, mcmLogger));
        _mcmChild = context.Spawn(mcmProps);

        _logger.LogInformation(
            "CurriculumGraphActor ready. Concepts={ConceptCount}, Edges={EdgeCount}, Version={Version}",
            _graph.Concepts.Count, _graph.EdgeCount, _graph.Version);
    }

    private Task OnStopping(IContext context)
    {
        _logger.LogInformation("CurriculumGraphActor stopping.");
        if (_mcmChild != null)
            context.Stop(_mcmChild);
        return Task.CompletedTask;
    }

    // ── Graph Loading ──

    private async Task LoadGraphFromRepository()
    {
        using var activity = _activitySource.StartActivity("CurriculumGraph.Load");
        var sw = Stopwatch.StartNew();

        try
        {
            var conceptsTask = _repository.LoadAllConceptsAsync();
            var edgesTask = _repository.LoadAllEdgesAsync();
            var versionTask = _repository.GetGraphVersionAsync();

            await Task.WhenAll(conceptsTask, edgesTask, versionTask);

            var newGraph = new CurriculumGraphSnapshot(
                versionTask.Result,
                conceptsTask.Result,
                edgesTask.Result);

            // Atomic swap: replace reference in single assignment
            _graph = newGraph;
            _lastLoadedAt = DateTimeOffset.UtcNow;

            sw.Stop();
            _loadDurationMs.Record(sw.ElapsedMilliseconds);
            _reloadCounter.Add(1);

            activity?.SetTag("graph.concepts", newGraph.Concepts.Count);
            activity?.SetTag("graph.edges", newGraph.EdgeCount);
            activity?.SetTag("graph.version", newGraph.Version);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load curriculum graph from Neo4j");

            // If this is the first load (startup), create an empty graph so the actor
            // remains functional and can retry on the next CurriculumPublished message
            _graph ??= new CurriculumGraphSnapshot(
                "empty",
                Array.Empty<ConceptNode>(),
                Array.Empty<PrerequisiteEdge>());
            _lastLoadedAt = DateTimeOffset.UtcNow;
        }
    }

    // ── Query Handlers ──

    private Task HandleGetConcept(IContext context, GetConcept q)
    {
        var found = _graph.Concepts.TryGetValue(q.ConceptId, out var concept);
        context.Respond(new GetConceptResponse(found, concept));
        return Task.CompletedTask;
    }

    private Task HandleGetPrerequisites(IContext context, GetPrerequisites q)
    {
        var prereqs = _graph.PrerequisitesByTarget.TryGetValue(q.ConceptId, out var edges)
            ? edges
            : (IReadOnlyList<PrerequisiteEdge>)Array.Empty<PrerequisiteEdge>();

        context.Respond(new GetPrerequisitesResponse(q.ConceptId, prereqs));
        return Task.CompletedTask;
    }

    private Task HandleGetFrontier(IContext context, GetFrontier q)
    {
        // A concept is on the frontier if:
        // 1. It is NOT yet mastered (P(known) < threshold)
        // 2. ALL its prerequisites ARE mastered (P(known) >= threshold)
        var frontier = new List<ConceptNode>();

        foreach (var (conceptId, concept) in _graph.Concepts)
        {
            double currentMastery = q.MasteryMap.GetValueOrDefault(conceptId, 0.0);
            if (currentMastery >= q.MasteryThreshold)
                continue; // Already mastered, not frontier

            // Check all prerequisites
            bool allPrereqsMet = true;
            if (_graph.PrerequisitesByTarget.TryGetValue(conceptId, out var prereqEdges))
            {
                foreach (var edge in prereqEdges)
                {
                    double prereqMastery = q.MasteryMap.GetValueOrDefault(edge.FromConceptId, 0.0);
                    if (prereqMastery < q.MasteryThreshold)
                    {
                        allPrereqsMet = false;
                        break;
                    }
                }
            }
            // If concept has no prerequisites, it is eligible by default

            if (allPrereqsMet)
                frontier.Add(concept);
        }

        context.Respond(new GetFrontierResponse(frontier));
        return Task.CompletedTask;
    }

    private Task HandleGetConceptCluster(IContext context, GetConceptCluster q)
    {
        var cluster = new List<ConceptNode>();
        foreach (var concept in _graph.Concepts.Values)
        {
            if (string.Equals(concept.Category, q.Category, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(concept.Subject, q.Subject, StringComparison.OrdinalIgnoreCase))
            {
                cluster.Add(concept);
            }
        }

        context.Respond(new GetConceptClusterResponse(q.Category, q.Subject, cluster));
        return Task.CompletedTask;
    }

    // ── Hot Reload ──

    private async Task HandleCurriculumPublished(IContext context, CurriculumPublished msg)
    {
        _logger.LogInformation(
            "CurriculumPublished received. Reloading graph from Neo4j. " +
            "PublishedBy={PublishedBy}, NewVersion={NewVersion}",
            msg.PublishedBy, msg.NewVersion);

        var previousVersion = _graph.Version;
        await LoadGraphFromRepository();

        // Tell McmGraphActor to reload its data too
        if (_mcmChild != null)
            context.Send(_mcmChild, new ReloadMcmData());

        _logger.LogInformation(
            "Graph reloaded. Version {PrevVersion} -> {NewVersion}. " +
            "Concepts={ConceptCount}, Edges={EdgeCount}",
            previousVersion, _graph.Version, _graph.Concepts.Count, _graph.EdgeCount);
    }

    // ── Health ──

    private Task HandleGetGraphHealth(IContext context)
    {
        context.Respond(new GraphHealthResponse(
            ConceptCount: _graph.Concepts.Count,
            EdgeCount: _graph.EdgeCount,
            Version: _graph.Version,
            LastLoadedAt: _lastLoadedAt,
            IsLoaded: _graph.Concepts.Count > 0));
        return Task.CompletedTask;
    }

    // ── MCM Forwarding ──

    private Task ForwardToMcmChild(IContext context, object msg)
    {
        if (_mcmChild != null)
            context.Forward(_mcmChild);
        else
            _logger.LogWarning("McmGraphActor child not available. Dropping message: {Type}", msg.GetType().Name);
        return Task.CompletedTask;
    }
}
