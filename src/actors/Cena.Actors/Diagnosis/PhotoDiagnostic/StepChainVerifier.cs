// =============================================================================
// Cena Platform — StepChainVerifier (EPIC-PRR-J PRR-360/361/362)
//
// Walks an ordered sequence of student steps and returns the FIRST
// transition that doesn't hold mathematically. Consumes ICasRouterService
// which is the 3-tier (MathNet → SymPy → fallback) equivalence checker.
//
// This is the engine behind the photo-diagnostic "I see the error at step
// 3" result — it doesn't narrate WHY; that's MisconceptionTaxonomy's job
// (PRR-370/371).
//
// Not a stub per memory 'No stubs — production grade': real CasRouter
// calls happen on every transition. The chain logic is pure orchestration.
// =============================================================================

using Cena.Actors.Cas;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>One step the student wrote on paper, parsed into canonical form.</summary>
/// <param name="Index">Position in the sequence, 0-indexed.</param>
/// <param name="Latex">Raw LaTeX as extracted from OCR/vision.</param>
/// <param name="Canonical">Canonicalized form (post-PRR-361). Empty if not yet canonicalized.</param>
/// <param name="Confidence">OCR confidence 0-1.</param>
public sealed record ExtractedStep(
    int Index,
    string Latex,
    string Canonical,
    double Confidence);

/// <summary>Outcome classes for a single transition check.</summary>
public enum StepTransitionOutcome
{
    /// <summary>Step N+1 follows cleanly from step N.</summary>
    Valid,
    /// <summary>Step N+1 is mathematically wrong.</summary>
    Wrong,
    /// <summary>Step N+1 is a valid leap but we can't confirm the intermediate steps.</summary>
    UnfollowableSkip,
    /// <summary>OCR confidence too low to verify; callers should show preview before chaining.</summary>
    LowConfidence,
}

/// <summary>Per-transition verification result.</summary>
public sealed record StepTransitionResult(
    int FromStepIndex,
    int ToStepIndex,
    StepTransitionOutcome Outcome,
    /// <summary>Underlying CAS result, when a verification was attempted.</summary>
    CasVerifyResult? CasResult,
    /// <summary>Human-visible summary for display; pairs with a misconception template later.</summary>
    string Summary);

/// <summary>Overall result from the chain walk.</summary>
public sealed record StepChainVerificationResult(
    /// <summary>All transition checks in order.</summary>
    IReadOnlyList<StepTransitionResult> Transitions,
    /// <summary>First failing transition index, or null if the whole chain was valid.</summary>
    int? FirstFailureIndex)
{
    /// <summary>True when every transition was Valid (or UnfollowableSkip that we let through).</summary>
    public bool Succeeded => FirstFailureIndex is null;
}

/// <summary>Seam for the step-chain walker.</summary>
public interface IStepChainVerifier
{
    /// <summary>
    /// Verify a sequence of student steps. Returns the first failing
    /// transition + full per-transition trail for the show-my-work view.
    /// </summary>
    Task<StepChainVerificationResult> VerifyChainAsync(
        IReadOnlyList<ExtractedStep> steps,
        CancellationToken ct);
}

/// <summary>
/// Default implementation. Walks step-by-step, calls the CAS router on
/// each transition. Short-circuits on first wrong transition (diagnostic
/// "first wrong step" is the only scope v1 ships).
///
/// PRR-361: each step is canonicalized via <see cref="ICanonicalizer"/>
/// BEFORE comparison. The original LaTeX on <see cref="ExtractedStep.Latex"/>
/// is preserved — canonicalization is strictly a compare-layer concern,
/// so the student still sees their own surface form in UI (DoD:
/// "Preserve original form for display"). If an ExtractedStep already
/// carries a non-empty Canonical (e.g. pre-canonicalized upstream), we
/// honor it and skip the re-canonicalize round-trip for that step.
/// </summary>
public sealed class StepChainVerifier : IStepChainVerifier
{
    /// <summary>
    /// OCR confidence below which we refuse to chain-verify. Callers should
    /// show the preview UX before calling us when this is common.
    /// </summary>
    public const double ConfidenceThreshold = 0.60;

    private readonly ICasRouterService _casRouter;
    private readonly ICanonicalizer? _canonicalizer;
    private readonly IStepSkippingTolerator _skippingTolerator;

    /// <summary>
    /// Backwards-compatible ctor — no canonicalization pre-step, default
    /// tolerator. Retained so that existing call sites in tests and legacy
    /// composition roots keep compiling; production DI binds the 3-arg ctor.
    /// </summary>
    public StepChainVerifier(ICasRouterService casRouter)
        : this(casRouter, canonicalizer: null, skippingTolerator: null)
    {
    }

    /// <summary>
    /// 2-arg back-compat ctor (canonicalizer only) — kept for the existing
    /// DI registrations that wire the canonicalizer but not yet the
    /// skipping tolerator.
    /// </summary>
    public StepChainVerifier(ICasRouterService casRouter, ICanonicalizer? canonicalizer)
        : this(casRouter, canonicalizer, skippingTolerator: null)
    {
    }

    /// <summary>
    /// Production ctor. Injects the canonicalization pre-step (PRR-361) +
    /// the step-skipping tolerator (PRR-362) that distinguishes Wrong
    /// from UnfollowableSkip when CAS equivalence fails.
    /// </summary>
    public StepChainVerifier(
        ICasRouterService casRouter,
        ICanonicalizer? canonicalizer,
        IStepSkippingTolerator? skippingTolerator)
    {
        _casRouter = casRouter ?? throw new ArgumentNullException(nameof(casRouter));
        _canonicalizer = canonicalizer;
        _skippingTolerator = skippingTolerator ?? new StepSkippingTolerator();
    }

