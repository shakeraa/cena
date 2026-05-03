// =============================================================================
// Cena Platform — OCR Context Hints (ADR-0033)
//
// Consumer-side contract for subject / language / track / source hints fed
// into the cascade from both surfaces:
//   Surface A — student photo upload (hints inferred from tutor context)
//   Surface B — admin ingestion (hints from CuratorMetadata via RDY-019e)
//
// Field names MUST match scripts/ocr-spike/dev-fixtures/context-hints/examples.json.
// JSON naming policy: snake_case (set at JsonSerializerOptions level).
// =============================================================================

using System.Text.Json.Serialization;

namespace Cena.Infrastructure.Ocr.Contracts;

public sealed record OcrContextHints(
    [property: JsonPropertyName("subject")]           string? Subject,
    [property: JsonPropertyName("language")]          Language? Language,
    [property: JsonPropertyName("track")]             Track? Track,
    [property: JsonPropertyName("source_type")]       SourceType? SourceType,
    [property: JsonPropertyName("taxonomy_node")]     string? TaxonomyNode,
    [property: JsonPropertyName("expected_figures")]  bool? ExpectedFigures
);
