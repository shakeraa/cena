// =============================================================================
// Cena Platform -- McmGraphActor (Child of CurriculumGraphActor)
// Layer: Actor Model | Runtime: .NET 9 | Framework: Proto.Actor v1.x
//
// Holds the MCM (ErrorType, ConceptCategory) -> [(Methodology, confidence)]
// mapping in memory. Provides methodology recommendations for stagnation
// recovery and supports confidence updates from the intelligence layer flywheel.
// =============================================================================

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Proto;

namespace Cena.Actors.Graph;

// =============================================================================
// MCM DATA STRUCTURE
// =============================================================================

/// <summary>
/// Composite key for MCM lookup: (ErrorType, ConceptCategory).
/// </summary>
public readonly record struct McmKey(string ErrorType, string ConceptCategory)
{
    public override string ToString() => $"{ErrorType}:{ConceptCategory}";
}

/// <summary>
/// A single methodology recommendation with confidence score.
/// </summary>
public sealed record McmRecommendation(string Methodology, double Confidence);

// =============================================================================
// MESSAGES
// =============================================================================

/// <summary>
/// Look up methodology recommendations for a given error type and concept category.
/// ExcludedMethodologies filters out already-tried approaches.
/// </summary>
public sealed record McmLookup(
    string ErrorType,
    string ConceptCategory,
    IReadOnlySet<string>? ExcludedMethodologies = null);

/// <summary>Response to McmLookup.</summary>
public sealed record McmLookupResponse(
    string ErrorType,
    string ConceptCategory,
    IReadOnlyList<McmRecommendation> Recommendations,
    bool UsedFallback);

/// <summary>
/// Update the confidence of a specific MCM entry based on outcome data
/// from the intelligence layer flywheel.
/// </summary>
public sealed record UpdateMcmConfidence(
    string ErrorType,
    string ConceptCategory,
    string Methodology,
    double ConfidenceDelta);

/// <summary>Internal message: reload MCM data from repository.</summary>
public sealed record ReloadMcmData;

// =============================================================================
// ACTOR
// =============================================================================

/// <summary>
/// Child actor of CurriculumGraphActor. Holds the MCM
/// (ErrorType, ConceptCategory) -> [(Methodology, confidence)] map in memory.
/// Supports lookup with exclusion filter and confidence updates.
/// </summary>
public sealed class McmGraphActor : IActor
{
    private readonly INeo4jGraphRepository _repository;
    private readonly ILogger<McmGraphActor> _logger;

    /// <summary>
    /// MCM map: (ErrorType, ConceptCategory) -> sorted list of (Methodology, Confidence).
    /// Sorted descending by confidence for fast "pick best" lookups.
    /// </summary>
    private readonly Dictionary<McmKey, List<McmRecommendation>> _mcmMap = new();

