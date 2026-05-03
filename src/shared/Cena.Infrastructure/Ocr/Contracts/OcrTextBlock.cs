// =============================================================================
// Cena Platform — OCR Text Block
//
// A single recognised line/region of text. Confidence is per-region, used by
// Layer 4's τ gate. `text` is nullable so dev-fixtures can scrub raw Hebrew
// text (see scripts/ocr-spike/dev-fixtures) while keeping the shape intact
// for downstream integration testing.
// =============================================================================

using System.Text.Json.Serialization;

namespace Cena.Infrastructure.Ocr.Contracts;

public sealed record OcrTextBlock(
    [property: JsonPropertyName("text")]        string? Text,
    [property: JsonPropertyName("bbox")]        BoundingBox? Bbox,
    [property: JsonPropertyName("language")]    Language Language,
    [property: JsonPropertyName("confidence")]  double Confidence,
    [property: JsonPropertyName("is_rtl")]      bool IsRtl,

    // Scrubbed-fixture fields — null on real cascade output, set when a
    // dev-fixture redacts Ministry/NITE/Geva text per reference-only rules.
    [property: JsonPropertyName("text_length")] int? TextLength = null,
    [property: JsonPropertyName("text_hash")]   string? TextHash = null,
    [property: JsonPropertyName("_redacted")]   string? Redacted = null
);
