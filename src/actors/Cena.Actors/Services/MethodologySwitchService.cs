// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — MethodologySwitchService (Domain Service)
// 5-step MCM algorithm: classify error → MCM lookup → filter tried →
// select best → fallback. Includes cycling prevention and escalation.
// ═══════════════════════════════════════════════════════════════════════

using Cena.Actors.Mastery;
using Cena.Actors.Methodology;
using Cena.Actors.Students;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Services;

/// <summary>
/// Domain service (NOT an actor). Injected into StudentActor via DI.
/// Decides which teaching methodology to switch to when stagnation is detected.
/// Uses the MCM (Mode × Capability × Methodology) graph for recommendations.
/// </summary>
public interface IMethodologySwitchService
{
    Task<DecideSwitchResponse> DecideSwitch(DecideSwitchRequest request);
}

public sealed class MethodologySwitchService : IMethodologySwitchService
{
    private readonly ILogger<MethodologySwitchService> _logger;

    // ── All 9 methodologies — names must match Methodology enum for Enum.TryParse ──
    public static readonly string[] AllMethodologies =
    {
        "Socratic", "SpacedRepetition", "Feynman", "ProjectBased",
        "BloomsProgression", "WorkedExample", "Analogy", "RetrievalPractice",
        "DrillAndPractice"
    };

    // ── Error-type precedence (Step 1) ──
    private static readonly Dictionary<string, int> ErrorPrecedence = new()
    {
        ["conceptual"] = 1,   // Highest priority — hardest to overcome
        ["procedural"] = 2,
        ["motivational"] = 3  // Lowest priority
    };

    // ── Fallback defaults when MCM graph has no entry (Step 5) ──
    // Methodology names must match Methodology enum for Enum.TryParse
    private static readonly Dictionary<string, List<McmCandidate>> ErrorTypeDefaults = new()
    {
        ["conceptual"] = new() { new("Socratic", 0.80), new("Feynman", 0.65), new("Analogy", 0.55) },
        ["procedural"] = new() { new("WorkedExample", 0.85), new("RetrievalPractice", 0.70), new("SpacedRepetition", 0.60) },
        ["motivational"] = new() { new("ProjectBased", 0.75), new("Analogy", 0.60), new("BloomsProgression", 0.50) }
    };

