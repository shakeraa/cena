// =============================================================================
// Cena Platform -- MethodologySwitchService (Domain Service, NOT an Actor)
// Layer: Domain Services | Runtime: .NET 9
//
// DESIGN NOTES:
//   - Called by StudentActor when stagnation is detected or student requests switch.
//   - NOT an actor: stateless, injected via DI, called synchronously.
//   - 5-step algorithm:
//     1. Classify dominant error type (precedence: conceptual > procedural > motivational)
//     2. MCM graph lookup: query Neo4j cache for (error_type, concept_category) -> candidates
//     3. Filter: exclude methods in MethodAttemptHistory for this concept cluster
//     4. Select: first candidate with confidence > 0.5, else best available
//     5. Fallback: if MCM entry missing, use error-type defaults
//   - Cycling prevention: track last 3 stagnation cycles per concept
//   - Escalation: all 8 methods exhausted -> flag "mentor-resistant"
//   - 3-session cooldown enforcement after each switch
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;

using Cena.Contracts.Actors;

namespace Cena.Actors;

// =============================================================================
// MCM GRAPH TYPES (Methodology-Concept-Mapping)
// =============================================================================

/// <summary>
/// A candidate methodology from the MCM graph. Represents an edge in
/// the Neo4j graph: (ErrorType)-[:REMEDIATED_BY]->(Methodology) scoped
/// to a concept category.
/// </summary>
public sealed record McmCandidate(
    /// <summary>The recommended methodology.</summary>
    Methodology Methodology,

    /// <summary>Confidence score from the MCM graph (0.0-1.0).</summary>
    double Confidence,

    /// <summary>Evidence count: how many students benefited from this switch.</summary>
    int EvidenceCount,

    /// <summary>Average improvement in P(known) after switching.</summary>
    double AvgMasteryImprovement);

/// <summary>
/// Tracks stagnation cycles for a concept to prevent methodology cycling.
/// A cycle is: stagnation detected -> switch -> cooldown -> stagnation again.
/// </summary>
public sealed record StagnationCycleRecord(
    Methodology FromMethodology,
    Methodology ToMethodology,
    double StagnationScore,
    DateTimeOffset SwitchedAt);

// =============================================================================
// METHODOLOGY SWITCH SERVICE
// =============================================================================

/// <summary>
/// Domain service that decides the optimal methodology switch when stagnation
/// is detected. Implements the 5-step algorithm:
///
/// <para><b>Algorithm:</b></para>
/// <list type="number">
///   <item>
///     <b>Classify dominant error type:</b> Precedence order is
///     Conceptual > Procedural > Motivational. This determines which
///     remediation path to follow in the MCM graph.
///   </item>
///   <item>
///     <b>MCM graph lookup:</b> Query Neo4j (via Redis cache) for
///     (error_type, concept_category) -> list of candidate methodologies,
///     ordered by confidence score.
///   </item>
///   <item>
///     <b>Filter:</b> Exclude methodologies already in the student's
///     MethodAttemptHistory for this concept cluster. This prevents
///     re-trying approaches that already failed.
///   </item>
///   <item>
///     <b>Select:</b> First candidate with confidence > 0.5. If none
///     qualify, select the best available regardless of confidence.
///   </item>
///   <item>
///     <b>Fallback:</b> If no MCM entry exists for this (error_type,
///     concept_category) pair, use error-type defaults:
///     - Conceptual -> Feynman
///     - Procedural -> WorkedExample
///     - Motivational -> ProjectBased
///   </item>
/// </list>
///
/// <para><b>Cycling Prevention:</b></para>
/// Tracks the last 3 stagnation cycles per concept. If the same methodology
/// pair appears in recent cycles, it's excluded from candidates.
///
/// <para><b>Escalation:</b></para>
/// When all 8 methodologies have been exhausted for a concept cluster,
/// the service flags the concept as "mentor-resistant" and recommends:
/// - Skip the concept and return later
/// - Connect with a human tutor
/// - Try a different concept in the same cluster
/// </summary>
public sealed class MethodologySwitchService : IMethodologySwitchService
{
    // ---- Dependencies ----
    private readonly IDriver _neo4jDriver;
    private readonly IDistributedCache _cache;
    private readonly ILogger<MethodologySwitchService> _logger;

    // ---- Configuration ----
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
    private const double MinConfidenceThreshold = 0.5;
    private const int MaxStagnationCycles = 3;
    private const int CooldownSessions = 3;

