// =============================================================================
// Cena Platform — CasRouterLatexValidator (ADR-0002 + ADR-0033 bridge)
//
// Real implementation of ILatexValidator for the OCR cascade's Layer 5. Wraps
// the existing 3-tier CAS router (MathNet → SymPy → MathNet fallback):
//   - MathNet tier handles arithmetic + basic algebra in-process
//   - SymPy tier (via NATS sidecar) handles LaTeX, calculus, trig, ODE
//   - Router automatically promotes LaTeX input to SymPy after MathNet errors
//
// No mocks. No stubs. Every call hits real CAS infrastructure. When the CAS
// cost circuit breaker is open, returns Parsed=false with the breaker
// reason — Layer 5 flags those blocks CAS-failed and the orchestrator
// surfaces them to human review.
// =============================================================================

using Cena.Infrastructure.Ocr.Cas;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Cas;

public sealed class CasRouterLatexValidator : ILatexValidator
{
    private readonly ICasRouterService _router;
    private readonly ILogger<CasRouterLatexValidator>? _log;

    public CasRouterLatexValidator(
        ICasRouterService router,
        ILogger<CasRouterLatexValidator>? log = null)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _log = log;
    }

    public async ValueTask<LatexValidationResult> ValidateAsync(
        string latex, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(latex))
        {
            return new LatexValidationResult(
                Parsed: false,
                CanonicalForm: null,
                RejectionReason: "empty_latex");
        }

        var cleaned = CleanLatex(latex);
        if (cleaned.Length == 0)
        {
            return new LatexValidationResult(
                Parsed: false,
                CanonicalForm: null,
                RejectionReason: "empty_after_cleaning");
        }

        // NormalForm asks the router "can you simplify this?". On success the
        // router returns the canonical form in SimplifiedA — which is exactly
        // what OcrMathBlock.CanonicalForm expects. MathNet will error out on
        // raw LaTeX (Infix.ParseOrThrow rejects `\frac`, `=`, etc.); the
        // router then falls through to SymPy which parses LaTeX natively.
        var request = new CasVerifyRequest(
            Operation: CasOperation.NormalForm,
            ExpressionA: cleaned,
            ExpressionB: null,
            Variable: null);

        CasVerifyResult result;
        try
        {
            result = await _router.VerifyAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex,
                "[OCR_CASCADE] Layer5 CAS router threw; marking block CAS-failed");
            return new LatexValidationResult(
                Parsed: false,
                CanonicalForm: null,
                RejectionReason: $"router_exception:{ex.GetType().Name}");
        }

        // Status.Ok means the engine processed the request. Verified=true means
        // the math check passed. For NormalForm there's no "false" verification
        // — the engine either gave us a canonical form or errored.
        if (result.Status == CasVerifyStatus.Ok && result.Verified && result.SimplifiedA is not null)
        {
            return new LatexValidationResult(
                Parsed: true,
                CanonicalForm: result.SimplifiedA);
        }

        // Circuit breaker / timeout / unsupported → CAS could not verify this
        // block. Parsed=false means Layer 5 tags SympyParsed=false and the
        // orchestrator decides whether to surface for human review.
        return new LatexValidationResult(
            Parsed: false,
            CanonicalForm: null,
            RejectionReason: FormatRejectionReason(result));
    }

    /// <summary>
    /// Strip common LaTeX rendering artefacts that neither MathNet nor SymPy
    /// need. Keeps the body of the expression intact — real LaTeX→CAS
    /// conversion happens inside the SymPy tier (sympy.parsing.latex).
    /// </summary>
    internal static string CleanLatex(string latex)
    {
        var s = latex.Trim();

        // Drop $ … $ / $$ … $$ delimiters
        if (s.StartsWith("$$") && s.EndsWith("$$") && s.Length >= 4)
            s = s[2..^2];
        else if (s.StartsWith('$') && s.EndsWith('$') && s.Length >= 2)
            s = s[1..^1];

        s = s.Trim();

        // Remove thin-space macros that LaTeX uses for display but that CAS
        // parsers don't need: "\\," "\\ " "\\;" "\\!" "\\:"
        foreach (var artefact in new[] { @"\,", @"\;", @"\!", @"\:", @"\ " })
            s = s.Replace(artefact, string.Empty);

        return s.Trim();
    }

    private static string FormatRejectionReason(CasVerifyResult r) =>
        r.Status switch
        {
            CasVerifyStatus.CircuitBreakerOpen => "cas_circuit_open",
            CasVerifyStatus.Timeout => "cas_timeout",
            CasVerifyStatus.UnsupportedOperation => "cas_unsupported",
            CasVerifyStatus.Error => $"cas_error:{Trim(r.ErrorMessage)}",
            _ => $"cas_unverified:{Trim(r.ErrorMessage)}",
        };

    private static string Trim(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "unknown";
        return s.Length <= 80 ? s : s[..80];
    }
}
