// =============================================================================
// Cena Platform — LegacyStudentPlanSnapshot (prr-219)
//
// Input shape consumed by the migration safety net. A manifest builder
// produces these snapshots offline (batch job `scripts/migration/
// generate-upcast-manifest.ts`), then the online UpcastService drains the
// manifest per tenant with staged rollout, retries, and DLQ.
//
// We intentionally do NOT read the legacy Firebase Auth claims or Marten
// inside this service — the manifest loader upstream is responsible for
// producing a self-contained payload. The upcast is a pure function of
// the manifest row + a clock + a command handler, which keeps the unit
// tests hermetic.
// =============================================================================

namespace Cena.Actors.StudentPlan.Migration;

/// <summary>
/// One row in the migration manifest: the legacy plan data to upcast plus
/// the inference metadata the loader already resolved.
/// </summary>
/// <param name="MigrationSourceId">Idempotency key — stable identifier of
/// the legacy plan row. Re-running the upcast with the same id is a
/// no-op.</param>
/// <param name="StudentAnonId">Pseudonymous student id.</param>
/// <param name="TenantId">Institute the student belongs to (ADR-0001
/// scoping).</param>
/// <param name="LegacyDeadlineUtc">Legacy single-target deadline, if the
/// student had set one.</param>
/// <param name="LegacyWeeklyBudget">Legacy single-target weekly budget,
/// if set.</param>
/// <param name="InferredExamCode">Catalog primary key inferred by the
/// manifest loader from Firebase grade+track claim. Defaults to
/// <c>BAGRUT_MATH_4U</c> when the claim is absent (per prr-219 §Inference
/// Rules).</param>
/// <param name="InferredTrack">Track inferred from the Firebase claim.
/// Null when no track data.</param>
/// <param name="InferredSitting">Sitting tuple inferred from the legacy
/// deadline by the catalog service (PRR-220). Null when the deadline
/// doesn't fit any catalog sitting — in that case the upcast creates the
/// target in an immediately-archived state so retention still applies.</param>
public sealed record LegacyStudentPlanSnapshot(
    string MigrationSourceId,
    string StudentAnonId,
    string TenantId,
    DateTimeOffset? LegacyDeadlineUtc,
    TimeSpan? LegacyWeeklyBudget,
    ExamCode InferredExamCode,
    TrackCode? InferredTrack,
    SittingCode? InferredSitting);
