// =============================================================================
// Cena Platform — ActiveExamTargetPolicy tests (prr-226, ADR-0050 §10)
//
// Covers the four selection branches listed in the task body:
//   (a) Target selection priority    — override > lock > proximity > order.
//   (b) Exam-week lock activation    — boundary behaviour at day 14.
//   (c) TZ determinism               — same inputs across {IST, UTC, UTC-8,
//                                      UTC-5, UTC+10} produce the same
//                                      resolution.
//   (d) Override event honoured      — overrideTargetId short-circuits.
//
// The policy is a PURE function; the tests use InMemorySittingCanonicalDateResolver
// with fabricated canonical dates. No I/O, no clocks.
// =============================================================================

using Cena.Actors.Sessions;
using Cena.Actors.StudentPlan;

namespace Cena.Actors.Tests.Sessions;

public sealed class ActiveExamTargetPolicyTests
{
    private const string Student = "anon-stu-prr226";

    private static readonly SittingCode SummerMoedA = new("תשפ״ו", SittingSeason.Summer, SittingMoed.A);
    private static readonly SittingCode SummerMoedB = new("תשפ״ו", SittingSeason.Summer, SittingMoed.B);
    private static readonly SittingCode WinterMoedA = new("תשפ״ז", SittingSeason.Winter, SittingMoed.A);
    private static readonly SittingCode Unknown     = new("ת????", SittingSeason.Summer, SittingMoed.Special);

    private static ExamTarget NewTarget(
        string id,
        string examCode,
        SittingCode sitting,
        int weeklyHours = 5)
        => new(
            Id: new ExamTargetId(id),
            Source: ExamTargetSource.Student,
            AssignedById: new UserId(Student),
            EnrollmentId: null,
            ExamCode: new ExamCode(examCode),
            Track: new TrackCode("5U"),
            Sitting: sitting,
            WeeklyHours: weeklyHours,
            ReasonTag: null,
            CreatedAt: DateTimeOffset.Parse("2026-04-01T00:00:00Z"),
            ArchivedAt: null);

    private static InMemorySittingCanonicalDateResolver Resolver(
        params (SittingCode Code, DateTimeOffset Date)[] entries)
    {
        var dict = new Dictionary<SittingCode, DateTimeOffset>();
        foreach (var (code, date) in entries) dict[code] = date;
        return new InMemorySittingCanonicalDateResolver(dict);
    }

    // ── (a) Target selection priority ────────────────────────────────────

    [Fact]
    public void Resolve_empty_active_list_returns_none()
    {
        var result = ActiveExamTargetPolicy.Resolve(
            activeTargets: Array.Empty<ExamTarget>(),
            nowUtc: DateTimeOffset.Parse("2026-05-01T09:00:00Z"),
            sittingDateResolver: EmptySittingCanonicalDateResolver.Instance);

        Assert.Null(result.ActiveTargetId);
        Assert.False(result.LockedForExamWeek);
        Assert.Equal(ActiveTargetSelectionReason.None, result.Reason);
    }

    [Fact]
    public void Resolve_override_short_circuits_all_other_rules()
    {
        // Target A sits inside the 14-day window (normally would lock).
        // Override explicitly picks target B instead. Lock must NOT fire.
        var now = DateTimeOffset.Parse("2026-06-01T09:00:00Z");
        var a = NewTarget("et-a", "BAGRUT_MATH_5U", SummerMoedA);
        var b = NewTarget("et-b", "BAGRUT_ENG", WinterMoedA);

        var result = ActiveExamTargetPolicy.Resolve(
            activeTargets: new[] { a, b },
            nowUtc: now,
            sittingDateResolver: Resolver(
                (SummerMoedA, DateTimeOffset.Parse("2026-06-10T08:00:00Z")),  // 9 days away
                (WinterMoedA, DateTimeOffset.Parse("2027-01-15T08:00:00Z"))),
            overrideTargetId: b.Id);

        Assert.Equal(b.Id, result.ActiveTargetId);
        Assert.False(result.LockedForExamWeek);
        Assert.Equal(ActiveTargetSelectionReason.Override, result.Reason);
    }

