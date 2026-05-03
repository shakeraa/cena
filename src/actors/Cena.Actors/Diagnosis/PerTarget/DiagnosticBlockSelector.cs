// =============================================================================
// Cena Platform — DiagnosticBlockSelector (prr-228)
//
// Easy-first adaptive stop for a per-target diagnostic block.
//
// BKT parameter policy: the running posterior uses the Koedinger defaults
// locked by ADR-0039 via BktParameters.P{Init,Learn,Slip,Guess}. Any
// deviation from those values requires a new ADR — the
// BktParametersLockedTest enforces.
//
// Rules (locked by the 2026-04-21 task-body tightening):
//   1. HARD CAP: 6-8 items per target — floor 6, ceiling 8.
//   2. EASY FIRST: the first item in a block is always from the easy band.
//   3. ADAPTIVE STOP: stop as early as 4 items if BKT posterior has
//      converged — max-|posterior - 0.5| > ConvergenceBand, i.e. the
//      student's mastery estimate is clearly above OR below the band.
//   4. SKIPS DON'T PENALISE: skip counts toward the cap but the BKT update
//      treats a skip as "no observation" (prior unchanged for that step).
//
// The selector is pure logic — it takes responses in, returns an
// <see cref="AdaptiveStopDecision"/>. The selector does NOT own the item
// pool; callers pass the next best item. See DiagnosticBlockEngine for
// the end-to-end orchestration.
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Diagnosis.PerTarget;

/// <summary>
/// Hard thresholds for the 2026-04-21 tightening.
/// </summary>
public static class DiagnosticBlockThresholds
{
    /// <summary>Minimum items per block before adaptive stop may fire.</summary>
    public const int MinItemsBeforeStop = 4;

    /// <summary>Floor cap — block always serves at least this many items
    /// unless converged via adaptive stop.</summary>
    public const int FloorCap = 6;

    /// <summary>Ceiling cap — block NEVER serves more than this many
    /// items per target, regardless of convergence.</summary>
    public const int CeilingCap = 8;

    /// <summary>Convergence band. If |posterior - 0.5| exceeds this value,
    /// the BKT estimate is confident enough to stop. 0.25 corresponds to a
    /// posterior of either &lt; 0.25 (clearly below) or &gt; 0.75 (clearly
    /// above).</summary>
    public const double ConvergenceBand = 0.25;
}

/// <summary>
/// The decision returned by <see cref="DiagnosticBlockSelector"/>.
/// </summary>
public enum AdaptiveStopDecision
{
    /// <summary>Keep serving items; block is not converged and under cap.</summary>
    Continue = 0,

    /// <summary>BKT posterior is confident — stop early.</summary>
    StopConverged = 1,

    /// <summary>Hit the ceiling cap — stop regardless of convergence.</summary>
    StopCeiling = 2,
}

/// <summary>
/// Pure-logic easy-first adaptive-stop decision maker for per-target
/// diagnostic blocks.
/// </summary>
public static class DiagnosticBlockSelector
{
    /// <summary>
    /// Decide whether to stop the block given the running BKT posterior
    /// and the response count so far.
    /// </summary>
    /// <param name="responsesServed">Items served so far (answered +
    /// skipped). Skips count toward the cap (ADR-0050 cold-start safety
    /// net: a student who skips every item still finishes at the floor
    /// cap, producing an honest "cold-start" prior).</param>
    /// <param name="bktPosterior">Current BKT posterior on the target's
    /// dominant skill, in [0, 1]. Pass 0.5 when there is no signal yet
    /// (e.g. all responses so far were skips).</param>
    /// <returns>
    /// <see cref="AdaptiveStopDecision.StopCeiling"/> at ceiling cap,
    /// <see cref="AdaptiveStopDecision.StopConverged"/> once min-items +
    /// convergence are both satisfied, otherwise
    /// <see cref="AdaptiveStopDecision.Continue"/>.
    /// </returns>
    public static AdaptiveStopDecision Decide(
        int responsesServed,
        double bktPosterior)
    {
        if (responsesServed < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(responsesServed),
                "must be >= 0.");
        }

        if (bktPosterior < 0 || bktPosterior > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bktPosterior),
                $"must be in [0, 1], got {bktPosterior}.");
        }

        if (responsesServed >= DiagnosticBlockThresholds.CeilingCap)
        {
            return AdaptiveStopDecision.StopCeiling;
        }

        if (responsesServed < DiagnosticBlockThresholds.MinItemsBeforeStop)
        {
            return AdaptiveStopDecision.Continue;
        }

        // We only *consider* stopping once we've hit the floor cap OR
        // we're above min-items AND converged. The floor cap is the honest
        // default for the "struggling / skipping every item" case.
        var confident = Math.Abs(bktPosterior - 0.5)
            >= DiagnosticBlockThresholds.ConvergenceBand;

        if (confident && responsesServed >= DiagnosticBlockThresholds.MinItemsBeforeStop)
        {
            return AdaptiveStopDecision.StopConverged;
        }

        if (responsesServed >= DiagnosticBlockThresholds.FloorCap && confident)
        {
            return AdaptiveStopDecision.StopConverged;
        }

        return AdaptiveStopDecision.Continue;
    }

    /// <summary>
    /// Simple BKT-style posterior update given the response. Used by tests
    /// and by the engine; the real BKT tracker lives in Mastery/ and is
    /// the authoritative update path for production mastery state — this
    /// helper is for the in-block adaptive-stop signal only, where we
    /// need a cheap running estimate without committing to a mastery row
    /// mid-block.
    ///
    /// Parameters chosen deliberately close to the BktParameters defaults
    /// (pLearn=0.15, pSlip=0.10, pGuess=0.25) so that the adaptive-stop
    /// band tracks what the actual BKT tracker will produce once the block
    /// completes and the engine emits mastery rows.
    /// </summary>
    public static double UpdatePosterior(
        double prior,
        DiagnosticBlockResponse response)
    {
        // Skip ⇒ no observation ⇒ posterior unchanged. Explicit clamp
        // guards against drift from float noise in earlier steps.
        if (response.Action == DiagnosticResponseAction.Skipped)
        {
            return Math.Clamp(prior, 0.0, 1.0);
        }

        // Route through the ADR-0039 Koedinger locked defaults. The
        // in-block running estimate MUST use the same canonical constants
        // the authoritative BKT tracker uses so the adaptive-stop band
        // tracks what the tracker will produce after the block.
        var pSlip = BktParameters.PSlip;
        var pGuess = BktParameters.PGuess;
        var pLearn = BktParameters.PLearn;

        double evidencePosterior;
        if (response.Correct)
        {
            var numerator = prior * (1 - pSlip);
            var denominator = numerator + (1 - prior) * pGuess;
            evidencePosterior = denominator > 0 ? numerator / denominator : prior;
        }
        else
        {
            var numerator = prior * pSlip;
            var denominator = numerator + (1 - prior) * (1 - pGuess);
            evidencePosterior = denominator > 0 ? numerator / denominator : prior;
        }

        // Apply learn step: P(known after evidence) += pLearn * P(unknown).
        var posterior = evidencePosterior + (1 - evidencePosterior) * pLearn;
        return Math.Clamp(posterior, 0.0, 1.0);
    }
}
