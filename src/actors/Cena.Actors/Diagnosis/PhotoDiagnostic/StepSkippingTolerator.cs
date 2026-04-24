// =============================================================================
// Cena Platform — StepSkippingTolerator (EPIC-PRR-J PRR-362, ADR-0002)
//
// Classifies a failed step transition as either "actually wrong" or
// "valid-but-unfollowable leap". Without this, every mid-chain failure
// gets flagged as Wrong — and the confidence-damaging UX consequence is
// false-wrong labels on work that just skipped legitimate intermediate
// steps (a common pattern in real student work: mental arithmetic,
// multi-step algebra collapsed to one line, chain-rule unrolling). Per
// memory "Labels match data" — don't call a correct step wrong.
//
// Layering on top of PRR-361 (Canonicalizer) + StepChainVerifier:
//   1. StepChainVerifier runs CasOperation.Equivalence on the canonical
//      forms of step_i and step_{i+1}. When that succeeds → Valid.
//   2. When it fails, the verifier hands the failed transition to this
//      tolerator, which returns one of:
//        • Wrong              — small, close algebraic error (most likely
//                               a genuine mistake the student made on
//                               THIS transition)
//        • UnfollowableSkip   — structurally dissimilar; student probably
//                               skipped intermediate work we can't verify
//                               from their photo. The UI surfaces
//                               "I couldn't follow between step X and
//                               step Y" instead of flagging the step as
//                               wrong.
//
// Heuristic (deterministic, no LLM, no extra SymPy round-trip):
//   • Canonical-token overlap ratio = |shared_tokens| / |tokens_in_smaller|.
//     Tokens are character n-grams of length 3 on the canonical form; the
//     canonical form comes from PRR-361 Canonicalizer so the cheap layer
//     has already absorbed surface noise.
//   • Length ratio = max(|A|,|B|) / min(|A|,|B|).
//   • Decision:
//       overlap < OverlapUnfollowableThreshold (default 0.30) OR
//       length ratio > LengthRatioUnfollowableThreshold (default 3.0)
//       ⇒ UnfollowableSkip.
//     Otherwise ⇒ Wrong.
//   • Both thresholds are options + exposed as constants so finance /
//     pedagogy can tune them without a code change. The defaults come
//     from a sampling of the first-week dispute corpus in the
//     PHOTO-UPLOAD-DIAGNOSTIC-001 persona review.
//
// The heuristic is intentionally crude + honest rather than clever. Per
// memory "Honest not complimentary": when we're uncertain, surface the
// uncertainty in the summary ("I couldn't follow…") rather than bluffing
// a confident "Wrong" or "OK" verdict.
// =============================================================================

using Cena.Actors.Cas;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>Knobs for <see cref="StepSkippingTolerator"/>.</summary>
public sealed record StepSkippingToleratorOptions(
    double OverlapUnfollowableThreshold = 0.30,
    double LengthRatioUnfollowableThreshold = 3.0)
{
    /// <summary>Default thresholds from the persona-engineering dispute corpus.</summary>
    public static readonly StepSkippingToleratorOptions Default = new();
}

/// <summary>Input to <see cref="IStepSkippingTolerator.Classify"/>.</summary>
/// <param name="FromCanonical">Step N after Canonicalizer normalisation.</param>
/// <param name="ToCanonical">Step N+1 after Canonicalizer normalisation.</param>
/// <param name="CasResult">The CAS router's verdict; must have Verified=false (caller already handled Valid).</param>
public sealed record StepSkippingContext(
    string FromCanonical,
    string ToCanonical,
    CasVerifyResult CasResult);

/// <summary>
/// Given a failed CAS equivalence, decide whether the student wrote
/// a genuinely wrong step or skipped intermediate work.
/// </summary>
public interface IStepSkippingTolerator
{
    /// <summary>
    /// Classify a failed transition. Caller has already ruled out Valid
    /// (by checking <see cref="CasVerifyResult.Verified"/>) and
    /// LowConfidence (upstream). This method returns exactly one of
    /// <see cref="StepTransitionOutcome.Wrong"/> or
    /// <see cref="StepTransitionOutcome.UnfollowableSkip"/>.
    /// </summary>
    StepTransitionOutcome Classify(StepSkippingContext context);
}

/// <summary>
/// Default tolerator: canonical-token overlap + length-ratio heuristic.
/// Deterministic; no I/O; trivially unit-testable.
/// </summary>
public sealed class StepSkippingTolerator : IStepSkippingTolerator
{
    /// <summary>Character n-gram size for the overlap score.</summary>
    public const int NGramSize = 3;

    private readonly StepSkippingToleratorOptions _options;

    /// <summary>Construct with tunable thresholds; defaults are sane.</summary>
    public StepSkippingTolerator(StepSkippingToleratorOptions? options = null)
    {
        _options = options ?? StepSkippingToleratorOptions.Default;
    }

    /// <inheritdoc />
    public StepTransitionOutcome Classify(StepSkippingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var a = context.FromCanonical ?? string.Empty;
        var b = context.ToCanonical ?? string.Empty;

        // Degenerate input: either side empty is always Wrong — there's
        // nothing to tell us intermediate steps were skipped.
        if (a.Length == 0 || b.Length == 0) return StepTransitionOutcome.Wrong;

        var lengthRatio = (double)Math.Max(a.Length, b.Length)
            / Math.Max(1, Math.Min(a.Length, b.Length));
        if (lengthRatio > _options.LengthRatioUnfollowableThreshold)
        {
            return StepTransitionOutcome.UnfollowableSkip;
        }

        var overlap = ComputeOverlapRatio(a, b);
        if (overlap < _options.OverlapUnfollowableThreshold)
        {
            return StepTransitionOutcome.UnfollowableSkip;
        }

        return StepTransitionOutcome.Wrong;
    }

    /// <summary>
    /// Pure helper: what fraction of the smaller side's character n-grams
    /// also appear on the larger side? Returns a value in [0, 1]. Small
    /// inputs (&lt; NGramSize chars) fall back to character-set overlap so
    /// very short forms don't always get classified as UnfollowableSkip.
    /// </summary>
    public static double ComputeOverlapRatio(string a, string b)
    {
        if (a is null || b is null) return 0.0;
        if (a.Length == 0 || b.Length == 0) return 0.0;

        var (small, large) = a.Length <= b.Length ? (a, b) : (b, a);

        // Very short strings: overlap on character set instead of n-grams.
        if (small.Length < NGramSize)
        {
            var smallSet = new HashSet<char>(small);
            if (smallSet.Count == 0) return 0.0;
            var largeSet = new HashSet<char>(large);
            var shared = smallSet.Count(c => largeSet.Contains(c));
            return (double)shared / smallSet.Count;
        }

        var smallNGrams = new HashSet<string>(EnumerateNGrams(small, NGramSize));
        if (smallNGrams.Count == 0) return 0.0;
        var largeNGrams = new HashSet<string>(EnumerateNGrams(large, NGramSize));
        var sharedCount = smallNGrams.Count(g => largeNGrams.Contains(g));
        return (double)sharedCount / smallNGrams.Count;
    }

    private static IEnumerable<string> EnumerateNGrams(string s, int n)
    {
        for (var i = 0; i + n <= s.Length; i++)
        {
            yield return s.Substring(i, n);
        }
    }
}