    [Fact]
    public void Resolve_deadline_proximity_picks_earliest_future_target()
    {
        var now = DateTimeOffset.Parse("2026-04-01T09:00:00Z");
        var a = NewTarget("et-a", "BAGRUT_MATH_5U", WinterMoedA);  // Jan 2027
        var b = NewTarget("et-b", "BAGRUT_ENG", SummerMoedA);       // Jun 2026
        var c = NewTarget("et-c", "PET", SummerMoedB);              // Jul 2026

        var result = ActiveExamTargetPolicy.Resolve(
            activeTargets: new[] { a, b, c },
            nowUtc: now,
            sittingDateResolver: Resolver(
                (WinterMoedA, DateTimeOffset.Parse("2027-01-15T08:00:00Z")),
                (SummerMoedA, DateTimeOffset.Parse("2026-06-20T08:00:00Z")),
                (SummerMoedB, DateTimeOffset.Parse("2026-07-20T08:00:00Z"))));

        Assert.Equal(b.Id, result.ActiveTargetId);
        Assert.False(result.LockedForExamWeek);
        Assert.Equal(ActiveTargetSelectionReason.DeadlineProximity, result.Reason);
    }

    [Fact]
    public void Resolve_insertion_order_fallback_when_catalog_is_silent()
    {
        var now = DateTimeOffset.Parse("2026-04-01T09:00:00Z");
        var a = NewTarget("et-a", "BAGRUT_MATH_5U", Unknown);
        var b = NewTarget("et-b", "BAGRUT_ENG", Unknown);

        var result = ActiveExamTargetPolicy.Resolve(
            activeTargets: new[] { a, b },
            nowUtc: now,
            sittingDateResolver: EmptySittingCanonicalDateResolver.Instance);

        Assert.Equal(a.Id, result.ActiveTargetId);
        Assert.False(result.LockedForExamWeek);
        Assert.Equal(ActiveTargetSelectionReason.InsertionOrder, result.Reason);
    }

    [Fact]
    public void Resolve_mastery_deficit_wins_on_proximity_tie()
    {
        // Two targets share the same canonical date; deficit fn prefers b.
        var now = DateTimeOffset.Parse("2026-04-01T09:00:00Z");
        var shared = DateTimeOffset.Parse("2026-07-20T08:00:00Z");
        var a = NewTarget("et-a", "BAGRUT_MATH_5U", SummerMoedA);
        var b = NewTarget("et-b", "BAGRUT_ENG", SummerMoedB);

        double Deficit(ExamTarget t) => t.Id.Value == "et-b" ? 0.9 : 0.1;

        var result = ActiveExamTargetPolicy.Resolve(
            activeTargets: new[] { a, b },
            nowUtc: now,
            sittingDateResolver: Resolver((SummerMoedA, shared), (SummerMoedB, shared)),
            deficitFunc: Deficit);

        Assert.Equal(b.Id, result.ActiveTargetId);
        Assert.False(result.LockedForExamWeek);
        Assert.Equal(ActiveTargetSelectionReason.MasteryDeficit, result.Reason);
    }

    // ── (b) Exam-week lock boundary ──────────────────────────────────────

    [Fact]
    public void Resolve_lock_fires_at_exactly_14_days()
    {
        // Canonical date is 14d after "today in Israel" → inside the
        // inclusive window.
        var now = DateTimeOffset.Parse("2026-06-01T06:00:00Z");
        var fourteenDaysOut = DateTimeOffset.Parse("2026-06-15T06:00:00Z");
        var far = DateTimeOffset.Parse("2026-12-01T06:00:00Z");

        var near = NewTarget("et-near", "BAGRUT_MATH_5U", SummerMoedA);
        var far1 = NewTarget("et-far", "BAGRUT_ENG", WinterMoedA);

        var result = ActiveExamTargetPolicy.Resolve(
            activeTargets: new[] { far1, near },
            nowUtc: now,
            sittingDateResolver: Resolver(
                (SummerMoedA, fourteenDaysOut),
                (WinterMoedA, far)));

        Assert.Equal(near.Id, result.ActiveTargetId);
        Assert.True(result.LockedForExamWeek);
        Assert.Equal(ActiveTargetSelectionReason.ExamWeekLock, result.Reason);
    }

