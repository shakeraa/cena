// =============================================================================
// Cena Platform — ActiveExamTargetPolicy (prr-226, ADR-0050 §10)
//
// Pure deterministic policy that picks THE active exam target a single
// session should run against. Replaces the implicit "single-target" world
// of prr-148 with the multi-target world of prr-218 / ADR-0050.
//
// Selection order (highest priority wins):
//
//   0. Explicit override — if the caller passes an ExamTargetId (e.g. from
//      a just-emitted ExamTargetOverrideApplied_V1 event for this session),
//      honour it unconditionally, provided the target is active. Override
//      suppresses the exam-week lock so the student can intentionally
//      study a different target even during exam week.
//
//   1. Exam-week lock — if ANY active target's canonical sitting date is
//      within <see cref="ExamWeekLockWindow"/> (14 days) of the student's
//      local "today", lock the session to that target. If multiple targets
//      are inside the window, pick the one with the earliest date. Lock
//      state is a SCHEDULER-INTERNAL flag only — callers MUST NOT surface
//      "days until", "exam week", "countdown" copy per ADR-0048 §§1-3 and
//      the shipgate ban in PRR-224.
//
//   2. Deadline proximity — target with the earliest canonical date that
//      has NOT yet passed (catalog resolution null ⇒ target is skipped from
//      proximity ranking, it gets placed at the tail).
//
//   3. Mastery deficit — tie-breaker when two targets have identical (or
//      both-null) canonical dates. Requires a caller-supplied deficit
//      function; returns the target with the LARGER deficit. Present in
//      the public signature so PRR-237 can wire the cross-target
//      interleaving stub into this same policy without a re-signature.
//
//   4. Insertion order — deterministic final tie-breaker.
//
// TZ-safe determinism (persona-sre, prr-157):
//
//   The "today" boundary is computed by converting both <c>nowUtc</c> and
//   each target's resolved canonical date to Israel local time via
//   <see cref="IsraelTimeZoneResolver.ConvertFromUtc"/>, then subtracting
//   the local dates. This gives the same day-delta regardless of the
//   process's own TZ setting. The property tests assert this across
//   {IST, UTC, UTC-8, UTC-5, UTC+10}.
//
// NO LLM on this path (persona-finops, prr-149/224 + ADR-0026).
// The shipgate scanner + SchedulerNoLlmCallTest both guard that. This file
// is pure math + a dictionary lookup.
// =============================================================================

using Cena.Actors.StudentPlan;
using Cena.Infrastructure.Time;

namespace Cena.Actors.Sessions;

/// <summary>
/// Outcome of running <see cref="ActiveExamTargetPolicy.Resolve"/>.
/// </summary>
/// <param name="ActiveTargetId">The chosen active target. Null when the
/// student has zero active targets — callers must tolerate this (e.g.
/// students who completed everything, or cold-start students).</param>
/// <param name="LockedForExamWeek">True when the scheduler should suppress
/// cross-target interleaving and pin the session to <see cref="ActiveTargetId"/>
/// because that target's canonical date is inside the 14-day window.
/// SCHEDULER-INTERNAL: must not be surfaced in UX copy.</param>
/// <param name="Reason">Which branch of the selection order fired. Useful
/// for debug logs and the selection-priority unit tests.</param>
public readonly record struct ActiveExamTargetResolution(
    ExamTargetId? ActiveTargetId,
    bool LockedForExamWeek,
    ActiveTargetSelectionReason Reason);

/// <summary>
/// Which selection rule produced the active target.
/// </summary>
public enum ActiveTargetSelectionReason
{
    /// <summary>No active target available.</summary>
    None = 0,

    /// <summary>Student or scheduler override honoured.</summary>
    Override = 1,

    /// <summary>Exam-week lock at or under 14-day window.</summary>
    ExamWeekLock = 2,

    /// <summary>Earliest not-yet-passed canonical sitting date.</summary>
    DeadlineProximity = 3,

    /// <summary>Tie-break on mastery deficit (larger deficit wins).</summary>
    MasteryDeficit = 4,