    public MethodologySwitchService(ILogger<MethodologySwitchService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 5-step methodology switching algorithm with cooldown enforcement.
    /// Step 0: Check cooldown. Steps 1-5: classify error → MCM lookup → filter → select → fallback.
    /// </summary>
    public Task<DecideSwitchResponse> DecideSwitch(DecideSwitchRequest request)
    {
        // ═══ Step 0: Cooldown enforcement ═══
        if (request.CurrentAssignment != null)
        {
            var cooldown = MethodologyResolver.CheckCooldown(
                request.CurrentAssignment,
                request.SessionsSinceLastSwitch,
                DateTimeOffset.UtcNow);

            if (!cooldown.IsAllowed)
            {
                _logger.LogInformation(
                    "Methodology switch deferred for student {StudentId} concept {ConceptId}: {Reason}",
                    request.StudentId, request.ConceptId, cooldown.Reason);

                return Task.FromResult(new DecideSwitchResponse(
                    ShouldSwitch: false,
                    RecommendedMethodology: request.CurrentMethodology,
                    Confidence: 0,
                    AllMethodologiesExhausted: false,
                    EscalationAction: null,
                    DecisionTrace: $"Cooldown active: {cooldown.Reason}",
                    DeferredByCooldown: true,
                    CooldownSessionsRemaining: cooldown.SessionsRemaining,
                    CooldownHoursRemaining: cooldown.TimeRemaining.TotalHours));
            }
        }

        // Adapt request to internal types
        var errorDistribution = new Dictionary<string, int>();
        if (request.DominantErrorType != ErrorType.None)
            errorDistribution[request.DominantErrorType.ToString().ToLowerInvariant()] = 1;

        var triedMethods = new HashSet<string>(
            new HashSet<string>(request.MethodAttemptHistory ?? new List<string>()),
            StringComparer.OrdinalIgnoreCase);

        string dominantError = request.DominantErrorType != ErrorType.None
            ? request.DominantErrorType.ToString().ToLowerInvariant()
            : ClassifyDominantError(errorDistribution);

        string conceptCategory = request.ConceptCategory ?? "procedural";

        // ═══ Step 2: MCM graph lookup ═══
        var candidates = LookupMcm(dominantError, conceptCategory, null);

        // ═══ Step 3: Filter out already-tried methods ═══
        var filtered = candidates
            .Where(c => !triedMethods.Contains(c.Methodology))
            .ToList();

        // ═══ Step 4: Select best remaining ═══
        if (filtered.Count > 0)
        {
            var selected = filtered.FirstOrDefault(c => c.Confidence > 0.5)
                           ?? filtered[0];

            _logger.LogInformation(
                "Methodology switch: {Error}+{Category} → {Method} (confidence={Confidence:F2}, tried={Tried}/{Total})",
                dominantError, conceptCategory, selected.Methodology,
                selected.Confidence, triedMethods.Count, AllMethodologies.Length);

            var recommended = Enum.TryParse<Students.Methodology>(selected.Methodology, true, out var m)
                ? m : Students.Methodology.Socratic;

            return Task.FromResult(new DecideSwitchResponse(
                ShouldSwitch: true,
                RecommendedMethodology: recommended,
                Confidence: selected.Confidence,
                AllMethodologiesExhausted: false,
                EscalationAction: null,
                DecisionTrace: $"MCM recommendation: {selected.Methodology} (confidence {selected.Confidence:F2})"
            ));
        }

        // ═══ Step 5: Escalation — all methods exhausted ═══
        if (triedMethods.Count >= AllMethodologies.Length)
        {
            _logger.LogWarning(
                "All {Count} methodologies exhausted for concept cluster {Category} — escalating to mentor-resistant",
                AllMethodologies.Length, conceptCategory);

            return Task.FromResult(new DecideSwitchResponse(
                ShouldSwitch: false,
                RecommendedMethodology: request.CurrentMethodology,
                Confidence: 0,
                AllMethodologiesExhausted: true,
                EscalationAction: "Flag as mentor-resistant. Suggest student skip to a related concept or seek human tutoring.",
                DecisionTrace: $"All {AllMethodologies.Length} methodologies exhausted for this concept cluster"
            ));
        }

        // Fallback: pick from defaults that haven't been tried
        var fallbackCandidates = ErrorTypeDefaults.GetValueOrDefault(dominantError)
            ?? ErrorTypeDefaults["procedural"];
        var fallback = fallbackCandidates.FirstOrDefault(c => !triedMethods.Contains(c.Methodology));

        if (fallback != null)
        {
            _logger.LogInformation(
                "Methodology switch (fallback): {Error} → {Method} (no MCM entry, using defaults)",
                dominantError, fallback.Methodology);

            var recommended = Enum.TryParse<Students.Methodology>(fallback.Methodology, true, out var m)
                ? m : Students.Methodology.Socratic;

            return Task.FromResult(new DecideSwitchResponse(
                ShouldSwitch: true,
                RecommendedMethodology: recommended,
                Confidence: fallback.Confidence * 0.8,
                AllMethodologiesExhausted: false,
                EscalationAction: null,
                DecisionTrace: $"Fallback default: {fallback.Methodology} (no MCM entry for {dominantError}+{conceptCategory})"
            ));
        }

        // Truly exhausted — escalate
        return Task.FromResult(new DecideSwitchResponse(
            ShouldSwitch: false,
            RecommendedMethodology: request.CurrentMethodology,
            Confidence: 0,
            AllMethodologiesExhausted: true,
            EscalationAction: "Flag as mentor-resistant. Recommend human tutoring.",
            DecisionTrace: "All methodologies and fallbacks exhausted"
        ));
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

public record DecideSwitchRequest(
    string StudentId,
    string ConceptId,
    string? ConceptCategory,
    ErrorType DominantErrorType,
    Students.Methodology CurrentMethodology,
    List<string>? MethodAttemptHistory,
    double StagnationScore,
    int ConsecutiveStagnantSessions,
    MethodologyAssignment? CurrentAssignment = null,
    int SessionsSinceLastSwitch = int.MaxValue
);

public record DecideSwitchResponse(
    bool ShouldSwitch,
    Students.Methodology RecommendedMethodology,
    double Confidence,
    bool AllMethodologiesExhausted,
    string? EscalationAction,
    string DecisionTrace,
    bool DeferredByCooldown = false,
    int CooldownSessionsRemaining = 0,
    double CooldownHoursRemaining = 0
);

// ErrorType and Methodology enums are defined in Cena.Actors.Students namespace
