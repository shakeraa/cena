// =============================================================================
// Cena Platform — IMathpixRunner (Layer 4a cloud fallback)
//
// Invoked by Layer 4 when a math region's confidence is below τ. The
// implementation wraps the existing Mathpix HTTP client from RDY-012 and
// respects that task's circuit breaker. When the breaker is open,
// implementations throw OcrCircuitOpenException — Layer 4 catches and passes
// the original block through untouched (degraded mode, not failure).
//
// If this service is not registered, Layer 4 skips math rescue entirely.
// =============================================================================

using Cena.Infrastructure.Ocr.Contracts;

namespace Cena.Infrastructure.Ocr.Runners;

public interface IMathpixRunner
{
    /// <summary>
    /// Attempt to re-recognise the math region behind a low-confidence block.
    /// Returns a NEW <see cref="OcrMathBlock"/> with updated latex + confidence.
    /// Implementations must not mutate the input.
    /// </summary>
    /// <exception cref="OcrCircuitOpenException">
    /// Mathpix circuit breaker is open (RDY-012). Caller must tolerate this
    /// and fall back to the original block.
    /// </exception>
    Task<OcrMathBlock> RescueMathAsync(OcrMathBlock block, CancellationToken ct);
}
