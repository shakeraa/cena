// =============================================================================
// Cena Platform -- Stagnation Insights Service
// Analyzes causal factors behind student stagnation: difficulty mismatch,
// focus degradation, prerequisite gaps, methodology ineffectiveness.
// Queries the event stream for ConceptAttempted_V1 events with difficulty data.
// =============================================================================

using Cena.Actors.Events;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api;

public interface IStagnationInsightsService
{
    Task<StagnationInsightsResponse> GetInsightsAsync(string studentId, string conceptId);
    Task<StagnationTimelineResponse> GetTimelineAsync(string studentId, string conceptId, int limit = 50);
}

// ── Response DTOs ──

public sealed record StagnationInsightsResponse(
    string StudentId,
    string ConceptId,
    StagnationCausalFactors Factors,
    IReadOnlyList<StagnationRecommendation> Recommendations,
    AttemptSummary Summary);

public sealed record StagnationCausalFactors(
    float DifficultyMismatchScore,      // 0-1: how much difficulty mismatch contributed
    float FocusDegradationScore,        // 0-1: how much focus issues contributed
    float PrerequisiteGapScore,         // 0-1: how much prerequisite weakness contributed
    float MethodologyIneffectivenessScore, // 0-1: how much the teaching method contributed
    float ErrorRepetitionScore,         // 0-1: how much repeated same-type errors contributed
    string PrimaryFactor,               // "difficulty_mismatch", "focus", "prerequisites", "methodology", "error_repetition"
    string Explanation);

public sealed record StagnationRecommendation(
    string Action,                      // "reduce_difficulty", "switch_methodology", "review_prerequisites", "suggest_break", "investigate_errors"
    string Reason,
    float Confidence);

public sealed record AttemptSummary(
    int TotalAttempts,
    int CorrectCount,
    float AccuracyRate,
    int StretchAttempts,
    int RegressionAttempts,
    int FocusDegradedAttempts,
    float AvgDifficultyGap,
    float AvgResponseTimeMs,
    IReadOnlyList<string> MethodologiesUsed,
    IReadOnlyList<string> ErrorTypes);

public sealed record StagnationTimelineResponse(
    string StudentId,
    string ConceptId,
    IReadOnlyList<AttemptTimelineEntry> Timeline);

public sealed record AttemptTimelineEntry(
    DateTimeOffset Timestamp,
    string QuestionId,
    bool IsCorrect,
    float PriorMastery,
    float PosteriorMastery,
    float QuestionDifficulty,
    float DifficultyGap,
    string? DifficultyFrame,
    string? FocusState,
    string Methodology,
    string ErrorType,
    int ResponseTimeMs,
    int HintsUsed);

// ── Service Implementation ──

public sealed class StagnationInsightsService : IStagnationInsightsService
{
    private readonly IDocumentStore _store;
    private readonly ILogger<StagnationInsightsService> _logger;

    public StagnationInsightsService(
        IDocumentStore store,
        ILogger<StagnationInsightsService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<StagnationInsightsResponse> GetInsightsAsync(string studentId, string conceptId)
    {
        var attempts = await LoadAttempts(studentId, conceptId);

        if (attempts.Count == 0)
        {
            return new StagnationInsightsResponse(
                studentId, conceptId,
                new StagnationCausalFactors(0, 0, 0, 0, 0, "none", "No attempts found"),
                new List<StagnationRecommendation>(),
                EmptySummary());
        }

        // ── Analyze causal factors ──
        var difficultyScore = AnalyzeDifficultyMismatch(attempts);
        var focusScore = AnalyzeFocusDegradation(attempts);
        var prereqScore = AnalyzePrerequisiteGaps(attempts);
        var methodologyScore = AnalyzeMethodologyIneffectiveness(attempts);
        var errorRepScore = AnalyzeErrorRepetition(attempts);

        // Determine primary factor
        var factors = new (string name, float score)[]
        {
            ("difficulty_mismatch", difficultyScore),
            ("focus", focusScore),
            ("prerequisites", prereqScore),
            ("methodology", methodologyScore),
            ("error_repetition", errorRepScore)
        };
        var primary = factors.OrderByDescending(f => f.score).First();

        var explanation = primary.name switch
        {
            "difficulty_mismatch" => $"Student is consistently receiving questions outside their ZPD. " +
                $"{attempts.Count(a => a.DifficultyGap > 0.3f)} of {attempts.Count} attempts were stretch questions.",
            "focus" => $"Focus degradation is a significant factor. " +
                $"{attempts.Count(a => a.FocusState is "Declining" or "Degrading" or "Critical")} of {attempts.Count} attempts occurred during poor focus.",
            "prerequisites" => "Prior mastery on this concept's prerequisites appears insufficient. " +
                "The student may need to revisit foundational topics.",
            "methodology" => $"The current methodology may not be effective. " +
                $"{attempts.Select(a => a.MethodologyActive).Distinct().Count()} different approaches have been tried.",
            "error_repetition" => $"The student is repeating the same error type. " +
                $"Dominant error: {GetDominantErrorType(attempts)}.",
            _ => "Multiple factors may be contributing."
        };

        var causal = new StagnationCausalFactors(
            difficultyScore, focusScore, prereqScore, methodologyScore, errorRepScore,
            primary.name, explanation);

        // ── Generate recommendations ──
        var recommendations = GenerateRecommendations(causal, attempts);

        // ── Build summary ──
        var summary = BuildSummary(attempts);

        return new StagnationInsightsResponse(studentId, conceptId, causal, recommendations, summary);
    }

    public async Task<StagnationTimelineResponse> GetTimelineAsync(string studentId, string conceptId, int limit = 50)
    {
        var attempts = await LoadAttempts(studentId, conceptId);

        var timeline = attempts
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .Select(a => new AttemptTimelineEntry(
                a.Timestamp, a.QuestionId, a.IsCorrect,
                (float)a.PriorMastery, (float)a.PosteriorMastery,
                a.QuestionDifficulty, a.DifficultyGap,
                a.DifficultyFrame, a.FocusState,
                a.MethodologyActive, a.ErrorType,
                a.ResponseTimeMs, a.HintCountUsed))
            .ToList();

        return new StagnationTimelineResponse(studentId, conceptId, timeline);
    }

    // ── Private Analysis Methods ──

    private async Task<List<ConceptAttempted_V1>> LoadAttempts(string studentId, string conceptId)
    {
        await using var session = _store.QuerySession();

        var events = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.StreamKey == studentId && e.EventTypeName == "ConceptAttempted_V1")
            .OrderByDescending(e => e.Timestamp)
            .Take(200)
            .ToListAsync();

        return events
            .Select(e => e.Data)
            .OfType<ConceptAttempted_V1>()
            .Where(a => a.ConceptId == conceptId)
            .ToList();
    }

