// =============================================================================
// Cena Platform — IStudentPlanMigrationService (prr-219)
//
// The prr-219 migration safety net's public contract. Consumed by:
//   - The admin-triggered batch endpoint (POST /api/admin/institutes/{id}/
//     migrate-student-plan).
//   - The DLQ worker (re-drives failed rows).
//
// Splits "one student's upcast" from "the whole institute's drain" so
// retry and observability can be per-row.
//
// Design (per prr-219 DoD):
//   - Idempotent: MigrationSourceId is the dedup key; re-running returns
//     a "no-op" result rather than emitting a duplicate ExamTargetAdded.
//   - Dry-run mode: reports how many events would be emitted without
//     writing. Used by the admin UI to preview the blast radius before
//     flipping the flag.
//   - DLQ-aware: per-row failures emit StudentPlanMigrationFailed_V1
//     WITHOUT failing the batch, so a single bad row doesn't lock the
//     tenant.
// =============================================================================

using Cena.Actors.StudentPlan.Events;

namespace Cena.Actors.StudentPlan.Migration;

/// <summary>
/// Outcome classifier for a single-row upcast.
/// </summary>
public enum UpcastRowOutcome
{
    /// <summary>Row was successfully migrated (new events appended).</summary>
    Migrated,

    /// <summary>Row was already migrated (idempotent no-op).</summary>
    AlreadyMigrated,

    /// <summary>Row would be migrated (dry-run only — no writes).</summary>
    WouldMigrate,

    /// <summary>Row was routed to the DLQ (failure captured).</summary>
    Failed,

    /// <summary>Row was skipped because the feature flag was disabled
    /// for the student / tenant at the time of execution.</summary>
    Skipped,
}

/// <summary>
/// Per-row result carrying the new target id (on success) or the error
/// category (on failure).
/// </summary>
public sealed record UpcastRowResult(
    string MigrationSourceId,
    string StudentAnonId,
    UpcastRowOutcome Outcome,
    ExamTargetId? TargetId = null,
    MigrationErrorCategory? ErrorCategory = null,
    string? ErrorMessage = null);

/// <summary>
/// Aggregate result of a batch drain across many rows.
/// </summary>
public sealed record UpcastBatchResult(
    string TenantId,
    int Total,
    int Migrated,
    int AlreadyMigrated,
    int WouldMigrate,
    int Failed,
    int Skipped,
    IReadOnlyList<UpcastRowResult> Rows);

/// <summary>
/// The migration safety net. Stateless; wraps the command handler +
/// feature flag + store.
/// </summary>
public interface IStudentPlanMigrationService
{
    /// <summary>
    /// Upcast a single legacy plan row.
    /// </summary>
    Task<UpcastRowResult> UpcastAsync(
        LegacyStudentPlanSnapshot snapshot,
        bool dryRun,
        CancellationToken ct = default);

    /// <summary>
    /// Drain a batch of rows for a tenant. Non-flag-enabled rows are
    /// reported as <see cref="UpcastRowOutcome.Skipped"/>. Per-row
    /// failures are captured, not propagated — the batch never throws
    /// for row-level errors.
    /// </summary>
    Task<UpcastBatchResult> UpcastTenantAsync(
        string tenantId,
        IEnumerable<LegacyStudentPlanSnapshot> snapshots,
        bool dryRun,
        CancellationToken ct = default);

    /// <summary>
    /// Return true when a given legacy row has already been migrated
    /// (i.e. the student's plan stream contains a StudentPlanMigrated_V1
    /// event with the matching MigrationSourceId).
    /// </summary>
    Task<bool> IsMigratedAsync(
        string studentAnonId,
        string migrationSourceId,
        CancellationToken ct = default);
}