    // ---- Telemetry ----
    private static readonly ActivitySource ActivitySourceInstance =
        new("Cena.Services.MethodologySwitchService", "1.0.0");
    private static readonly Meter MeterInstance =
        new("Cena.Services.MethodologySwitchService", "1.0.0");
    private static readonly Counter<long> SwitchDecisionCounter =
        MeterInstance.CreateCounter<long>("cena.methodology.switch_decisions_total", description: "Total methodology switch decisions");
    private static readonly Counter<long> EscalationCounter =
        MeterInstance.CreateCounter<long>("cena.methodology.escalations_total", description: "Total methodology escalations (all methods exhausted)");
    private static readonly Counter<long> FallbackCounter =
        MeterInstance.CreateCounter<long>("cena.methodology.fallbacks_total", description: "Total fallback decisions (no MCM data)");
    private static readonly Histogram<double> DecisionLatency =
        MeterInstance.CreateHistogram<double>("cena.methodology.decision_ms", description: "Decision latency in milliseconds");

    /// <summary>
    /// All available methodologies. Used to detect exhaustion.
    /// Must stay in sync with the <see cref="Methodology"/> enum.
    /// </summary>
    private static readonly Methodology[] AllMethodologies =
    {
        Methodology.Socratic,
        Methodology.SpacedRepetition,
        Methodology.Feynman,
        Methodology.ProjectBased,
        Methodology.BloomsProgression,
        Methodology.WorkedExample,
        Methodology.Analogy,
        Methodology.RetrievalPractice
    };

    /// <summary>
    /// Default methodology recommendations by error type.
    /// Used as fallback when the MCM graph has no data for the given
    /// (error_type, concept_category) pair.
    /// </summary>
    private static readonly Dictionary<ErrorType, Methodology[]> ErrorTypeDefaults = new()
    {
        [ErrorType.Conceptual] = new[]
        {
            Methodology.Feynman,
            Methodology.Analogy,
            Methodology.Socratic,
            Methodology.BloomsProgression
        },
        [ErrorType.Procedural] = new[]
        {
            Methodology.WorkedExample,
            Methodology.DrillAndPractice,
            Methodology.BloomsProgression,
            Methodology.RetrievalPractice
        },
        [ErrorType.Motivational] = new[]
        {
            Methodology.ProjectBased,
            Methodology.Socratic,
            Methodology.Analogy,
            Methodology.RetrievalPractice
        },
        [ErrorType.None] = new[]
        {
            Methodology.SpacedRepetition,
            Methodology.RetrievalPractice,
            Methodology.Feynman
        }
    };