    /// <summary>
    /// High score = student is getting questions that are too hard or too easy.
    /// </summary>
    private static float AnalyzeDifficultyMismatch(List<ConceptAttempted_V1> attempts)
    {
        var withDifficulty = attempts.Where(a => a.QuestionDifficulty > 0).ToList();
        if (withDifficulty.Count == 0) return 0f;

        var stretchCount = withDifficulty.Count(a => a.DifficultyGap > 0.25f);
        var regressionCount = withDifficulty.Count(a => a.DifficultyGap < -0.25f);
        var mismatchRate = (float)(stretchCount + regressionCount) / withDifficulty.Count;

        // Also check: are wrong answers correlated with high difficulty gap?
        var wrongWithHighGap = withDifficulty.Count(a => !a.IsCorrect && a.DifficultyGap > 0.2f);
        var wrongTotal = withDifficulty.Count(a => !a.IsCorrect);
        var gapCorrelation = wrongTotal > 0 ? (float)wrongWithHighGap / wrongTotal : 0f;

        return Math.Min(1f, mismatchRate * 0.6f + gapCorrelation * 0.4f);
    }

    /// <summary>
    /// High score = many attempts occurred during declining/degrading/critical focus.
    /// </summary>
    private static float AnalyzeFocusDegradation(List<ConceptAttempted_V1> attempts)
    {
        var withFocus = attempts.Where(a => a.FocusState is not null).ToList();
        if (withFocus.Count == 0) return 0f;

        var degradedCount = withFocus.Count(a =>
            a.FocusState is "Declining" or "Degrading" or "Critical");

        // Weight critical more heavily
        var criticalCount = withFocus.Count(a => a.FocusState == "Critical");
        var weightedScore = (float)(degradedCount + criticalCount) / withFocus.Count;

        // Check: are wrong answers correlated with poor focus?
        var wrongInDegraded = withFocus.Count(a => !a.IsCorrect &&
            a.FocusState is "Declining" or "Degrading" or "Critical");
        var wrongTotal = withFocus.Count(a => !a.IsCorrect);
        var focusCorrelation = wrongTotal > 0 ? (float)wrongInDegraded / wrongTotal : 0f;

        return Math.Min(1f, weightedScore * 0.5f + focusCorrelation * 0.5f);
    }

    /// <summary>
    /// High score = mastery isn't improving (plateau) and prior mastery is low.
    /// </summary>
    private static float AnalyzePrerequisiteGaps(List<ConceptAttempted_V1> attempts)
    {
        if (attempts.Count < 5) return 0f;

        var recent = attempts.Take(10).ToList();
        var avgMastery = recent.Average(a => a.PosteriorMastery);
        var masteryVariance = recent.Select(a => a.PosteriorMastery).Max() -
                              recent.Select(a => a.PosteriorMastery).Min();

        // Low mastery + low variance = stuck at a low level (likely prerequisite gap)
        if (avgMastery < 0.3 && masteryVariance < 0.05)
            return 0.9f;
        if (avgMastery < 0.5 && masteryVariance < 0.1)
            return 0.6f;

        return Math.Max(0f, 0.3f - (float)avgMastery) * 2f;
    }