    /// <inheritdoc/>
    public async Task<StepChainVerificationResult> VerifyChainAsync(
        IReadOnlyList<ExtractedStep> steps, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count < 2)
        {
            // A single step has no transitions; trivially "succeeded".
            return new StepChainVerificationResult(Array.Empty<StepTransitionResult>(), null);
        }

        // Guard: chain-verifying low-confidence steps just produces noise.
        var firstLowConfidence = steps.FirstOrDefault(s => s.Confidence < ConfidenceThreshold);
        if (firstLowConfidence is { } lc)
        {
            var transition = new StepTransitionResult(
                FromStepIndex: Math.Max(lc.Index - 1, 0),
                ToStepIndex: lc.Index,
                Outcome: StepTransitionOutcome.LowConfidence,
                CasResult: null,
                Summary: $"Step {lc.Index + 1} confidence {lc.Confidence:P0} is below {ConfidenceThreshold:P0} — preview before chaining.");
            return new StepChainVerificationResult(new[] { transition }, lc.Index);
        }

        // PRR-361: populate each step's Canonical field via the canonicalizer.
        // We do this once up-front rather than per-transition so each pair
        // shares the cached form on both sides.
        var canonicalized = await CanonicalizeAllAsync(steps, ct).ConfigureAwait(false);

        var results = new List<StepTransitionResult>(canonicalized.Count - 1);
        for (int i = 0; i < canonicalized.Count - 1; i++)
        {
            var from = canonicalized[i];
            var to = canonicalized[i + 1];
            var cas = await VerifyTransitionAsync(from, to, ct);

            // PRR-362 step-skipping tolerance: when CAS equivalence fails,
            // ask the skipping-tolerator whether this looks like a genuine
            // error (short, close algebraic mistake) or a legit leap
            // (student skipped intermediate work we can't reconstruct).
            // The distinction surfaces as a different UI message: "Wrong"
            // vs "I couldn't follow between step N and step N+1".
            StepTransitionOutcome outcome;
            string summary;
            if (cas.Verified)
            {
                outcome = StepTransitionOutcome.Valid;
                summary = $"Step {from.Index + 1} → Step {to.Index + 1}: OK";
            }
            else
            {
                outcome = _skippingTolerator.Classify(new StepSkippingContext(
                    FromCanonical: string.IsNullOrWhiteSpace(from.Canonical) ? from.Latex : from.Canonical,
                    ToCanonical: string.IsNullOrWhiteSpace(to.Canonical) ? to.Latex : to.Canonical,
                    CasResult: cas));
                summary = outcome switch
                {
                    StepTransitionOutcome.UnfollowableSkip =>
                        $"Step {from.Index + 1} → Step {to.Index + 1}: I couldn't follow the work between these two steps.",
                    _ =>
                        $"Step {from.Index + 1} → Step {to.Index + 1}: {cas.ErrorMessage ?? "not equivalent"}",
                };
            }

            results.Add(new StepTransitionResult(from.Index, to.Index, outcome, cas, summary));

            // Short-circuit only on Wrong — UnfollowableSkip is NOT a
            // failure for the chain, it's a honest-uncertainty signal
            // the UI renders inline while continuing to show the rest
            // of the steps. Memory "Honest not complimentary".
            if (outcome == StepTransitionOutcome.Wrong)
            {
                return new StepChainVerificationResult(results, to.Index);
            }
        }
        return new StepChainVerificationResult(results, null);
    }

    /// <summary>
    /// Run the (optional) canonicalizer over every step, returning a new
    /// list with <see cref="ExtractedStep.Canonical"/> populated. Preserves
    /// <see cref="ExtractedStep.Latex"/> verbatim for display. If no
    /// canonicalizer is wired OR a step already carries a canonical form,
    /// we pass it through unchanged.
    /// </summary>
    private async Task<IReadOnlyList<ExtractedStep>> CanonicalizeAllAsync(
        IReadOnlyList<ExtractedStep> steps, CancellationToken ct)
    {
        if (_canonicalizer is null)
        {
            return steps;
        }

        var output = new ExtractedStep[steps.Count];
        for (int i = 0; i < steps.Count; i++)
        {
            var s = steps[i];
            if (!string.IsNullOrWhiteSpace(s.Canonical))
            {
                output[i] = s;
                continue;
            }

            var form = await _canonicalizer
                .CanonicalizeAsync(s.Latex ?? string.Empty, CasOperation.StepValidity, ct)
                .ConfigureAwait(false);

            // Prefer the SymPy-expanded canonical when available; fall back
            // to the cheap NormalizedLatex. Either way, preserve the raw
            // Latex on the output step.
            var canonical = form.CanonicalExpanded ?? form.NormalizedLatex;
            output[i] = s with { Canonical = canonical };
        }
        return output;
    }

    /// <summary>
    /// Verify that <paramref name="to"/> is algebraically equivalent to
    /// <paramref name="from"/>. Uses the canonical form when available; falls
    /// back to raw LaTeX otherwise.
    /// </summary>
    private Task<CasVerifyResult> VerifyTransitionAsync(
        ExtractedStep from, ExtractedStep to, CancellationToken ct)
    {
        var request = new CasVerifyRequest(
            Operation: CasOperation.StepValidity,
            ExpressionA: !string.IsNullOrWhiteSpace(from.Canonical) ? from.Canonical : from.Latex,
            ExpressionB: !string.IsNullOrWhiteSpace(to.Canonical) ? to.Canonical : to.Latex,
            Variable: null);
        return _casRouter.VerifyAsync(request, ct);
    }
}
