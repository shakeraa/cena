// =============================================================================
// Cena Platform — BKT+ Calculator (BKT-PLUS-001)
//
// Three extensions per Dr. Rami Khalil adversarial review (improvement #44):
//   1. Ebbinghaus forgetting curve: PLEffective = PL × 2^(-days/halfLife)
//   2. Skill prerequisite DAG gating
//   3. Assistance-weighted learning rate
//
// Replaces the simple +0.05 stub. Builds on BktService (Corbett & Anderson 1994)
// and HintAdjustedBktService (SAI-02).
// =============================================================================

using System.Runtime.CompilerServices;

namespace Cena.Actors.Services;

/// <summary>
/// Extended mastery state for a single skill, including forgetting curve data.
/// </summary>
public record SkillMasteryState(
    string SkillId,
    double PLearned,
    DateTimeOffset LastPracticedAt,
    double HalfLifeDays,
    int TotalAttempts,
    int CorrectAttempts
)
{
    /// <summary>Default half-life for Ebbinghaus forgetting curve (14 days).</summary>
    public const double DefaultHalfLifeDays = 14.0;

    /// <summary>PP-010: Category-specific default half-life (days).</summary>
    public static double DefaultHalfLifeForCategory(SkillCategory category) => category switch
    {
        SkillCategory.Procedural => 7.0,
        SkillCategory.Conceptual => 21.0,
        SkillCategory.MetaCognitive => 30.0,
        _ => DefaultHalfLifeDays
    };

    /// <summary>Threshold below which a previously-mastered skill triggers a refresh recommendation.</summary>
    public const double RefreshThreshold = 0.40;

    /// <summary>Mastery level at which a skill is considered "learned".</summary>
    public const double MasteredThreshold = 0.80;

    /// <summary>Prerequisite gate: downstream skills blocked until prerequisites reach this level.</summary>
    public const double PrerequisiteGateThreshold = 0.60;
}

/// <summary>
/// PP-010: Skill category determines default forgetting curve half-life.
/// Research: Bahrick &amp; Hall 1991, Cepeda et al. 2006.
/// </summary>
public enum SkillCategory
{
    /// <summary>Rote/mechanical skills (factoring, trig identities). Half-life: 7 days.</summary>
    Procedural,

    /// <summary>Understanding-based skills (what a function is, graph interpretation). Half-life: 21 days.</summary>
    Conceptual,

    /// <summary>Strategy skills (knowing which technique to apply). Half-life: 30 days.</summary>
    MetaCognitive,

    /// <summary>Mixed or uncategorized. Half-life: 14 days (default).</summary>
    Mixed
}

/// <summary>
/// Assistance level for BKT+ weighted updates.
/// Higher assistance = less mastery credit.
/// </summary>
public enum AssistanceLevel
{
    /// <summary>Student solved with no help.</summary>
    Solo = 0,

    /// <summary>One hint used.</summary>
    OneHint = 1,

    /// <summary>Two hints used.</summary>
    TwoHints = 2,

    /// <summary>Step was auto-filled or answer revealed.</summary>
    AutoFilled = 3
}

/// <summary>
/// Input for a BKT+ update that includes forgetting and assistance context.
/// </summary>
public readonly record struct BktPlusInput(
    SkillMasteryState CurrentState,
    bool IsCorrect,
    AssistanceLevel Assistance,
    DateTimeOffset AttemptTime,
    BktParameters Parameters
);

/// <summary>
/// Result of a BKT+ update with effective mastery and refresh signals.
/// </summary>
public readonly record struct BktPlusResult(
    double PosteriorLearned,
    double EffectiveMastery,
    double NewHalfLifeDays,
    bool NeedsRefresh,
    bool CrossedProgressionThreshold,
    bool MeetsPrerequisiteGate
);

public interface IBktPlusCalculator
{
    /// <summary>
    /// Compute effective mastery accounting for time-based forgetting.
    /// PLEffective = PL × 2^(-daysSince / halfLife)
    /// </summary>
    double ComputeEffectiveMastery(SkillMasteryState state, DateTimeOffset now);

    /// <summary>
    /// Full BKT+ update: forgetting + Bayes update + assistance weighting.
    /// </summary>
    BktPlusResult Update(in BktPlusInput input);

    /// <summary>
    /// Check if all prerequisites for a skill meet the gate threshold.
    /// </summary>
    bool AllPrerequisitesMet(
        string skillId,
        IReadOnlyDictionary<string, SkillMasteryState> masteryMap,
        ISkillPrerequisiteGraph graph,
        DateTimeOffset now);
}

