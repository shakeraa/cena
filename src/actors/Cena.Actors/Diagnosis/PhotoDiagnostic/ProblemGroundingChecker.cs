// =============================================================================
// Cena Platform — ProblemGroundingChecker (EPIC-PRR-J PRR-353, ADR-0002)
//
// Intake-side guard on the photo-diagnostic pipeline. When a student
// uploads a photo of their work, we extract an ordered sequence of
// LaTeX steps (OCR → parse → canonicalize). Step 0 is supposed to be
// the student's restatement of the currently-posed problem's initial
// expression. If it isn't — either the photo is of a DIFFERENT problem,
// or a gaming / abuse attempt uploaded an unrelated screenshot — we
// short-circuit with a helpful message BEFORE running the expensive
// full chain verification.
//
// Per ADR-0002 (SymPy is the sole correctness oracle), the equivalence
// decision routes through ICasRouterService rather than any ad-hoc
// string compare. The router's 3-tier pipeline (MathNet → SymPy →
// fallback) handles the cheap cases in-process and delegates hard
// equivalence (expanded polynomial identity, trig reduction, numerical
// tolerance) to SymPy. We ask for CasOperation.Equivalence with the
// POSED expression as A and EXTRACTED step 0 as B. Equivalence, not
// StepValidity: the student may have restated the problem in any
// algebraically equivalent form (factored, expanded, collected).
//
// Reject semantics (helpful, not punitive):
//   - GroundingDecision.Accept: step 0 is equivalent to the posed
//     expression (or numerically-equal within the router's tolerance).
//   - GroundingDecision.Reject: step 0 is NOT equivalent. Caller shows
//     "I couldn't connect this to the problem you're solving." Not
//     "you uploaded the wrong photo" — the student may have legitimately
//     skipped writing the problem restatement (in which case UI re-prompts
//     them to upload the problem alongside their work).
//   - GroundingDecision.Undetermined: the CAS router was unavailable
//     (engine Error / Timeout / CircuitBreakerOpen). Caller proceeds
//     with full chain verification and records the gap — we DO NOT
//     reject on router outage because that would falsely accuse the
//     student of uploading the wrong problem during an infrastructure
//     incident.
// =============================================================================

using Cena.Actors.Cas;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>Verdict returned by <see cref="IProblemGroundingChecker"/>.</summary>
public enum GroundingDecision
{
    /// <summary>
    /// Step 0 is mathematically equivalent to the posed expression.
    /// Pipeline proceeds to full chain verification.
    /// </summary>
    Accept = 0,

    /// <summary>
    /// Step 0 is NOT equivalent. Pipeline short-circuits with a helpful
    /// "couldn't connect this to the problem" message.
    /// </summary>
    Reject = 1,

    /// <summary>
    /// CAS router was unavailable (SymPy outage, MathNet error). Pipeline
    /// should fall back to full chain verification and log the gap rather
    /// than falsely rejecting the student's upload.
    /// </summary>
    Undetermined = 2,
}

/// <summary>Full result shape so callers + tests can inspect the decision path.</summary>
public sealed record ProblemGroundingResult(
    GroundingDecision Decision,
    string? RejectReason,
    CasVerifyResult? CasResult)
{
    /// <summary>Quick accept factory.</summary>
    public static ProblemGroundingResult Accept(CasVerifyResult cas) =>
        new(GroundingDecision.Accept, null, cas);

    /// <summary>Reject with a stable machine-readable reason code.</summary>
    public static ProblemGroundingResult Reject(string reason, CasVerifyResult? cas) =>
        new(GroundingDecision.Reject, reason, cas);

    /// <summary>Undetermined — router failure; caller falls back.</summary>
    public static ProblemGroundingResult Undetermined(CasVerifyResult? cas) =>
        new(GroundingDecision.Undetermined, null, cas);
}

/// <summary>Seam for the grounding check; injectable for tests.</summary>
public interface IProblemGroundingChecker
{
    /// <summary>
    /// Verify that <paramref name="extractedStep0Latex"/> is equivalent to
    /// the currently-posed <paramref name="posedExpressionLatex"/>. Both
    /// arguments are LaTeX strings (already canonicalized by the upstream
    /// extraction pipeline when possible).
    /// </summary>
    Task<ProblemGroundingResult> CheckAsync(
        string posedExpressionLatex,
        string extractedStep0Latex,
        CancellationToken ct = default);
}

/// <summary>
/// Default implementation. Delegates equivalence to ICasRouterService
/// via CasOperation.Equivalence. Pure orchestration; every invariant
/// lives in the router.
/// </summary>
public sealed class ProblemGroundingChecker : IProblemGroundingChecker
{
    /// <summary>Machine-readable reject code for "expressions not equivalent".</summary>
    public const string RejectCodeNotEquivalent = "not_equivalent_to_posed";

    /// <summary>Machine-readable reject code for empty-input edge cases.</summary>
    public const string RejectCodeEmptyInput = "empty_input";

    private readonly ICasRouterService _casRouter;

    /// <summary>Construct with the CAS router.</summary>
    public ProblemGroundingChecker(ICasRouterService casRouter)
    {
        _casRouter = casRouter ?? throw new ArgumentNullException(nameof(casRouter));
    }

    /// <inheritdoc />
    public async Task<ProblemGroundingResult> CheckAsync(
        string posedExpressionLatex,
        string extractedStep0Latex,
        CancellationToken ct = default)
    {
        // Empty-input boundary. We don't call SymPy with an empty string;
        // the reject code is separable so the UI can render a different
        // message ("we couldn't read step 1 of your work — can you retake
        // the photo?" vs "this doesn't match the problem").
        if (string.IsNullOrWhiteSpace(posedExpressionLatex))
        {
            return ProblemGroundingResult.Reject(
                RejectCodeEmptyInput, cas: null);
        }
        if (string.IsNullOrWhiteSpace(extractedStep0Latex))
        {
            return ProblemGroundingResult.Reject(
                RejectCodeEmptyInput, cas: null);
        }

        var request = new CasVerifyRequest(
            Operation: CasOperation.Equivalence,
            ExpressionA: posedExpressionLatex,
            ExpressionB: extractedStep0Latex,
            Variable: null);

        var cas = await _casRouter.VerifyAsync(request, ct).ConfigureAwait(false);

        // Router status Ok = definitive answer (Verified=true → equivalent,
        // Verified=false → not equivalent). Anything else = infrastructure
        // problem; fall back to Undetermined so the pipeline doesn't reject
        // a legitimate upload during a SymPy outage.
        if (cas.Status != CasVerifyStatus.Ok)
        {
            return ProblemGroundingResult.Undetermined(cas);
        }

        return cas.Verified
            ? ProblemGroundingResult.Accept(cas)
            : ProblemGroundingResult.Reject(RejectCodeNotEquivalent, cas);
    }
}
