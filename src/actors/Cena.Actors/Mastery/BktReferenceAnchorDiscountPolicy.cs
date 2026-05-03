// =============================================================================
// Cena Platform — BKT reference-anchor discount policy (PRR-261, ADR-0059 §15.9 R12)
//
// Sweller 1998 (DOI 10.1023/A:1022193728205) established the worked-example
// transient: when a learner attempts a question within minutes of seeing a
// worked solution to a structurally identical item, the attempt is more
// signal of "I just saw how" than of skill. Folding such attempts into BKT
// at full weight inflates the posterior, the scheduler reads false-mastery,
// and the ADR-0050 spacing benefit erodes.
//
// ADR-0059 §15.9 R12 fix: when an attempt arrives within
// <see cref="WindowSeconds"/> of a reference render, weight the BKT
// observation at <see cref="AnchoredFactor"/>. The pure-math primitive is
// <see cref="BktTracer.UpdateWithDiscount"/>; this policy decides which
// factor applies for a given attempt.
//
// Why a separate type:
//   - The numbers are an ADR-locked invariant. Hard-coding them inside the
//     tracker would make a future spec change spread across a hot path.
//   - Tests can swap in a stub (e.g. for sensitivity-analysis runs).
//   - Architecture-tests can pin the constants.
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Resolves the BKT discount factor for an attempt, given the elapsed time
/// since the most recent reference (worked-example) render for the same
/// source question. Pure / stateless / cheap — call on the hot path.
/// </summary>
public interface IBktReferenceAnchorDiscountPolicy
{
    /// <summary>
    /// Resolve the BKT observation weight to apply to this attempt.
    /// </summary>
    /// <param name="referenceAnchoredWithinSeconds">
    /// Elapsed seconds since the student last rendered a reference
    /// (worked-example) for the source question being attempted. <c>null</c>
    /// when no recent reference exists or the call site doesn't track timing.
    /// </param>
    /// <returns>
    /// 1.0 when the attempt is NOT reference-anchored (or the timing is
    /// outside the window); the anchored factor (default 0.5) when within
    /// the window. Always in [0, 1].
    /// </returns>
    float ResolveDiscountFactor(int? referenceAnchoredWithinSeconds);
}

/// <summary>
/// ADR-0059 §15.9 R12 implementation: 5-minute window, 0.5× weight inside.
/// </summary>
public sealed class BktReferenceAnchorDiscountPolicy : IBktReferenceAnchorDiscountPolicy
{
    /// <summary>
    /// Window inside which an attempt is considered reference-anchored.
    /// 300 seconds (5 minutes) per ADR-0059 §15.9 R12 / Sweller transient.
    /// </summary>
    public const int WindowSeconds = 300;

    /// <summary>
    /// Discount factor applied to anchored attempts. 0.5× per §15.9 R12.
    /// Observation is folded into BKT at half its standard weight.
    /// </summary>
    public const float AnchoredFactor = 0.5f;

    /// <summary>
    /// Discount factor applied to non-anchored attempts. 1.0× — identical
    /// to the standard BKT update; <see cref="BktTracer.UpdateWithDiscount"/>
    /// at 1.0 is bit-identical to <see cref="BktTracer.Update"/>.
    /// </summary>
    public const float UnanchoredFactor = 1.0f;

    /// <summary>Default singleton — stateless, safe to share.</summary>
    public static readonly BktReferenceAnchorDiscountPolicy Default = new();

    /// <inheritdoc/>
    public float ResolveDiscountFactor(int? referenceAnchoredWithinSeconds) =>
        referenceAnchoredWithinSeconds is { } seconds && seconds >= 0 && seconds <= WindowSeconds
            ? AnchoredFactor
            : UnanchoredFactor;
}