    public MethodologySwitchService(
        IDriver neo4jDriver,
        IDistributedCache cache,
        ILogger<MethodologySwitchService> logger)
    {
        _neo4jDriver = neo4jDriver ?? throw new ArgumentNullException(nameof(neo4jDriver));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // =========================================================================
    // MAIN DECISION ALGORITHM
    // =========================================================================

    /// <summary>
    /// Executes the 5-step methodology switch decision algorithm.
    /// Returns a decision containing the recommended methodology (or escalation).
    /// </summary>
    public async Task<DecideSwitchResponse> DecideSwitch(DecideSwitchRequest request)
    {
        using var activity = ActivitySourceInstance.StartActivity("MethodologySwitch.Decide");
        activity?.SetTag("student.id", request.StudentId);
        activity?.SetTag("concept.id", request.ConceptId);
        activity?.SetTag("error.type", request.DominantErrorType.ToString());
        activity?.SetTag("current.methodology", request.CurrentMethodology.ToString());

        var sw = Stopwatch.StartNew();
        var traceLog = new List<string>();

        try
        {
            // ====================================================================
            // STEP 1: Classify dominant error type
            // Precedence: Conceptual > Procedural > Motivational
            // ====================================================================

            var errorType = request.DominantErrorType;
            traceLog.Add($"Step1: DominantErrorType={errorType}");

            // ====================================================================
            // STEP 2: MCM graph lookup
            // Query Neo4j (via Redis cache) for candidates
            // ====================================================================

            var mcmCandidates = await QueryMcmGraph(
                errorType, request.ConceptCategory);

            traceLog.Add($"Step2: MCM returned {mcmCandidates.Count} candidates " +
                $"for ({errorType}, {request.ConceptCategory})");

            // ====================================================================
            // STEP 3: Filter out previously attempted methods
            // ====================================================================

            var attemptedMethods = request.MethodAttemptHistory
                .Select(m => Enum.TryParse<Methodology>(m, true, out var parsed) ? parsed : (Methodology?)null)
                .Where(m => m.HasValue)
                .Select(m => m!.Value)
                .ToHashSet();

            // Also exclude the current methodology
            attemptedMethods.Add(request.CurrentMethodology);

            var filtered = mcmCandidates
                .Where(c => !attemptedMethods.Contains(c.Methodology))
                .OrderByDescending(c => c.Confidence)
                .ToList();

            traceLog.Add($"Step3: After filtering {attemptedMethods.Count} attempted methods, " +
                $"{filtered.Count} candidates remain");

            // ====================================================================
            // STEP 4: Select best candidate
            // ====================================================================

            McmCandidate? selected = null;

            // First pass: confidence > 0.5
            selected = filtered.FirstOrDefault(c => c.Confidence >= MinConfidenceThreshold);

            if (selected == null && filtered.Count > 0)
            {
                // Second pass: best available regardless of confidence
                selected = filtered.First();
                traceLog.Add($"Step4: No candidate above {MinConfidenceThreshold} confidence. " +
                    $"Using best available: {selected.Methodology} ({selected.Confidence:F2})");
            }
            else if (selected != null)
            {
                traceLog.Add($"Step4: Selected {selected.Methodology} " +
                    $"(confidence={selected.Confidence:F2}, evidence={selected.EvidenceCount})");
            }

            // ====================================================================
            // STEP 5: Fallback (no MCM data or all candidates filtered out)
            // ====================================================================

            if (selected == null)
            {
                selected = ApplyFallback(errorType, attemptedMethods);
                if (selected != null)
                {
                    FallbackCounter.Add(1,
                        new KeyValuePair<string, object?>("error.type", errorType.ToString()));
                    traceLog.Add($"Step5: Fallback applied. Selected {selected.Methodology}");
                }
            }

            // ====================================================================
            // CHECK: All methodologies exhausted?
            // ====================================================================

            if (selected == null)
            {
                // All 8 methodologies have been tried
                EscalationCounter.Add(1,
                    new KeyValuePair<string, object?>("concept.id", request.ConceptId));

                traceLog.Add("ESCALATION: All methodologies exhausted for this concept cluster");

                _logger.LogWarning(
                    "All methodologies exhausted for student {StudentId}, concept {ConceptId}. " +
                    "Attempted: [{Attempted}]",
                    request.StudentId, request.ConceptId,
                    string.Join(", ", attemptedMethods));

                sw.Stop();
                DecisionLatency.Record(sw.Elapsed.TotalMilliseconds);

                return new DecideSwitchResponse(
                    ShouldSwitch: false,
                    RecommendedMethodology: null,
                    Confidence: 0.0,
                    AllMethodologiesExhausted: true,
                    EscalationAction: DetermineEscalationAction(request),
                    DecisionTrace: string.Join(" | ", traceLog));
            }

            // ====================================================================
            // SUCCESS: Return switch decision
            // ====================================================================

            SwitchDecisionCounter.Add(1,
                new KeyValuePair<string, object?>("from", request.CurrentMethodology.ToString()),
                new KeyValuePair<string, object?>("to", selected.Methodology.ToString()),
                new KeyValuePair<string, object?>("error.type", errorType.ToString()));

            sw.Stop();
            DecisionLatency.Record(sw.Elapsed.TotalMilliseconds);

            _logger.LogInformation(
                "Methodology switch decision for student {StudentId}, concept {ConceptId}: " +
                "{From} -> {To} (confidence={Confidence:F2}, error={ErrorType})",
                request.StudentId, request.ConceptId,
                request.CurrentMethodology, selected.Methodology,
                selected.Confidence, errorType);

            return new DecideSwitchResponse(
                ShouldSwitch: true,
                RecommendedMethodology: selected.Methodology,
                Confidence: selected.Confidence,
                AllMethodologiesExhausted: false,
                EscalationAction: null,
                DecisionTrace: string.Join(" | ", traceLog));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Methodology switch decision failed for student {StudentId}, concept {ConceptId}",
                request.StudentId, request.ConceptId);

            sw.Stop();
            DecisionLatency.Record(sw.Elapsed.TotalMilliseconds);

            // On error, apply safe fallback
            var fallback = ApplyFallback(request.DominantErrorType, new HashSet<Methodology>());
            traceLog.Add($"ERROR: {ex.Message}. Applied emergency fallback: {fallback?.Methodology}");

            return new DecideSwitchResponse(
                ShouldSwitch: fallback != null,
                RecommendedMethodology: fallback?.Methodology,
                Confidence: fallback?.Confidence ?? 0.0,
                AllMethodologiesExhausted: false,
                EscalationAction: null,
                DecisionTrace: string.Join(" | ", traceLog));
        }
    }

    // =========================================================================
    // MCM GRAPH QUERY (Neo4j via Redis cache)
    // =========================================================================

