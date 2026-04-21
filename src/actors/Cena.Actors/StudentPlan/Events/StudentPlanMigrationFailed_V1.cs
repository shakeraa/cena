// =============================================================================
// Cena Platform — StudentPlanMigrationFailed_V1 (prr-219)
//
// Emitted during the legacy StudentPlanConfig → multi-target upcast
// (prr-219 migration safety net) when a per-student upcast fails. Does
// NOT fail the stream — it records the failure so the DLQ worker can
// retry and the admin UI can surface the failure count.
//
// Per prr-219 scope: "StudentPlanMigrationFailed_V1 event captures
// row-level failures".
//
// Stream key: `studentplan-{studentAnonId}` — the failure lives alongside
// the student's plan stream so ReasoningBank / audit tooling can correlate
// without a separate DLQ namespace.
// =============================================================================

namespace Cena.Actors.StudentPlan.Events;

/// <summary>
/// A per-student migration upcast failed. Appended to
/// <c>studentplan-{studentAnonId}</c>. Downstream DLQ worker reads these
/// to retry with exponential backoff.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id.</param>
/// <param name="TenantId">Institute the student belongs to (ADR-0001
/// scoping — admins scope retry operations per-tenant).</param>
/// <param name="MigrationSourceId">Idempotency key — legacy plan row id
/// that the migration attempted to upcast.</param>
/// <param name="ErrorCategory">Structured enum so the DLQ worker can
/// route (e.g. Transient → retry, Permanent → alert on-call).</param>
/// <param name="ErrorMessage">Sanitised error string (no PII; no stack
/// trace — keep the event body small).</param>
/// <param name="AttemptNumber">1-indexed attempt counter. The DLQ worker
/// increments this and alerts on-call after the third failed attempt
/// per prr-219 DoD.</param>
/// <param name="FailedAt">Wall-clock of the failure.</param>
public sealed record StudentPlanMigrationFailed_V1(
    string StudentAnonId,
    string TenantId,
    string MigrationSourceId,
    MigrationErrorCategory ErrorCategory,
    string ErrorMessage,
    int AttemptNumber,
    DateTimeOffset FailedAt);

/// <summary>
/// Migration failure category. Drives DLQ routing (Transient → retry,
/// Permanent → operator review).
/// </summary>
public enum MigrationErrorCategory
{
    /// <summary>Transient infrastructure failure (connection timeout,
    /// optimistic concurrency, etc). Retryable.</summary>
    Transient = 0,

    /// <summary>Permanent data-shape failure (legacy row can't be mapped
    /// to a catalog ExamCode). Not retryable — needs operator review.</summary>
    Permanent = 1,

    /// <summary>Legacy row violates the target invariants (e.g. weekly
    /// hours &gt; 40). Needs operator review.</summary>
    InvariantViolation = 2,
}

/// <summary>
/// Successful migration record. Appended to <c>studentplan-{studentAnonId}</c>
/// AFTER the ExamTargetAdded_V1 from the upcast, so audit queries can
/// distinguish "student added target" from "migration upcast added target"
/// without relying on <see cref="ExamTargetSource"/> alone.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id.</param>
/// <param name="TenantId">Institute the student belongs to.</param>
/// <param name="MigrationSourceId">Idempotency key — legacy plan row id.
/// Re-running with the same id is a no-op at the migration service.</param>
/// <param name="TargetId">The newly-created ExamTarget id (pointer into
/// the ExamTargetAdded_V1 event that precedes this one).</param>
/// <param name="MigratedAt">Wall-clock of the migration.</param>
public sealed record StudentPlanMigrated_V1(
    string StudentAnonId,
    string TenantId,
    string MigrationSourceId,
    ExamTargetId TargetId,
    DateTimeOffset MigratedAt);
