// =============================================================================
// Cena Platform — OCR Cascade Result (ADR-0033)
//
// The normalised output shape that IOcrCascadeService returns. Field names +
// order match scripts/ocr-spike/pipeline_prototype.py CascadeResult and the
// committed dev-fixtures at scripts/ocr-spike/dev-fixtures/cascade-results/.
//
// This is the contract: downstream consumers (PhotoCaptureEndpoints,
// BagrutPdfIngestionService, the admin UI review queue) depend on this shape.
// Changes are breaking — coordinate via ADR before editing.
// =============================================================================

using System.Text.Json.Serialization;

namespace Cena.Infrastructure.Ocr.Contracts;

public sealed record OcrCascadeResult(
    [property: JsonPropertyName("schema_version")]           string SchemaVersion,
    [property: JsonPropertyName("runner")]                   string Runner,
    [property: JsonPropertyName("source")]                   string Source,
    [property: JsonPropertyName("hints")]                    OcrContextHints? Hints,
    [property: JsonPropertyName("pdf_triage")]               PdfTriageVerdict? PdfTriage,

    [property: JsonPropertyName("text_blocks")]              IReadOnlyList<OcrTextBlock> TextBlocks,
    [property: JsonPropertyName("math_blocks")]              IReadOnlyList<OcrMathBlock> MathBlocks,
    [property: JsonPropertyName("figures")]                  IReadOnlyList<OcrFigureRef> Figures,

    [property: JsonPropertyName("overall_confidence")]       double OverallConfidence,
    [property: JsonPropertyName("fallbacks_fired")]          IReadOnlyList<string> FallbacksFired,

    [property: JsonPropertyName("cas_validated_math")]       int CasValidatedMath,
    [property: JsonPropertyName("cas_failed_math")]          int CasFailedMath,

    [property: JsonPropertyName("human_review_required")]    bool HumanReviewRequired,
    [property: JsonPropertyName("reasons_for_review")]       IReadOnlyList<string> ReasonsForReview,

    [property: JsonPropertyName("layer_timings_seconds")]    IReadOnlyDictionary<string, double> LayerTimingsSeconds,
    [property: JsonPropertyName("total_latency_seconds")]    double TotalLatencySeconds,
    [property: JsonPropertyName("captured_at")]              string CapturedAt,

    // Optional dev-fixture marker — ignored by the production cascade
    [property: JsonPropertyName("_dev_fixture")]             DevFixtureMarker? DevFixture = null
);

public sealed record DevFixtureMarker(
    [property: JsonPropertyName("scenario")]    string Scenario,
    [property: JsonPropertyName("source_kind")] string SourceKind,
    [property: JsonPropertyName("notes")]       string? Notes,
    [property: JsonPropertyName("scrubbing")]   string? Scrubbing
);
