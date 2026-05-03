// =============================================================================
// Cena Platform — Mock-exam run cost telemetry (PRR-322)
//
// Per-run cost attribution doc keyed on runId. Written ONCE by
// MockExamRunService.SubmitAsync after grading completes. Reads back by
// the admin "Mock-exam runs / cost" dashboard for per-run, daily, and
// 30-day projection views.
//
// Cost streams:
//   CAS calls         — counted by MockExamGrader (per VerifyAsync attempt
//                       INCLUDING retries; PRR-297 retries are real cost).
//   LLM tokens (in/out) — currently zero. The mock-exam runner does not
//                       invoke any LLM call site today (verified 2026-04-30
//                       via grep — neither MockExamRunService nor
//                       MockExamGrader takes ILlmGateway/IAiGenerationService).
//                       Fields shipped now so the doc shape is stable when
//                       PRR-322f-llm-attribution lands tutor-mid-exam or
//                       post-grade-explanation features.
//   OCR calls         — currently zero. Photo-input mock-exam paths don't
//                       exist yet; PRR-322f-ocr-attribution wires this when
//                       student-photo mock-exam ingestion lands.
//
// Cost-per-USD comes from CostRateConfig at write time; rates are NOT
// persisted on the doc. If a rate changes, the historical doc still
// reflects the old USD amounts (audit-friendly). For replay-with-new-
// rates, query the raw counts and re-multiply.
// =============================================================================

namespace Cena.Infrastructure.Documents;

public class MockExamRunCost
{
    /// <summary>Same value as ExamSimulationState.SimulationId / runId.</summary>
    public string Id { get; set; } = "";

    public string StudentId { get; set; } = "";
    public string ExamCode { get; set; } = "";

    /// <summary>
    /// Optional institute / tenant scope label. Null until ADR-0001 Phase 1
    /// of multi-institute lands and threads tenant id through the run.
    /// Surfaces as the "studentTenant" tag on the OpenTelemetry counter so
    /// per-tenant Grafana panels can split out cost when ready.
    /// </summary>
    public string? StudentTenant { get; set; }

    // ── Counts (non-monetary; replayable into different rate configs) ──
    public int CasCallsCount { get; set; }
    public int LlmTokensInput { get; set; }
    public int LlmTokensOutput { get; set; }
    public int OcrCallsCount { get; set; }

    // ── USD totals (frozen at compute-time; do NOT recompute from counts
    //    later — rates can drift). ────────────────────────────────────
    public decimal CasCostUsd { get; set; }
    public decimal LlmCostUsd { get; set; }
    public decimal OcrCostUsd { get; set; }
    public decimal TotalUsd { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}
