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
/// </summary>
public sealed class StepChainVerifier : IStepChainVerifier
{
    /// <summary>
    /// OCR confidence below which we refuse to chain-verify. Callers should
    /// show the preview UX before calling us when this is common.
    /// </summary>
    public const double ConfidenceThreshold = 0.60;

    private readonly ICasRouterService _casRouter;

    public StepChainVerifier(ICasRouterService casRouter)
    {
        _casRouter = casRouter ?? throw new ArgumentNullException(nameof(casRouter));
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

        var results = new List<StepTransitionResult>(steps.Count - 1);
        for (int i = 0; i < steps.Count - 1; i++)
        {
            var from = steps[i];
            var to = steps[i + 1];
            var cas = await VerifyTransitionAsync(from, to, ct);
            var outcome = cas.Verified
                ? StepTransitionOutcome.Valid
                : StepTransitionOutcome.Wrong;

            var summary = outcome == StepTransitionOutcome.Valid
                ? $"Step {from.Index + 1} → Step {to.Index + 1}: OK"
                : $"Step {from.Index + 1} → Step {to.Index + 1}: {cas.ErrorMessage ?? "not equivalent"}";

            results.Add(new StepTransitionResult(from.Index, to.Index, outcome, cas, summary));

            if (outcome == StepTransitionOutcome.Wrong)
            {
                return new StepChainVerificationResult(results, to.Index);
            }
        }
        return new StepChainVerificationResult(results, null);
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