    /// <summary>
    /// High score = multiple methodologies have been tried without improvement.
    /// </summary>
    private static float AnalyzeMethodologyIneffectiveness(List<ConceptAttempted_V1> attempts)
    {
        var methodologies = attempts.Select(a => a.MethodologyActive).Distinct().ToList();
        if (methodologies.Count <= 1) return 0.2f; // Only one methodology tried — can't diagnose yet

        // Multiple methodologies tried and still stagnating = methodology isn't the differentiator
        // But if accuracy isn't improving across methodology switches, score is high
        var accuracyByMethod = attempts
            .GroupBy(a => a.MethodologyActive)
            .ToDictionary(g => g.Key, g => g.Count(a => a.IsCorrect) / (float)g.Count());

        var maxAccuracy = accuracyByMethod.Values.Max();
        var minAccuracy = accuracyByMethod.Values.Min();
        var spread = maxAccuracy - minAccuracy;

        // If all methodologies have similar (low) accuracy, methodology isn't helping
        if (spread < 0.15f && maxAccuracy < 0.6f)
            return 0.8f;

        return Math.Max(0f, 0.5f - spread);
    }

    /// <summary>
    /// High score = same error type keeps repeating.
    /// </summary>
    private static float AnalyzeErrorRepetition(List<ConceptAttempted_V1> attempts)
    {
        var errors = attempts
            .Where(a => !a.IsCorrect && !string.IsNullOrEmpty(a.ErrorType) && a.ErrorType != "None")
            .Select(a => a.ErrorType)
            .ToList();

        if (errors.Count < 3) return 0f;

        var dominant = errors.GroupBy(e => e).OrderByDescending(g => g.Count()).First();
        var dominantRate = (float)dominant.Count() / errors.Count;

        // If > 60% of errors are the same type, this is a strong signal
        return dominantRate > 0.6f ? 0.9f : dominantRate;
    }

    private static string GetDominantErrorType(List<ConceptAttempted_V1> attempts)
    {
        return attempts
            .Where(a => !a.IsCorrect && !string.IsNullOrEmpty(a.ErrorType) && a.ErrorType != "None")
            .GroupBy(a => a.ErrorType)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault() ?? "unknown";
    }

    private static List<StagnationRecommendation> GenerateRecommendations(
        StagnationCausalFactors factors, List<ConceptAttempted_V1> attempts)
    {
        var recs = new List<StagnationRecommendation>();

        if (factors.DifficultyMismatchScore > 0.5f)
            recs.Add(new("reduce_difficulty",
                "Questions are consistently outside the student's ZPD. Recommend narrowing difficulty range.",
                factors.DifficultyMismatchScore));

        if (factors.FocusDegradationScore > 0.5f)
            recs.Add(new("suggest_break",
                "Many errors correlate with poor focus. Recommend shorter sessions with more breaks.",
                factors.FocusDegradationScore));

        if (factors.PrerequisiteGapScore > 0.5f)
            recs.Add(new("review_prerequisites",
                "Mastery plateau at low level suggests prerequisite gaps. Recommend reviewing foundational concepts.",
                factors.PrerequisiteGapScore));

        if (factors.MethodologyIneffectivenessScore > 0.5f)
            recs.Add(new("switch_methodology",
                "Current teaching approach isn't producing improvement. Recommend trying a different methodology.",
                factors.MethodologyIneffectivenessScore));

        if (factors.ErrorRepetitionScore > 0.5f)
            recs.Add(new("investigate_errors",
                $"Student keeps making the same type of error ({GetDominantErrorType(attempts)}). Targeted remediation needed.",
                factors.ErrorRepetitionScore));

        return recs.OrderByDescending(r => r.Confidence).ToList();
    }

    private static AttemptSummary BuildSummary(List<ConceptAttempted_V1> attempts)
    {
        var correct = attempts.Count(a => a.IsCorrect);
        return new AttemptSummary(
            TotalAttempts: attempts.Count,
            CorrectCount: correct,
            AccuracyRate: attempts.Count > 0 ? (float)correct / attempts.Count : 0f,
            StretchAttempts: attempts.Count(a => a.DifficultyGap > 0.25f),
            RegressionAttempts: attempts.Count(a => a.DifficultyGap < -0.25f),
            FocusDegradedAttempts: attempts.Count(a =>
                a.FocusState is "Declining" or "Degrading" or "Critical"),
            AvgDifficultyGap: attempts.Where(a => a.QuestionDifficulty > 0).Select(a => a.DifficultyGap).DefaultIfEmpty(0).Average(),
            AvgResponseTimeMs: (float)attempts.Average(a => a.ResponseTimeMs),
            MethodologiesUsed: attempts.Select(a => a.MethodologyActive).Distinct().ToList(),
            ErrorTypes: attempts
                .Where(a => !string.IsNullOrEmpty(a.ErrorType) && a.ErrorType != "None")
                .Select(a => a.ErrorType).Distinct().ToList());
    }

    private static AttemptSummary EmptySummary() => new(0, 0, 0, 0, 0, 0, 0, 0,
        new List<string>(), new List<string>());
}
