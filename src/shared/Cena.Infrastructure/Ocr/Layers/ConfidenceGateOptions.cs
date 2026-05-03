// =============================================================================
// Cena Platform — Layer 4 Confidence-Gate Options (ADR-0033)
//
// Tunable thresholds the gate uses to decide pass-through vs. cloud rescue
// vs. catastrophic-review. Defaults match the spike — re-derive from the
// full benchmark fixture distribution once RDY-OCR-PORT ships and the
// regression suite has baseline scores.
//
// Bind from configuration:
//   "Ocr": {
//     "ConfidenceGate": {
//       "ConfidenceThreshold": 0.65,
//       "StudentCatastrophicThreshold": 0.30,
//       "AdminCatastrophicThreshold": 0.40
//     }
//   }
// =============================================================================

namespace Cena.Infrastructure.Ocr.Layers;

public sealed class ConfidenceGateOptions
{
    /// <summary>
    /// τ — any region below this confidence triggers Layer 4 cloud fallback.
    /// Spike-derived preliminary value; production value is re-derived from
    /// the full 9-tool benchmark fixture distribution.
    /// </summary>
    public double ConfidenceThreshold { get; init; } = 0.65;

    /// <summary>
    /// Surface A (student interactive). Below this the cascade reports a
    /// catastrophic failure and the endpoint returns 422
    /// "could not recognize, please re-photograph".
    /// </summary>
    public double StudentCatastrophicThreshold { get; init; } = 0.30;

    /// <summary>
    /// Surface B (admin batch). Below this the cascade flags the item for
    /// human review (no 422 since there's no end user in the loop).
    /// </summary>
    public double AdminCatastrophicThreshold { get; init; } = 0.40;

    /// <summary>
    /// Maximum characters of the original text/latex to embed in the
    /// fallbacks_fired identifier. Keeps structured logs bounded.
    /// </summary>
    public int FallbackLabelTruncation { get; init; } = 20;
}
