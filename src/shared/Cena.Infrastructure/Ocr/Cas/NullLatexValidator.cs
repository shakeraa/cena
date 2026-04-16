// =============================================================================
// Cena Platform — NullLatexValidator
//
// Safe default registration for ILatexValidator. Returns Parsed=false for
// every input — the cascade will flag every math block as CAS-failed and
// surface the result to human-review. That's the fail-closed behaviour
// mandated by ADR-0002 when no real oracle is wired.
//
// Production hosts MUST replace this with a CAS-backed implementation:
//   services.AddSingleton<ILatexValidator, CasRouterLatexValidator>();
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Ocr.Cas;

public sealed class NullLatexValidator : ILatexValidator
{
    private readonly ILogger<NullLatexValidator>? _log;
    private int _warned;

    public NullLatexValidator(ILogger<NullLatexValidator>? log = null)
    {
        _log = log;
    }

    public ValueTask<LatexValidationResult> ValidateAsync(string latex, CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _warned, 1) == 0)
        {
            _log?.LogWarning(
                "[OCR_CASCADE] NullLatexValidator is registered — every math block " +
                "will be marked CAS-failed. Register a CAS-backed ILatexValidator " +
                "(e.g. CasRouterLatexValidator wrapping CasRouterService) in production hosts.");
        }
        return ValueTask.FromResult(
            new LatexValidationResult(
                Parsed: false,
                CanonicalForm: null,
                RejectionReason: "no_cas_validator_registered"));
    }
}
