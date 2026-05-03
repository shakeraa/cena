// =============================================================================
// Cena Platform — LearningSession event: SessionInterleavingPlanned_V1 (prr-237)
//
// Appended to the `session-{SessionId}` stream immediately after the
// SessionPlanGenerator has run the InterleavingPolicy for a newly-started
// session that has >1 active ExamTargets AND is NOT exam-week-locked.
// Session-scoped — NEVER appended to the student stream (ADR-0003
// session-scope rule + the prr-149 SessionScopedSnapshot guard).
//
// Purpose: audit trail for the cross-target interleaving decision. Ops
// needs to answer "for session X, how many slots went to each target,
// and why did we not interleave on session Y?". The event body carries
// the full allocation breakdown so the answer is a single stream scan,
// not a log dive.
//
// Research citation (persona-cogsci sign-off): interleaved practice
// produces discrimination-learning gains of d ≈ 0.34 per the Brunmair
// (2019) meta-analysis over 59 studies, with Rohrer & Taylor (2007)
// as the canonical single-study reference. We DO NOT cite the Rohrer
// single-study d = 1.05 (Honest-not-complimentary memory + ADR-0049
// citation-integrity rule + the "no overstated effect sizes" DoD in the
// PRR-237 task body).
// =============================================================================

using Cena.Actors.StudentPlan;

namespace Cena.Actors.Sessions.Events;

/// <summary>
/// One target's allocation inside <see cref="SessionInterleavingPlanned_V1"/>.
/// Mirrors <see cref="InterleavingPolicy"/>'s per-target bucket breakdown in
/// a wire-stable shape (bucket weight is a plain double, not a value type).
/// </summary>
/// <param name="TargetId">Stable ExamTarget id as a raw string (wire-stable
/// — consumers must not assume the
/// <see cref="Cena.Actors.StudentPlan.ExamTargetId"/> constructor shape).</param>
/// <param name="ExamCode">Catalog ExamCode string (e.g. "BAGRUT_MATH_5U").
/// Operational label for Prometheus slicing, never PII.</param>
/// <param name="Slots">Number of topic slots this target received in the
/// interleaved plan.</param>
/// <param name="BucketWeight">Raw bucket weight (WeeklyHours × deficit)
/// used to compute <see cref="Slots"/>. Logged verbatim so an audit can
/// replay the allocation arithmetic without a scheduler round-trip.</param>
public sealed record SessionInterleavingAllocation_V1(
    string TargetId,
    string ExamCode,
    int Slots,
    double BucketWeight);

/// <summary>
/// Event marking that <see cref="InterleavingPolicy.Plan"/> was run for
/// the session <paramref name="SessionId"/>.
/// <para>
/// Emitted for BOTH the interleaved-path sessions (audit of the mix) and
/// the short-circuit paths (audit of WHY we stayed single-target — e.g.
/// exam-week lock). Consumers distinguish via
/// <see cref="DisabledReasonTag"/>.
/// </para>
/// <para>
/// Stream: <c>session-{SessionId}</c>. Never appended to the student
/// stream (session-scope per ADR-0003).
/// </para>
/// </summary>
/// <param name="StudentAnonId">Session owner, anon id form.</param>
/// <param name="SessionId">Session stream id.</param>
/// <param name="PlannedAtUtc">Wall-clock time of the interleaving run.</param>
/// <param name="Enabled">True iff interleaving actually ran (<see cref="Allocations"/>
/// carries the per-target slot split). False when
/// <see cref="DisabledReasonTag"/> names the short-circuit.</param>
/// <param name="DisabledReasonTag">One of: "not-disabled", "exam-week-lock",
/// "single-or-zero-targets", "only-one-target-has-candidates". Enum-string
/// form (not free-text) so the shipgate scanner can whitelist the tag set.</param>
/// <param name="Allocations">Per-target slot breakdown in insertion order
/// (stable with the student's active-target list). Empty when
/// <see cref="Enabled"/> is false.</param>
/// <param name="TotalSlots">Σ <see cref="SessionInterleavingAllocation_V1.Slots"/>
/// across <paramref name="Allocations"/>. 0 when disabled.</param>
public sealed record SessionInterleavingPlanned_V1(
    string StudentAnonId,
    string SessionId,
    DateTimeOffset PlannedAtUtc,
    bool Enabled,
    string DisabledReasonTag,
    IReadOnlyList<SessionInterleavingAllocation_V1> Allocations,
    int TotalSlots)
{
    /// <summary>Wire-stable tag strings for <see cref="DisabledReasonTag"/>.</summary>
    public static class ReasonTags
    {
        /// <summary>Interleaving actually ran — <see cref="Enabled"/> is true.</summary>
        public const string NotDisabled = "not-disabled";

        /// <summary>Caller's <c>lockedForExamWeek</c> flag was true —
        /// wave-2 single-target behaviour preserved.</summary>
        public const string ExamWeekLock = "exam-week-lock";

        /// <summary>Fewer than 2 targets on the plan; nothing to
        /// interleave against.</summary>
        public const string SingleOrZeroTargets = "single-or-zero-targets";

        /// <summary>Only one target had non-empty candidates; interleaving
        /// would degenerate to single-target.</summary>
        public const string OnlyOneTargetHasCandidates = "only-one-target-has-candidates";
    }

    /// <summary>
    /// Map an <see cref="InterleavingDisabledReason"/> to the wire-stable
    /// string tag used on this event.
    /// </summary>
    public static string TagFromReason(InterleavingDisabledReason reason)
        => reason switch
        {
            InterleavingDisabledReason.NotDisabled => ReasonTags.NotDisabled,
            InterleavingDisabledReason.ExamWeekLock => ReasonTags.ExamWeekLock,
            InterleavingDisabledReason.SingleOrZeroTargets => ReasonTags.SingleOrZeroTargets,
            InterleavingDisabledReason.OnlyOneTargetHasCandidates =>
                ReasonTags.OnlyOneTargetHasCandidates,
            _ => ReasonTags.NotDisabled,
        };
}

/// <summary>
/// Sink for <see cref="SessionInterleavingPlanned_V1"/> events. Production
/// wires the Marten event-store appender; tests inject an in-memory
/// collector. Optional on the generator — null sink means "do not audit",
/// which is the default for the wave-2 compatibility path.
/// </summary>
public interface ISessionInterleavingAuditSink
{
    /// <summary>Append an interleaving-audit event to the session stream.</summary>
    Task AppendAsync(SessionInterleavingPlanned_V1 @event, CancellationToken ct = default);
}

/// <summary>No-op sink for hosts that have not wired an audit pipeline.</summary>
public sealed class NullSessionInterleavingAuditSink : ISessionInterleavingAuditSink
{
    /// <summary>Singleton.</summary>
    public static readonly NullSessionInterleavingAuditSink Instance = new();

    private NullSessionInterleavingAuditSink() { }

    /// <inheritdoc />
    public Task AppendAsync(SessionInterleavingPlanned_V1 @event, CancellationToken ct = default)
        => Task.CompletedTask;
}