    // ── Fallback defaults when no MCM entry exists ──
    private static readonly Dictionary<string, List<McmRecommendation>> FallbackDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["conceptual"] = new()
        {
            new McmRecommendation("socratic", 0.80),
            new McmRecommendation("feynman", 0.65),
            new McmRecommendation("analogy", 0.55),
            new McmRecommendation("worked_example", 0.40)
        },
        ["procedural"] = new()
        {
            new McmRecommendation("worked_example", 0.85),
            new McmRecommendation("retrieval_practice", 0.70),
            new McmRecommendation("spaced_repetition", 0.60),
            new McmRecommendation("blooms_progression", 0.45)
        },
        ["motivational"] = new()
        {
            new McmRecommendation("project_based", 0.75),
            new McmRecommendation("analogy", 0.60),
            new McmRecommendation("blooms_progression", 0.50),
            new McmRecommendation("retrieval_practice", 0.40)
        }
    };

    // ── Telemetry (ACT-031: instance-based via IMeterFactory) ──
    private readonly Counter<long> _lookupCounter;
    private readonly Counter<long> _fallbackCounter;
    private readonly Counter<long> _confidenceUpdates;

    public McmGraphActor(INeo4jGraphRepository repository, ILogger<McmGraphActor> logger, IMeterFactory meterFactory)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var meter = meterFactory.Create("Cena.Actors.McmGraph", "1.0.0");
        _lookupCounter = meter.CreateCounter<long>("cena.mcm.lookups_total", description: "Total MCM lookups");
        _fallbackCounter = meter.CreateCounter<long>("cena.mcm.fallback_total", description: "MCM lookups using fallback defaults");
        _confidenceUpdates = meter.CreateCounter<long>("cena.mcm.confidence_updates_total", description: "MCM confidence updates");
    }

    public Task ReceiveAsync(IContext context)
    {
        return context.Message switch
        {
            Started         => OnStarted(),
            McmLookup q     => HandleLookup(context, q),
            UpdateMcmConfidence cmd => HandleUpdateConfidence(cmd),
            ReloadMcmData   => HandleReload(),
            _ => Task.CompletedTask
        };
    }

    // ── Lifecycle ──

    private async Task OnStarted()
    {
        _logger.LogInformation("McmGraphActor starting. Loading MCM entries...");
        await LoadMcmEntries();
        _logger.LogInformation("McmGraphActor ready. MCM keys={KeyCount}", _mcmMap.Count);
    }

    // ── Loading ──

    private async Task LoadMcmEntries()
    {
        try
        {
            var entries = await _repository.LoadMcmEntriesAsync();

            _mcmMap.Clear();

            foreach (var entry in entries)
            {
                var key = new McmKey(
                    entry.ErrorType.ToLowerInvariant(),
                    entry.ConceptCategory.ToLowerInvariant());

                if (!_mcmMap.TryGetValue(key, out var list))
                {
                    list = new List<McmRecommendation>();
                    _mcmMap[key] = list;
                }

                list.Add(new McmRecommendation(entry.Methodology, entry.Confidence));
            }

            // Sort each list by confidence descending
            foreach (var list in _mcmMap.Values)
                list.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load MCM entries from repository. Using fallback defaults only.");
        }
    }

    // ── Lookup ──

    private Task HandleLookup(IContext context, McmLookup q)
    {
        _lookupCounter.Add(1);

        var key = new McmKey(
            q.ErrorType.ToLowerInvariant(),
            q.ConceptCategory.ToLowerInvariant());

        bool usedFallback = false;
        List<McmRecommendation>? candidates;

        if (!_mcmMap.TryGetValue(key, out candidates) || candidates.Count == 0)
        {
            // Try error-type-only fallback
            usedFallback = true;
            _fallbackCounter.Add(1);
            candidates = FallbackDefaults.GetValueOrDefault(q.ErrorType.ToLowerInvariant())
                ?? FallbackDefaults["procedural"];
        }

        // Apply exclusion filter
        IReadOnlyList<McmRecommendation> filtered;
        if (q.ExcludedMethodologies != null && q.ExcludedMethodologies.Count > 0)
        {
            var excludeSet = new HashSet<string>(
                q.ExcludedMethodologies, StringComparer.OrdinalIgnoreCase);
            filtered = candidates
                .Where(r => !excludeSet.Contains(r.Methodology))
                .ToList();
        }
        else
        {
            filtered = candidates;
        }

        // If all candidates were excluded, return the full fallback list (unfiltered)
        // so the caller knows what is available (even if already tried)
        if (filtered.Count == 0)
        {
            filtered = candidates;
            usedFallback = true;
        }

        context.Respond(new McmLookupResponse(
            q.ErrorType, q.ConceptCategory, filtered, usedFallback));

        return Task.CompletedTask;
    }

    // ── Confidence Update (Intelligence Layer Flywheel) ──

    private Task HandleUpdateConfidence(UpdateMcmConfidence cmd)
    {
        _confidenceUpdates.Add(1);

        var key = new McmKey(
            cmd.ErrorType.ToLowerInvariant(),
            cmd.ConceptCategory.ToLowerInvariant());

        if (!_mcmMap.TryGetValue(key, out var list))
        {
            // Create a new entry for this key
            list = new List<McmRecommendation>();
            _mcmMap[key] = list;
        }

        // Find and update the methodology's confidence
        int idx = list.FindIndex(r =>
            string.Equals(r.Methodology, cmd.Methodology, StringComparison.OrdinalIgnoreCase));

        if (idx >= 0)
        {
            double newConfidence = Math.Clamp(list[idx].Confidence + cmd.ConfidenceDelta, 0.0, 1.0);
            list[idx] = list[idx] with { Confidence = newConfidence };

            _logger.LogDebug(
                "MCM confidence updated: {Key}/{Method} -> {Confidence:F3} (delta={Delta:+F3})",
                key, cmd.Methodology, newConfidence, cmd.ConfidenceDelta);
        }
        else
        {
            // Add new entry with the delta as initial confidence (clamped to valid range)
            double initialConfidence = Math.Clamp(cmd.ConfidenceDelta, 0.1, 1.0);
            list.Add(new McmRecommendation(cmd.Methodology, initialConfidence));

            _logger.LogDebug(
                "MCM new entry: {Key}/{Method} -> {Confidence:F3}",
                key, cmd.Methodology, initialConfidence);
        }

        // Re-sort by confidence descending
        list.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

        return Task.CompletedTask;
    }

    // ── Reload ──

    private async Task HandleReload()
    {
        _logger.LogInformation("McmGraphActor reloading MCM entries...");
        await LoadMcmEntries();
        _logger.LogInformation("McmGraphActor reloaded. MCM keys={KeyCount}", _mcmMap.Count);
    }
}
