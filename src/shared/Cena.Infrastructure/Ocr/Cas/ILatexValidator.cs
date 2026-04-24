// =============================================================================
// Cena Platform — ILatexValidator (ADR-0002 bridge for Layer 5)
//
// Layer 5 of the OCR cascade round-trips every recovered LaTeX string through
// a CAS oracle (per ADR-0002, "the LLM explains, SymPy verifies"). This
// interface is the boundary — the real implementation wraps the existing
// CasRouterService (src/actors/Cena.Actors/Cas/CasRouterService.cs), but the
// cascade code does not take a hard dependency on Actors so Infrastructure
// stays at the right layer.
//
// Registration pattern (in the host that knows about CasRouterService):
//   services.AddOcrCascadeCore();  // registers NullLatexValidator default
//   services.AddSingleton<ILatexValidator, CasRouterLatexValidator>();
//                                  // ↑ real impl in Actors/Admin.Api, wraps CAS
// =============================================================================

namespace Cena.Infrastructure.Ocr.Cas;

public interface ILatexValidator
{
    /// <summary>
    /// Attempt to parse the LaTeX and produce a canonical form. Implementations
    /// MUST NOT throw on malformed input — return Parsed=false instead. Genuine
    /// infrastructure failures (cancelled token, etc.) may propagate.
    /// </summary>
    ValueTask<LatexValidationResult> ValidateAsync(string latex, CancellationToken ct);
}

public sealed record LatexValidationResult(
    bool Parsed,
    string? CanonicalForm,
    string? RejectionReason = null);
