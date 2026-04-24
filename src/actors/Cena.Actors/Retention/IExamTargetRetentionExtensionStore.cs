// =============================================================================
// Cena Platform — ExamTarget retention-extension store contract (prr-229)
//
// Holds the per-student opt-in flag for the 60-month extended retention
// window (ADR-0050 §6, prr-229). The flag is a profile-bit — one row per
// student; idempotent set / clear; queried by the retention worker on
// every sweep.
//
// Semantics:
//   - Absence of a row → default 24-month retention.
//   - Row present with ExtendedUntilUtc > nowUtc → 60-month window active.
//   - Row present with ExtendedUntilUtc <= nowUtc → treat as absent
//     (extension expired; student must opt-in again).
//
// The extension is itself time-bounded at the profile level so a student
// who set the flag and then forgot about it doesn't keep their data
// indefinitely.
// =============================================================================

namespace Cena.Actors.Retention;

/// <summary>
/// A single retention-extension opt-in row.
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id.</param>
/// <param name="SetAtUtc">When the student opted in.</param>
/// <param name="ExtendedUntilUtc">
/// When the extension expires — typically <c>SetAtUtc + 60 months</c>
/// per ADR-0050 §6 (MaxExtendedRetentionMonths). Callers compute this
/// once at opt-in time and persist it; the retention worker compares
/// against its clock on each sweep.
/// </param>
public sealed record ExamTargetRetentionExtension(
    string StudentAnonId,
    DateTimeOffset SetAtUtc,
    DateTimeOffset ExtendedUntilUtc);

/// <summary>
/// Repository for the per-student retention-extension flag.
/// </summary>
public interface IExamTargetRetentionExtensionStore
{
    /// <summary>
    /// Is the student's 60-month extension currently active (row present
    /// AND not expired against <paramref name="nowUtc"/>)?
    /// </summary>
    Task<bool> IsExtendedAsync(
        string studentAnonId,
        DateTimeOffset nowUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Set the opt-in. Idempotent — overwrites any existing row. Audit
    /// logging is the caller's responsibility (the endpoint logs via
    /// the standard <c>[SIEM]</c> channel).
    /// </summary>
    Task SetAsync(
        ExamTargetRetentionExtension extension,
        CancellationToken ct = default);

    /// <summary>
    /// Load the raw extension row for a student, if any. Returns
    /// <c>null</c> when no row exists. Used by the admin dashboard to
    /// show who has opted in.
    /// </summary>
    Task<ExamTargetRetentionExtension?> TryGetAsync(
        string studentAnonId,
        CancellationToken ct = default);

    /// <summary>
    /// Delete the row for a student. Called by the RTBF cascade. Returns
    /// <c>true</c> if a row existed, <c>false</c> otherwise (idempotent).
    /// </summary>
    Task<bool> DeleteAsync(
        string studentAnonId,
        CancellationToken ct = default);
}
