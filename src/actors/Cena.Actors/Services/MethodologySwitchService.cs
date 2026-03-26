// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — MethodologySwitchService (Domain Service)
// 5-step MCM algorithm: classify error → MCM lookup → filter tried →
// select best → fallback. Includes cycling prevention and escalation.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services;

/// <summary>
/// Domain service (NOT an actor). Injected into StudentActor via DI.
/// Decides which teaching methodology to switch to when stagnation is detected.
/// Uses the MCM (Mode × Capability × Methodology) graph for recommendations.
/// </summary>
public interface IMethodologySwitchService
{
    MethodologySwitchDecision DecideSwitch(MethodologySwitchInput input);
}

public sealed class MethodologySwitchService : IMethodologySwitchService
{
    private readonly ILogger<MethodologySwitchService> _logger;

    // ── All 8 methodologies in the system ──
    public static readonly string[] AllMethodologies =
    {
        "socratic", "spaced_repetition", "feynman", "project_based",
        "blooms_progression", "worked_example", "analogy", "retrieval_practice"
    };

    // ── Error-type precedence (Step 1) ──
    private static readonly Dictionary<string, int> ErrorPrecedence = new()
    {
        ["conceptual"] = 1,   // Highest priority — hardest to overcome
        ["procedural"] = 2,
        ["motivational"] = 3  // Lowest priority
    };

    // ── Fallback defaults when MCM graph has no entry (Step 5) ──
    private static readonly Dictionary<string, List<McmCandidate>> ErrorTypeDefaults = new()
    {
        ["conceptual"] = new() { new("socratic", 0.80), new("feynman", 0.65), new("analogy", 0.55) },
        ["procedural"] = new() { new("worked_example", 0.85), new("retrieval_practice", 0.70), new("spaced_repetition", 0.60) },
        ["motivational"] = new() { new("project_based", 0.75), new("analogy", 0.60), new("blooms_progression", 0.50) }
    };

    public MethodologySwitchService(ILogger<MethodologySwitchService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 5-step methodology switching algorithm.
    /// </summary>
    public MethodologySwitchDecision DecideSwitch(MethodologySwitchInput input)
    {
        // ═══ Step 1: Classify dominant error type (by precedence) ═══
        string dominantError = ClassifyDominantError(input.ErrorDistribution);

        // ═══ Step 2: MCM graph lookup ═══
        var candidates = LookupMcm(dominantError, input.ConceptCategory, input.McmEntries);

        // ═══ Step 3: Filter out already-tried methods ═══
        var filtered = candidates
            .Where(c => !input.MethodAttemptHistory.Contains(c.Methodology))
            .ToList();

        // ═══ Step 4: Select best remaining ═══
        if (filtered.Count > 0)
        {
            // Pick first with confidence > 0.5, else best available
            var selected = filtered.FirstOrDefault(c => c.Confidence > 0.5)
                           ?? filtered[0];

            _logger.LogInformation(
                "Methodology switch: {Error}+{Category} → {Method} (confidence={Confidence:F2}, tried={Tried}/{Total})",
                dominantError, input.ConceptCategory, selected.Methodology,
                selected.Confidence, input.MethodAttemptHistory.Count, AllMethodologies.Length);

            return new MethodologySwitchDecision(
                NewMethodology: selected.Methodology,
                Confidence: selected.Confidence,
                DominantErrorType: dominantError,
                IsEscalation: false,
                Reason: $"MCM recommendation: {selected.Methodology} (confidence {selected.Confidence:F2})",
                SuggestedAction: null
            );
        }

        // ═══ Step 5: Escalation — all methods exhausted ═══
        if (input.MethodAttemptHistory.Count >= AllMethodologies.Length)
        {
            _logger.LogWarning(
                "All {Count} methodologies exhausted for concept cluster {Category} — escalating to mentor-resistant",
                AllMethodologies.Length, input.ConceptCategory);

            return new MethodologySwitchDecision(
                NewMethodology: input.CurrentMethodology, // Keep current
                Confidence: 0,
                DominantErrorType: dominantError,
                IsEscalation: true,
                Reason: $"All {AllMethodologies.Length} methodologies exhausted for this concept cluster",
                SuggestedAction: "Flag as mentor-resistant. Suggest student skip to a related concept or seek human tutoring."
            );
        }

        // Fallback: pick from defaults that haven't been tried
        var fallbackCandidates = ErrorTypeDefaults.GetValueOrDefault(dominantError)
            ?? ErrorTypeDefaults["procedural"];
        var fallback = fallbackCandidates.FirstOrDefault(c => !input.MethodAttemptHistory.Contains(c.Methodology));

        if (fallback != null)
        {
            _logger.LogInformation(
                "Methodology switch (fallback): {Error} → {Method} (no MCM entry, using defaults)",
                dominantError, fallback.Methodology);

            return new MethodologySwitchDecision(
                NewMethodology: fallback.Methodology,
                Confidence: fallback.Confidence * 0.8, // Discount fallback confidence
                DominantErrorType: dominantError,
                IsEscalation: false,
                Reason: $"Fallback default: {fallback.Methodology} (no MCM entry for {dominantError}+{input.ConceptCategory})",
                SuggestedAction: null
            );
        }

        // Truly exhausted — escalate
        return new MethodologySwitchDecision(
            NewMethodology: input.CurrentMethodology,
            Confidence: 0,
            DominantErrorType: dominantError,
            IsEscalation: true,
            Reason: "All methodologies and fallbacks exhausted",
            SuggestedAction: "Flag as mentor-resistant. Recommend human tutoring."
        );
    }

    // ── Step 1: Classify dominant error type by precedence ──
    private static string ClassifyDominantError(Dictionary<string, int> errorDistribution)
    {
        if (errorDistribution.Count == 0)
            return "procedural"; // Default

        // Sort by precedence (conceptual first), then by count (most frequent)
        return errorDistribution
            .OrderBy(kv => ErrorPrecedence.GetValueOrDefault(kv.Key, 99))
            .ThenByDescending(kv => kv.Value)
            .First()
            .Key;
    }

    // ── Step 2: MCM lookup with fallback ──
    private static List<McmCandidate> LookupMcm(
        string errorType, string conceptCategory, List<McmCandidate>? mcmEntries)
    {
        // If MCM entries provided (from Neo4j cache), use them
        if (mcmEntries != null && mcmEntries.Count > 0)
        {
            return mcmEntries.OrderByDescending(c => c.Confidence).ToList();
        }

        // Fallback to error-type defaults
        return ErrorTypeDefaults.GetValueOrDefault(errorType)
            ?? ErrorTypeDefaults["procedural"];
    }
}

// ── Types ──

public record McmCandidate(string Methodology, double Confidence);

public record MethodologySwitchInput(
    string CurrentMethodology,
    string ConceptCategory,
    Dictionary<string, int> ErrorDistribution,  // errorType → count in last 3 sessions
    HashSet<string> MethodAttemptHistory,        // methods already tried for this cluster
    List<McmCandidate>? McmEntries              // from Neo4j cache, null if no entry
);

public record MethodologySwitchDecision(
    string NewMethodology,
    double Confidence,
    string DominantErrorType,
    bool IsEscalation,
    string Reason,
    string? SuggestedAction
);
