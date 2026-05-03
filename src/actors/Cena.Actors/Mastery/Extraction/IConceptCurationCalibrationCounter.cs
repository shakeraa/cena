// =============================================================================
// Cena Platform — Concept Curation Calibration Counter (ADR-0062 Phase 1 gate)
//
// Distinct from IConceptItemPublicationCounter (Phase 2 nudge gate at ≥10
// items per leaf). This counter implements the Phase 1 calibration corpus
// rule from ADR-0062 §5:
//
//   "First 200 Bagrut items extracted under the new pipeline: curator
//    must confirm the concept set before publish (calibration corpus).
//    After 200: extraction stands by default; curator UI surfaces the
//    set for one-click override."
//
// What "an item" means for this counter:
//   * Streams (questions) for which a `QuestionConceptsConfirmed_V1`
//     event has been appended at least once. The counter aggregates
//     across the whole catalog, not per-track or per-skill.
//   * Once the count crosses the threshold the gate opens for everyone
//     (the goal is precision calibration, not per-item gating).
//
// What the gate does NOT do:
//   * Block draft creation, edit, or approval. Only publish.
//   * Block items that did not go through the new extraction pipeline
//     (legacy seeded questions, mock-exam fixtures). The gate fires
//     only when the question stream carries a
//     `QuestionConceptsExtracted_V1` event with at least one concept.
//   * Decide BKT, mastery, or scheduling — purely a publish governance
//     control.
//
// Why a counter, not a flag:
//   * The threshold (200) is configurable per ADR-0062 if telemetry
//     justifies tightening or loosening; an env-var override on the
//     counter implementation costs ~3 lines without touching callers.
//   * Tests can stub a counter that reports any value without seeding
//     200 real questions.
//
// Why FAIL-CLOSED is wrong here (vs. IConceptItemPublicationCounter):
//   * Phase 2 nudges fail closed because a missed nudge is harmless;
//     a stale-data nudge over-claims mastery.
//   * Phase 1 publish-gate fails OPEN: if the counter errors, we should
//     NOT block legitimate publishes (the curator already approved the
//     question via the upstream review flow). Errors propagate up; the
//     caller decides — see QuestionBankService.PublishAsync where the
//     gate explicitly allows publish on counter failure with a warning
//     log so ops can investigate without locking the catalog.
// =============================================================================

namespace Cena.Actors.Mastery.Extraction;

public interface IConceptCurationCalibrationCounter
{
    /// <summary>
    /// Threshold above which the calibration gate opens. ADR-0062 §5 sets
    /// this to 200 for the Bagrut math corpus. Implementations may expose
    /// this as a configurable property; the contract stays "≥ this many
    /// curator-confirmed items unlocks the gate".
    /// </summary>
    int CalibrationThreshold { get; }

    /// <summary>
    /// Number of distinct question streams that have at least one
    /// <c>QuestionConceptsConfirmed_V1</c> event. Every confirm — accept,
    /// edit, override — counts; the calibration corpus measures extractor
    /// precision against curator decisions, not just "extractor was right".
    /// </summary>
    Task<int> GetConfirmedItemCountAsync(CancellationToken ct = default);

    /// <summary>
    /// Convenience: <c>true</c> iff the calibration phase is complete
    /// (count meets <see cref="CalibrationThreshold"/>). Implementations
    /// may cache the underlying count for a short TTL because the gate
    /// fires on every publish — uncached, this would be a Marten round
    /// trip per publish call. Cache invalidation is fine because once
    /// the threshold is crossed, it stays crossed (monotone).
    /// </summary>
    Task<bool> IsCalibrationCompleteAsync(CancellationToken ct = default);
}