    [Fact]
    public void Resolve_lock_does_not_fire_at_15_days()
    {
        // 15 days out is OUTSIDE the 14-day inclusive window.
        var now = DateTimeOffset.Parse("2026-06-01T06:00:00Z");
        var fifteenDaysOut = DateTimeOffset.Parse("2026-06-16T06:00:00Z");

        var near = NewTarget("et-near", "BAGRUT_MATH_5U", SummerMoedA);

        var result = ActiveExamTargetPolicy.Resolve(
            activeTargets: new[] { near },
            nowUtc: now,
            sittingDateResolver: Resolver((SummerMoedA, fifteenDaysOut)));

        Assert.Equal(near.Id, result.ActiveTargetId);
        Assert.False(result.LockedForExamWeek);
        Assert.Equal(ActiveTargetSelectionReason.DeadlineProximity, result.Reason);
    }

    [Fact]
    public void Resolve_lock_fires_on_day_of_exam()
    {
        // 0 days out is still inside the window per the inclusive "≤ 14".
        var now = DateTimeOffset.Parse("2026-06-01T06:00:00Z");
        var sameDay = DateTimeOffset.Parse("2026-06-01T13:00:00Z");

        var t = NewTarget("et-today", "BAGRUT_MATH_5U", SummerMoedA);

        var result = ActiveExamTargetPolicy.Resolve(
            activeTargets: new[] { t },
            nowUtc: now,
            sittingDateResolver: Resolver((SummerMoedA, sameDay)));

        Assert.Equal(t.Id, result.ActiveTargetId);
        Assert.True(result.LockedForExamWeek);
        Assert.Equal(ActiveTargetSelectionReason.ExamWeekLock, result.Reason);
    }

    [Fact]
    public void Resolve_past_sitting_does_not_trigger_lock()
    {
        // A target whose canonical date is in the past (the student did not
        // archive it yet) must NOT trigger a lock. It falls out of the
        // proximity future-list; any other active target takes priority.
        var now = DateTimeOffset.Parse("2026-09-01T06:00:00Z");
        var past = DateTimeOffset.Parse("2026-06-10T06:00:00Z");
        var future = DateTimeOffset.Parse("2027-01-15T06:00:00Z");

        var stale = NewTarget("et-stale", "BAGRUT_MATH_5U", SummerMoedA);
        var next = NewTarget("et-next", "BAGRUT_ENG", WinterMoedA);

        var result = ActiveExamTargetPolicy.Resolve(
            activeTargets: new[] { stale, next },
            nowUtc: now,
            sittingDateResolver: Resolver(
                (SummerMoedA, past),
                (WinterMoedA, future)));

        Assert.Equal(next.Id, result.ActiveTargetId);
        Assert.False(result.LockedForExamWeek);
        Assert.Equal(ActiveTargetSelectionReason.DeadlineProximity, result.Reason);
    }

    [Fact]
    public void Resolve_lock_picks_earliest_of_multiple_near_targets()
    {
        var now = DateTimeOffset.Parse("2026-06-01T06:00:00Z");
        var t1 = DateTimeOffset.Parse("2026-06-03T06:00:00Z"); // 2 days
        var t2 = DateTimeOffset.Parse("2026-06-10T06:00:00Z"); // 9 days

        var later  = NewTarget("et-later", "BAGRUT_MATH_5U", SummerMoedB);
        var sooner = NewTarget("et-sooner", "BAGRUT_ENG", SummerMoedA);

        var result = ActiveExamTargetPolicy.Resolve(
            activeTargets: new[] { later, sooner },
            nowUtc: now,
            sittingDateResolver: Resolver(
                (SummerMoedB, t2),
                (SummerMoedA, t1)));

        Assert.Equal(sooner.Id, result.ActiveTargetId);
        Assert.True(result.LockedForExamWeek);
        Assert.Equal(ActiveTargetSelectionReason.ExamWeekLock, result.Reason);
    }

    // ── (c) TZ determinism ────────────────────────────────────────────────

