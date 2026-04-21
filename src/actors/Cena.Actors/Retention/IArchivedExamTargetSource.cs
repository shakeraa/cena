// =============================================================================
// Cena Platform — Archived exam-target enumeration source (prr-229)
//
// The retention worker needs to enumerate every archived target across
// every student to decide which ones are past the 24-month window. That
// query lives either in the StudentPlan aggregate store (prr-218) OR in
// a Marten projection once the full multi-target event model lands.
//
// Until then, this interface isolates the worker from the underlying
// store shape: the worker asks for "all archived targets", the impl
// returns ArchivedExamTarget records. Phase-1 ships an in-memory impl
// driven from the same event log the ExamTargetErasureCascade reads;
// Phase-2 swaps in a Marten-backed projection without touching the
// worker.
// =============================================================================

using Cena.Actors.ExamTargets;

namespace Cena.Actors.Retention;

/// <summary>
/// Flat projection row for an archived exam target. Carries the minimum
/// the retention worker needs: student id, target id, archive anchor,
/// and the student's retention-extension flag state cached at archive
/// time (so the worker doesn't need to cross-join with the extension
/// store per row — though the worker DOES re-verify against the live
/// store before shredding to honour post-archive opt-ins).
/// </summary>
/// <param name="StudentAnonId">Pseudonymous student id.</param>
/// <param name="ExamTargetCode">The archived target's catalog code.</param>
/// <param name="ArchivedAtUtc">
/// Wall-clock of the archive event; retention clock anchor per
/// <see cref="ExamTargetRetentionPolicy"/>.
/// </param>
/// <param name="TenantId">
/// ADR-0001 tenant scope. Null for cross-tenant (self-enrolled) students.
/// Workers MUST respect tenant isolation — never shred a target outside
/// the caller's tenant scope.
/// </param>
public sealed record ArchivedExamTargetRow(
    string StudentAnonId,
    ExamTargetCode ExamTargetCode,
    DateTimeOffset ArchivedAtUtc,
    string? TenantId);

/// <summary>
/// Source of archived exam targets for the retention worker.
/// </summary>
public interface IArchivedExamTargetSource
{
    /// <summary>
    /// Enumerate every archived target that is CURRENTLY archived. The
    /// worker filters by "past its retention horizon" itself against the
    /// policy + live extension store; the source's job is purely to
    /// enumerate, not decide.
    /// </summary>
    IAsyncEnumerable<ArchivedExamTargetRow> ListArchivedAsync(
        CancellationToken ct = default);
}

/// <summary>
/// Notifier invoked when a target has been shredded so the student can
/// be informed ("your archived Bagrut-Math-5U target's data has been
/// removed per your retention settings"). Implementations route through
/// the existing notification dispatcher.
/// </summary>
public interface IRetentionShredNotifier
{
    /// <summary>
    /// Notify the student that a target's data was shredded. Idempotent:
    /// replaying the same (student, target) pair must not re-fire the
    /// notification.
    /// </summary>
    Task NotifyShreddedAsync(
        string studentAnonId,
        ExamTargetCode examTargetCode,
        DateTimeOffset shreddedAtUtc,
        CancellationToken ct = default);
}
