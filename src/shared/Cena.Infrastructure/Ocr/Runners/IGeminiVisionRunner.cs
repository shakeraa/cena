// =============================================================================
// Cena Platform — IGeminiVisionRunner (Layer 4b cloud fallback)
//
// Layer 4 calls this when a text block's per-region confidence is below τ.
// Wraps the Gemini 1.5 Flash vision endpoint. Same contract as
// IMathpixRunner: throw on open breaker, don't mutate input.
// =============================================================================

using Cena.Infrastructure.Ocr.Contracts;

namespace Cena.Infrastructure.Ocr.Runners;

public interface IGeminiVisionRunner
{
    /// <summary>
    /// Re-recognise the text region and return an updated block.
    /// </summary>
    /// <param name="croppedRegionBytes">
    /// PNG-encoded bytes of the cropped text region, extracted by Layer 4.
    /// </param>
    /// <param name="originalBlock">
    /// The low-confidence block the cascade wants to rescue.
    /// </param>
    Task<OcrTextBlock> RescueTextAsync(
        ReadOnlyMemory<byte> croppedRegionBytes,
        OcrTextBlock originalBlock,
        CancellationToken ct);
}