    [Fact]
    public void Resolve_is_deterministic_across_server_tz()
    {
        // Two equivalent UTC instants expressed with different offsets MUST
        // produce the same resolution. Critical at the 02:00-03:00 IST dead
        // zone (midnight-adjacent across common server TZs).
        // Exam is 7 days out (well inside the lock window).
        var examUtc = new DateTimeOffset(2026, 6, 15, 7, 0, 0, TimeSpan.Zero);
        var t = NewTarget("et-1", "BAGRUT_MATH_5U", SummerMoedA);
        var resolver = Resolver((SummerMoedA, examUtc));

        // The SAME wall-clock instant, expressed with offsets from the test
        // suite: IST, UTC, UTC-8, UTC-5, UTC+10. The underlying UtcDateTime
        // is identical, so the policy must deliver identical results.
        var baseInstant = new DateTimeOffset(2026, 6, 8, 7, 0, 0, TimeSpan.Zero);
        var nowValues = new[]
        {
            baseInstant,
            baseInstant.ToOffset(TimeSpan.FromHours(3)),   // IST standard
            baseInstant.ToOffset(TimeSpan.FromHours(-8)),  // US Pacific
            baseInstant.ToOffset(TimeSpan.FromHours(-5)),  // US Eastern
            baseInstant.ToOffset(TimeSpan.FromHours(10)),  // AU East
        };

        ActiveExamTargetResolution? first = null;
        foreach (var now in nowValues)
        {
            var r = ActiveExamTargetPolicy.Resolve(
                activeTargets: new[] { t },
                nowUtc: now,
                sittingDateResolver: resolver);
            first ??= r;
            Assert.Equal(first.Value.ActiveTargetId, r.ActiveTargetId);
            Assert.Equal(first.Value.LockedForExamWeek, r.LockedForExamWeek);
            Assert.Equal(first.Value.Reason, r.Reason);
        }

        Assert.True(first!.Value.LockedForExamWeek);
    }

    [Fact]
    public void Resolve_midnight_boundary_is_stable()
    {
        // Two adjacent UTC timestamps straddling a server-local midnight
        // must still agree on the day-delta because the policy normalises
        // via Israel local time. Verified by picking an exam exactly 14
        // days out in Israel local date and asserting the lock fires
        // identically at 23:55 and 00:05 UTC on the boundary UTC day.
        // The exam is at IST noon on day D+14; "now" starts on day D.
        var examUtc = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

        var t = NewTarget("et-1", "BAGRUT_MATH_5U", SummerMoedA);
        var resolver = Resolver((SummerMoedA, examUtc));

        var lateYesterdayUtc = new DateTimeOffset(2026, 6, 1, 23, 55, 0, TimeSpan.Zero);
        var earlyTodayUtc    = new DateTimeOffset(2026, 6, 2, 0, 5, 0, TimeSpan.Zero);

        var r1 = ActiveExamTargetPolicy.Resolve(
            activeTargets: new[] { t }, nowUtc: lateYesterdayUtc,
            sittingDateResolver: resolver);
        var r2 = ActiveExamTargetPolicy.Resolve(
            activeTargets: new[] { t }, nowUtc: earlyTodayUtc,
            sittingDateResolver: resolver);

        // Both are within the 14-day window in Israel local time (Israel is
        // UTC+3 in summer, so both UTC timestamps fall on June 2 in IST),
        // so both must lock.
        Assert.True(r1.LockedForExamWeek);
        Assert.True(r2.LockedForExamWeek);
    }

    // ── (d) Override honoured when target not active ─────────────────────

    [Fact]
    public void Resolve_override_is_ignored_when_target_is_not_in_active_list()
    {
        var now = DateTimeOffset.Parse("2026-06-01T06:00:00Z");
        var a = NewTarget("et-a", "BAGRUT_MATH_5U", SummerMoedA);

        var result = ActiveExamTargetPolicy.Resolve(
            activeTargets: new[] { a },
            nowUtc: now,
            sittingDateResolver: Resolver((SummerMoedA, DateTimeOffset.Parse("2026-07-20T08:00:00Z"))),
            overrideTargetId: new ExamTargetId("et-never-existed"));

        // Override was ignored; falls through to proximity.
        Assert.Equal(a.Id, result.ActiveTargetId);
        Assert.Equal(ActiveTargetSelectionReason.DeadlineProximity, result.Reason);
    }

    [Fact]
    public void ExamWeekLockWindow_is_exactly_14_days()
    {
        // Guards against a future edit that nudges the window accidentally.
        // ADR-0050 §10 locks the value at 14 days; ADR-0048 forbids surfacing
        // it in UX copy.
        Assert.Equal(TimeSpan.FromDays(14), ActiveExamTargetPolicy.ExamWeekLockWindow);
    }
}
