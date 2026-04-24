// =============================================================================
// Cena Platform — OCR Math Block
//
// A single math region extracted as LaTeX plus its CAS-validation state.
// Per ADR-0002: only SympyParsed==true blocks are allowed to surface to
// students. Layer 5 sets this.
// =============================================================================

using System.Text.Json.Serialization;

namespace Cena.Infrastructure.Ocr.Contracts;

public sealed record OcrMathBlock(
    [property: JsonPropertyName("latex")]          string? Latex,
    [property: JsonPropertyName("bbox")]           BoundingBox? Bbox,
    [property: JsonPropertyName("confidence")]     double Confidence,
    [property: JsonPropertyName("sympy_parsed")]   bool SympyParsed,
    [property: JsonPropertyName("canonical_form")] string? CanonicalForm,

    // Scrubbed-fixture fields
    [property: JsonPropertyName("latex_length")]   int? LatexLength = null,
    [property: JsonPropertyName("latex_hash")]     string? LatexHash = null,
    [property: JsonPropertyName("_redacted")]      string? Redacted = null
);