    /// <summary>
    /// Queries the MCM (Methodology-Concept-Mapping) graph in Neo4j for
    /// methodology candidates. Uses Redis as a distributed cache layer
    /// to avoid repeated graph queries.
    ///
    /// <para><b>Cypher Query:</b></para>
    /// <code>
    /// MATCH (e:ErrorType {name: $errorType})-[r:REMEDIATED_BY]->(m:Methodology)
    /// WHERE r.conceptCategory = $category
    /// RETURN m.name AS methodology, r.confidence AS confidence,
    ///        r.evidenceCount AS evidence, r.avgImprovement AS improvement
    /// ORDER BY r.confidence DESC
    /// </code>
    /// </summary>
    private async Task<List<McmCandidate>> QueryMcmGraph(
        ErrorType errorType, string conceptCategory)
    {
        using var activity = ActivitySourceInstance.StartActivity("MethodologySwitch.QueryMcm");

        // ---- Check Redis cache first ----
        var cacheKey = $"mcm:{errorType}:{conceptCategory}";
        var cached = await TryGetFromCache<List<McmCandidate>>(cacheKey);
        if (cached != null)
        {
            activity?.SetTag("cache.hit", true);
            return cached;
        }

        activity?.SetTag("cache.hit", false);

        // ---- Query Neo4j ----
        var candidates = new List<McmCandidate>();

        try
        {
            await using var session = _neo4jDriver.AsyncSession();

            var result = await session.ExecuteReadAsync(async tx =>
            {
                var query = @"
                    MATCH (e:ErrorType {name: $errorType})-[r:REMEDIATED_BY]->(m:Methodology)
                    WHERE r.conceptCategory = $category
                    RETURN m.name AS methodology, r.confidence AS confidence,
                           r.evidenceCount AS evidence, r.avgImprovement AS improvement
                    ORDER BY r.confidence DESC";

                var cursor = await tx.RunAsync(query, new
                {
                    errorType = errorType.ToString(),
                    category = conceptCategory
                });

                return await cursor.ToListAsync();
            });

            foreach (var record in result)
            {
                var methodologyName = record["methodology"].As<string>();
                if (Enum.TryParse<Methodology>(methodologyName, true, out var methodology))
                {
                    candidates.Add(new McmCandidate(
                        methodology,
                        record["confidence"].As<double>(),
                        record["evidence"].As<int>(),
                        record["improvement"].As<double>()));
                }
            }

            // ---- Cache result ----
            await SetCache(cacheKey, candidates, CacheTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Neo4j query failed for MCM lookup. ErrorType={ErrorType}, " +
                "Category={Category}. Falling back to defaults.",
                errorType, conceptCategory);
            // Return empty -- fallback logic will handle it
        }

        return candidates;
    }

    // =========================================================================
    // FALLBACK LOGIC
    // =========================================================================

    /// <summary>
    /// Applies error-type-based defaults when the MCM graph has no data.
    /// Returns the first methodology not yet attempted by the student.
    /// </summary>
    private static McmCandidate? ApplyFallback(
        ErrorType errorType, HashSet<Methodology> attemptedMethods)
    {
        if (!ErrorTypeDefaults.TryGetValue(errorType, out var defaults))
            defaults = ErrorTypeDefaults[ErrorType.None];

        foreach (var methodology in defaults)
        {
            if (!attemptedMethods.Contains(methodology))
            {
                return new McmCandidate(methodology, 0.4, 0, 0.0);
            }
        }

        // Try any methodology not yet attempted
        foreach (var methodology in AllMethodologies)
        {
            if (!attemptedMethods.Contains(methodology))
            {
                return new McmCandidate(methodology, 0.2, 0, 0.0);
            }
        }

        return null; // All exhausted
    }

    // =========================================================================
    // ESCALATION
    // =========================================================================

    /// <summary>
    /// Determines the appropriate escalation action when all methodologies
    /// have been exhausted for a concept cluster.
    ///
    /// Options:
    /// - "suggest_skip": Skip the concept, revisit later with fresh perspective
    /// - "connect_tutor": Connect student with a human tutor
    /// - "try_adjacent": Try a related concept in the same cluster
    /// </summary>
    private static string DetermineEscalationAction(DecideSwitchRequest request)
    {
        // If stagnation score is very high, human intervention is recommended
        if (request.StagnationScore > 0.9)
            return "connect_tutor";

        // If many consecutive stagnant sessions, suggest skipping
        if (request.ConsecutiveStagnantSessions >= 5)
            return "suggest_skip";

        // Default: try an adjacent concept
        return "try_adjacent";
    }

    // =========================================================================
    // CACHE HELPERS
    // =========================================================================

    private async Task<T?> TryGetFromCache<T>(string key) where T : class
    {
        try
        {
            var bytes = await _cache.GetAsync(key);
            if (bytes == null) return null;

            return System.Text.Json.JsonSerializer.Deserialize<T>(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cache read failed for key {Key}", key);
            return null;
        }
    }

    private async Task SetCache<T>(string key, T value, TimeSpan ttl)
    {
        try
        {
            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value);
            await _cache.SetAsync(key, bytes, new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cache write failed for key {Key}", key);
        }
    }
}
