// =============================================================================
// Cena Platform — IMathpixRunner (Layer 4a cloud fallback)
//
// Layer 4 calls this when a math block's per-region confidence is below τ.
// The runner receives the ORIGINAL page-region crop (already extracted by
// Layer 4 via ImageSharp) plus the low-confidence block for context, and
// returns a NEW OcrMathBlock with updated latex + confidence.
//
// Real implementations hit the Mathpix /v3/text API via HttpClient, wrapped
// by RDY-012's Polly circuit breaker. When the breaker is open, throw
// OcrCircuitOpenException — Layer 4 catches and passes the original block
// through untouched (degraded mode is still a functional cascade).
//
// Implementations MUST NOT mutate the input block.
// =============================================================================

using Cena.Infrastructure.Ocr.Contracts;

namespace Cena.Infrastructure.Ocr.Runners;

public interface IMathpixRunner
{
    /// <summary>
    /// Re-recognise the math region and return an updated block.
    /// </summary>
    /// <param name="croppedRegionBytes">
    /// PNG-encoded bytes of the cropped math region, extracted from the
    /// preprocessed page by Layer 4 using the block's BoundingBox.
    /// </param>
    /// <param name="originalBlock">
    /// The low-confidence block the cascade wants to rescue. Implementations
    /// should preserve bbox + language + is_rtl from the original.
    /// </param>
    /// <exception cref="OcrCircuitOpenException">
    /// RDY-012 circuit breaker is open. Layer 4 tolerates.
    /// </exception>
    Task<OcrMathBlock> RescueMathAsync(
        ReadOnlyMemory<byte> croppedRegionBytes,
        OcrMathBlock originalBlock,
        CancellationToken ct);
}