    /// <summary>Deterministic fallback — insertion order.</summary>
    InsertionOrder = 5,
}

/// <summary>
/// Pure policy picking the one target a session runs against.
/// </summary>
public static class ActiveExamTargetPolicy
{
    /// <summary>
    /// Length of the silent exam-week lock window. Per ADR-0050 §10 and
    /// ADR-0048, the lock triggers when <c>canonical_date - today ≤ 14 days</c>
    /// in Israel local time. Exposed as a constant so tests can assert the
    /// boundary precisely; callers should NEVER introduce a separate
    /// "14-day" literal or render it in UX copy.
    /// </summary>
    public static readonly TimeSpan ExamWeekLockWindow = TimeSpan.FromDays(14);

    /// <summary>
    /// Optional caller-supplied mastery-deficit function. Higher return
    /// value = larger deficit. Used only as a tie-breaker when two targets
    /// have identical canonical dates or both are unknown. Return 0 if the
    /// caller does not want to differentiate — the fallback becomes
    /// insertion order. PRR-237 will wire the cross-target interleaving
    /// stub into this same signature.
    /// </summary>
    public delegate double MasteryDeficitFunc(ExamTarget target);

    /// <summary>
    /// Resolve the active target for a session.
    /// </summary>
    /// <param name="activeTargets">Caller supplies the active-only list
    /// (see <see cref="IStudentPlanReader.ListTargetsAsync"/> without
    /// <c>includeArchived</c>). Order is treated as insertion order for
    /// the final tie-breaker.</param>
    /// <param name="nowUtc">Wall-clock now. The policy converts to Israel
    /// local time internally — do not pre-convert.</param>
    /// <param name="sittingDateResolver">Resolves sitting tuples to their
    /// canonical UTC date/time. May return null for unknown sittings; those
    /// targets sink to the end of the proximity ranking.</param>
    /// <param name="overrideTargetId">When non-null AND the target is in
    /// <paramref name="activeTargets"/>, short-circuits the rest of the
    /// policy. Override ALSO suppresses the exam-week lock so the student
    /// can intentionally study a different subject during exam week (the
    /// lock is advisory, not coercive).</param>
    /// <param name="deficitFunc">Optional tie-breaker. Null ⇒ insertion
    /// order.</param>
    public static ActiveExamTargetResolution Resolve(
        IReadOnlyList<ExamTarget> activeTargets,
        DateTimeOffset nowUtc,
        ISittingCanonicalDateResolver sittingDateResolver,
        ExamTargetId? overrideTargetId = null,
        MasteryDeficitFunc? deficitFunc = null)
    {
        ArgumentNullException.ThrowIfNull(activeTargets);
        ArgumentNullException.ThrowIfNull(sittingDateResolver);

        if (activeTargets.Count == 0)
        {
            return new ActiveExamTargetResolution(
                ActiveTargetId: null,
                LockedForExamWeek: false,
                Reason: ActiveTargetSelectionReason.None);
        }

        // 0. Override: unconditional if the id matches an active target.
        if (overrideTargetId is { } over)
        {
            var match = activeTargets.FirstOrDefault(t => t.Id == over);
            if (match is not null)
            {
                return new ActiveExamTargetResolution(
                    ActiveTargetId: match.Id,
                    LockedForExamWeek: false,
                    Reason: ActiveTargetSelectionReason.Override);
            }
        }

        // Pre-compute: every target's (canonical date, days-until) pair in
        // Israel-local wall-clock so the boundary is TZ-deterministic.
        var todayIsrael = IsraelTimeZoneResolver.ConvertFromUtc(nowUtc).Date;
        var candidates = new List<(ExamTarget Target, DateTimeOffset? Canonical, double? DaysUntilLocal)>(activeTargets.Count);
        foreach (var t in activeTargets)
        {
            var canon = sittingDateResolver.Resolve(t.Sitting);
            double? daysUntil = null;
            if (canon.HasValue)
            {
                var canonIsrael = IsraelTimeZoneResolver.ConvertFromUtc(canon.Value).Date;
                daysUntil = (canonIsrael - todayIsrael).TotalDays;
            }
            candidates.Add((t, canon, daysUntil));
        }

        // 1. Exam-week lock: any candidate with 0 ≤ daysUntil ≤ 14 wins.
        //    Pick the earliest (smallest non-negative daysUntil).
        var lockCandidate = candidates
            .Where(c => c.DaysUntilLocal is >= 0 and <= 14d)
            .OrderBy(c => c.DaysUntilLocal!.Value)
            .ThenBy(c => IndexOf(activeTargets, c.Target))
            .Cast<(ExamTarget Target, DateTimeOffset? Canonical, double? DaysUntilLocal)?>()
            .FirstOrDefault();

        if (lockCandidate is { } locked)
        {
            return new ActiveExamTargetResolution(
                ActiveTargetId: locked.Target.Id,
                LockedForExamWeek: true,
                Reason: ActiveTargetSelectionReason.ExamWeekLock);
        }

        // 2. Deadline proximity: earliest not-yet-passed canonical date.
        var futureCandidates = candidates
            .Where(c => c.DaysUntilLocal is > 14d)
            .OrderBy(c => c.DaysUntilLocal!.Value)
            .ThenBy(c => IndexOf(activeTargets, c.Target))
            .ToList();

        if (futureCandidates.Count > 0)
        {
            var earliest = futureCandidates[0];
            // If a strict tie on the proximity date and a deficit function
            // is supplied, tie-break by larger deficit.
            if (deficitFunc is not null && futureCandidates.Count > 1)
            {
                var earliestDate = earliest.DaysUntilLocal!.Value;
                var tied = futureCandidates
                    .Where(c => Math.Abs(c.DaysUntilLocal!.Value - earliestDate) < double.Epsilon)
                    .ToList();
                if (tied.Count > 1)
                {
                    var topByDeficit = tied
                        .OrderByDescending(c => deficitFunc(c.Target))
                        .ThenBy(c => IndexOf(activeTargets, c.Target))
                        .First();
                    return new ActiveExamTargetResolution(
                        ActiveTargetId: topByDeficit.Target.Id,
                        LockedForExamWeek: false,
                        Reason: ActiveTargetSelectionReason.MasteryDeficit);
                }
            }

            return new ActiveExamTargetResolution(
                ActiveTargetId: earliest.Target.Id,
                LockedForExamWeek: false,
                Reason: ActiveTargetSelectionReason.DeadlineProximity);
        }

        // 3. Mastery deficit / insertion order: none of the candidates have
        //    a known future canonical date (all unknown, or all in the past
        //    but not inside the lock window). Pick by deficit if supplied,
        //    else by insertion order.
        if (deficitFunc is not null)
        {
            var maxDeficit = double.NegativeInfinity;
            var winner = activeTargets[0];
            foreach (var c in candidates)
            {
                var d = deficitFunc(c.Target);
                if (d > maxDeficit)
                {
                    maxDeficit = d;
                    winner = c.Target;
                }
            }
            if (maxDeficit > 0)
            {
                return new ActiveExamTargetResolution(
                    ActiveTargetId: winner.Id,
                    LockedForExamWeek: false,
                    Reason: ActiveTargetSelectionReason.MasteryDeficit);
            }
        }

        return new ActiveExamTargetResolution(
            ActiveTargetId: activeTargets[0].Id,
            LockedForExamWeek: false,
            Reason: ActiveTargetSelectionReason.InsertionOrder);
    }

    // IReadOnlyList<T> has no IndexOf; rolling our own keeps the policy
    // dependent only on the minimal read-shape contract and avoids coercing
    // callers to List<T>.
    private static int IndexOf(IReadOnlyList<ExamTarget> list, ExamTarget target)
    {
        for (var i = 0; i < list.Count; i++)
            if (list[i].Id == target.Id) return i;
        return int.MaxValue;
    }
}