/// <summary>
/// BKT+ calculator implementing forgetting, prerequisites, and assistance weighting.
/// </summary>
public sealed class BktPlusCalculator : IBktPlusCalculator
{
    private readonly IBktService _bktService;

    // Assistance credit multipliers: solo=1.0, 1 hint=0.75, 2 hints=0.50, auto-filled=0.05
    // PP-006: AutoFilled reduced from 0.25 to 0.05 per Heffernan & Heffernan (2014)
    // — auto-filled answers involve no cognitive work; 0.25 inflated mastery for
    // students who game the hint ladder. 0.05 barely moves the needle, as intended.
    private static readonly double[] AssistanceCreditMultipliers = [1.0, 0.75, 0.50, 0.05];

    // Half-life adjustment: good practice increases half-life, errors decrease it
    private const double HalfLifeGrowthFactor = 1.1;   // +10% on correct
    private const double HalfLifeShrinkFactor = 0.9;    // -10% on incorrect
    private const double MinHalfLifeDays = 1.0;
    private const double MaxHalfLifeDays = 180.0;

    public BktPlusCalculator(IBktService bktService)
    {
        _bktService = bktService;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ComputeEffectiveMastery(SkillMasteryState state, DateTimeOffset now)
    {
        var daysSince = (now - state.LastPracticedAt).TotalDays;
        if (daysSince <= 0) return state.PLearned;

        // Ebbinghaus: PLEffective = PL × 2^(-days / halfLife)
        var decay = Math.Pow(2.0, -daysSince / state.HalfLifeDays);
        return state.PLearned * decay;
    }

    public BktPlusResult Update(in BktPlusInput input)
    {
        // Step 1: Compute effective mastery (with forgetting) as the prior
        var effectivePrior = ComputeEffectiveMastery(input.CurrentState, input.AttemptTime);

        // Step 2: Apply assistance-weighted learning rate
        // P(T) is scaled down by assistance: P(T) × (1 - 0.25 × assistanceLevel)
        var assistanceIndex = Math.Min((int)input.Assistance, AssistanceCreditMultipliers.Length - 1);
        var creditMultiplier = AssistanceCreditMultipliers[assistanceIndex];

        var adjustedParams = input.Parameters with
        {
            PLearning = input.Parameters.PLearning * creditMultiplier
        };

        // Step 3: Run standard BKT update with adjusted parameters
        var bktInput = new BktUpdateInput(
            PriorMastery: effectivePrior,
            IsCorrect: input.IsCorrect,
            Parameters: adjustedParams
        );
        var bktResult = _bktService.Update(bktInput);

        // Step 4: Adjust half-life based on outcome
        var newHalfLife = input.IsCorrect
            ? input.CurrentState.HalfLifeDays * HalfLifeGrowthFactor
            : input.CurrentState.HalfLifeDays * HalfLifeShrinkFactor;
        newHalfLife = Math.Clamp(newHalfLife, MinHalfLifeDays, MaxHalfLifeDays);

        // Step 5: Compute effective mastery for the new posterior
        // (effective = posterior since we just practiced, so no decay yet)
        var posteriorLearned = bktResult.PosteriorMastery;
        var needsRefresh = posteriorLearned >= SkillMasteryState.MasteredThreshold
            && effectivePrior < SkillMasteryState.RefreshThreshold;

        return new BktPlusResult(
            PosteriorLearned: posteriorLearned,
            EffectiveMastery: posteriorLearned, // just practiced, no decay
            NewHalfLifeDays: newHalfLife,
            NeedsRefresh: needsRefresh,
            CrossedProgressionThreshold: bktResult.CrossedProgressionThreshold,
            MeetsPrerequisiteGate: bktResult.MeetsPrerequisiteGate
        );
    }

    public bool AllPrerequisitesMet(
        string skillId,
        IReadOnlyDictionary<string, SkillMasteryState> masteryMap,
        ISkillPrerequisiteGraph graph,
        DateTimeOffset now)
    {
        var prerequisites = graph.GetPrerequisites(skillId);
        if (prerequisites.Count == 0) return true;

        foreach (var prereqId in prerequisites)
        {
            if (!masteryMap.TryGetValue(prereqId, out var prereqState))
                return false; // never attempted = not met

            var effective = ComputeEffectiveMastery(prereqState, now);
            if (effective < SkillMasteryState.PrerequisiteGateThreshold)
                return false;
        }

        return true;
    }
}
