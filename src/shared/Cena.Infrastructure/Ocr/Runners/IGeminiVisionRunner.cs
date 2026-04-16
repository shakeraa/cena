// =============================================================================
// Cena Platform — IGeminiVisionRunner (Layer 4b cloud fallback)
//
// Invoked by Layer 4 when a text block's confidence is below τ. Wraps the
// existing Gemini Vision HTTP client (RDY-012 circuit-broken). Same
// contract as IMathpixRunner — throw on open breaker, don't mutate input.
// =============================================================================

using Cena.Infrastructure.Ocr.Contracts;

namespace Cena.Infrastructure.Ocr.Runners;

public interface IGeminiVisionRunner
{
    Task<OcrTextBlock> RescueTextAsync(OcrTextBlock block, CancellationToken ct);
}
