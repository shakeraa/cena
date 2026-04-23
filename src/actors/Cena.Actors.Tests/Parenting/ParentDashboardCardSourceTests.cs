// =============================================================================
// Cena Platform — ParentDashboardCardSource tests (EPIC-PRR-I PRR-320)
//
// Locks two surfaces:
//
//   1. NoopParentDashboardCardSource contract — the legitimate zero-data
//      default returns an empty bundle so GetOrZero is the source of
//      truth for every linked student.
//
//   2. MartenParentDashboardCardSource.Fold — the pure folding function
//      that reduces a stream of HintRequested_V1 + timestamp pairs to
//      per-student cards. Exercised directly (no IDocumentStore mock)
//      so every invariant has a dedicated assertion:
//        • v1 minutes-proxy multiplier applied (memory "Labels match data":
//          the DTO says WeeklyMinutes, the proxy is 2 min / hint event)
//        • window boundaries (inclusive at monthlyStart / weeklyStart,
//          inclusive at now, exclude anything outside)
//        • distinct ConceptId count for TopicsPracticed
//        • most-recent event timestamp wins for LastActiveAt
//        • cross-student isolation (events from unlinked ids ignored)
//        • empty linked-student list → empty bundle
//        • null arg → ArgumentNullException
//        • ReadinessScore always null (explicitly deferred — PRR-320
//          scope documentation)
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Subscriptions;
using Cena.Api.Contracts.Parenting;
using Xunit;

namespace Cena.Actors.Tests.Parenting;

public class ParentDashboardCardSourceTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset Now =
        new(2026, 04, 20, 12, 00, 00, TimeSpan.Zero);

    private static LinkedStudent L(string encId, int ordinal = 0) =>
        new(StudentSubjectIdEncrypted: encId,
            Ordinal: ordinal,
            Tier: SubscriptionTier.Premium,
            LinkedAt: Now.AddDays(-60));

    private static MartenParentDashboardCardSource.HintEventEntry HintAt(
        string studentId,
        DateTimeOffset ts,
        string conceptId = "c1") =>
        new(new HintRequested_V1(
                StudentId: studentId,
                SessionId: "s1",
                ConceptId: conceptId,
                QuestionId: "q1",
                HintLevel: 1),
            ts);

    // ── Noop contract: empty bundle with the supplied now stamp ─────────────

    [Fact]
    public async Task Noop_returns_empty_bundle_stamped_with_now()
    {
        var sut = new NoopParentDashboardCardSource();
        var linked = new List<LinkedStudent>
        {
            L("enc::alice"),
            L("enc::bob", 1),
        };

        var bundle = await sut.BuildAsync(linked, Now, CancellationToken.None);

        Assert.NotNull(bundle);
        Assert.Equal(Now, bundle.ComputedAtUtc);
        // Empty dictionary: per-student lookups fall through to GetOrZero.
        Assert.Empty(bundle.PerStudent);

        var zeroCard = bundle.GetOrZero("enc::alice");
        Assert.Equal(0, zeroCard.WeeklyMinutes);
        Assert.Equal(0, zeroCard.MonthlyMinutes);
        Assert.Equal(0, zeroCard.TopicsPracticed);
        Assert.Null(zeroCard.LastActiveAt);
        Assert.Null(zeroCard.ReadinessScore);
    }

    [Fact]
    public async Task Noop_throws_on_null_linked_students()
    {
        var sut = new NoopParentDashboardCardSource();
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await sut.BuildAsync(null!, Now, CancellationToken.None));
    }

    // ── GetOrZero: defensive against unknown ids + empty string ─────────────

    [Fact]
    public void GetOrZero_returns_zero_card_for_unknown_id_and_empty_string()
    {
        var bundle = ParentDashboardCards.Empty(Now);
        var a = bundle.GetOrZero("unknown");
        var b = bundle.GetOrZero(string.Empty);
        Assert.Equal(0, a.WeeklyMinutes);
        Assert.Equal(0, b.WeeklyMinutes);
        Assert.Null(a.LastActiveAt);
        Assert.Null(b.LastActiveAt);
    }

    // ── Fold: empty linked set → empty bundle ───────────────────────────────

    [Fact]
    public void Fold_empty_linked_set_returns_empty_bundle()
    {
        var events = new List<MartenParentDashboardCardSource.HintEventEntry>
        {
            HintAt("enc::alice", Now.AddDays(-1)),
        };

        var bundle = MartenParentDashboardCardSource.Fold(
            events, new HashSet<string>(StringComparer.Ordinal), Now);

        Assert.Empty(bundle.PerStudent);
        Assert.Equal(Now, bundle.ComputedAtUtc);
    }

    // ── Fold: v1 minutes-proxy multiplier (2 min/hint event) ────────────────
    //
    // Memory "Labels match data": WeeklyMinutes / MonthlyMinutes are in
    // minutes — the fold MUST multiply the hint-event count by
    // MinutesPerEventProxy. If we ever decide to replace the proxy with
    // a real projection, this test pins the exact spot that changes.

    [Fact]
    public void Fold_applies_v1_minutes_proxy_multiplier_to_event_count()
    {
        // 3 events in the last 7 days (weekly) + 2 more in the 8-30d
        // range (monthly only). Weekly = 3, Monthly = 5. With proxy=2
        // min/event, WeeklyMinutes = 6, MonthlyMinutes = 10.
        var linked = new HashSet<string>(new[] { "enc::alice" }, StringComparer.Ordinal);
        var events = new List<MartenParentDashboardCardSource.HintEventEntry>
        {
            HintAt("enc::alice", Now.AddDays(-1)),  // weekly
            HintAt("enc::alice", Now.AddDays(-2)),  // weekly
            HintAt("enc::alice", Now.AddDays(-3)),  // weekly
            HintAt("enc::alice", Now.AddDays(-10)), // monthly only
            HintAt("enc::alice", Now.AddDays(-20)), // monthly only
        };

        var bundle = MartenParentDashboardCardSource.Fold(events, linked, Now);

        Assert.True(bundle.PerStudent.ContainsKey("enc::alice"));
        var card = bundle.PerStudent["enc::alice"];
        // Proxy: 3 weekly events × 2 min = 6 WeeklyMinutes.
        Assert.Equal(3 * MartenParentDashboardCardSource.MinutesPerEventProxy,
                     card.WeeklyMinutes);
        Assert.Equal(6, card.WeeklyMinutes);
        // 5 monthly events × 2 min = 10 MonthlyMinutes.
        Assert.Equal(5 * MartenParentDashboardCardSource.MinutesPerEventProxy,
                     card.MonthlyMinutes);
        Assert.Equal(10, card.MonthlyMinutes);
    }

    // ── Fold: distinct ConceptId count ──────────────────────────────────────

    [Fact]
    public void Fold_topics_practiced_is_distinct_concept_count_in_monthly_window()
    {
        // Alice: c1 (3x), c2 (1x), c3 (1x) — distinct = 3.
        var linked = new HashSet<string>(new[] { "enc::alice" }, StringComparer.Ordinal);
        var events = new List<MartenParentDashboardCardSource.HintEventEntry>
        {
            HintAt("enc::alice", Now.AddDays(-1), conceptId: "c1"),
            HintAt("enc::alice", Now.AddDays(-2), conceptId: "c1"),
            HintAt("enc::alice", Now.AddDays(-3), conceptId: "c1"),
            HintAt("enc::alice", Now.AddDays(-4), conceptId: "c2"),
            HintAt("enc::alice", Now.AddDays(-25), conceptId: "c3"),
        };

        var bundle = MartenParentDashboardCardSource.Fold(events, linked, Now);

        Assert.Equal(3, bundle.PerStudent["enc::alice"].TopicsPracticed);
    }

    // ── Fold: last-active timestamp = max event timestamp ───────────────────

    [Fact]
    public void Fold_last_active_is_most_recent_in_window_event_timestamp()
    {
        var linked = new HashSet<string>(new[] { "enc::alice" }, StringComparer.Ordinal);
        var expectedMax = Now.AddHours(-3);
        var events = new List<MartenParentDashboardCardSource.HintEventEntry>
        {
            HintAt("enc::alice", Now.AddDays(-5)),
            HintAt("enc::alice", Now.AddDays(-1)),
            HintAt("enc::alice", expectedMax),   // most recent
            HintAt("enc::alice", Now.AddDays(-10)),
        };

        var bundle = MartenParentDashboardCardSource.Fold(events, linked, Now);

        Assert.Equal(expectedMax, bundle.PerStudent["enc::alice"].LastActiveAt);
    }

    // ── Fold: window exclusion (out-of-window events ignored) ───────────────

    [Fact]
    public void Fold_excludes_events_outside_the_monthly_window()
    {
        var linked = new HashSet<string>(new[] { "enc::alice" }, StringComparer.Ordinal);
        var events = new List<MartenParentDashboardCardSource.HintEventEntry>
        {
            HintAt("enc::alice", Now.AddDays(-31)),   // too old
            HintAt("enc::alice", Now.AddDays(-45)),   // too old
            HintAt("enc::alice", Now.AddMinutes(+1)), // future: excluded
        };

        var bundle = MartenParentDashboardCardSource.Fold(events, linked, Now);

        // No events in-window → student is absent from the dictionary
        // (GetOrZero fallback then returns all zeros).
        Assert.False(bundle.PerStudent.ContainsKey("enc::alice"));
        var card = bundle.GetOrZero("enc::alice");
        Assert.Equal(0, card.WeeklyMinutes);
        Assert.Equal(0, card.MonthlyMinutes);
        Assert.Equal(0, card.TopicsPracticed);
        Assert.Null(card.LastActiveAt);
    }

    // ── Fold: cross-student isolation ───────────────────────────────────────

    [Fact]
    public void Fold_events_from_unlinked_students_do_not_contaminate_linked_cards()
    {
        // Linked set has Alice only; Bob's events in the window are
        // ignored and must NOT bleed into Alice's card.
        var linked = new HashSet<string>(new[] { "enc::alice" }, StringComparer.Ordinal);
        var events = new List<MartenParentDashboardCardSource.HintEventEntry>
        {
            HintAt("enc::alice", Now.AddDays(-1), "c1"),
            HintAt("enc::bob",   Now.AddDays(-1), "c99"),
            HintAt("enc::bob",   Now.AddDays(-2), "c99"),
            HintAt("enc::bob",   Now.AddDays(-3), "c99"),
        };

        var bundle = MartenParentDashboardCardSource.Fold(events, linked, Now);

        // Alice got exactly her one event.
        var alice = bundle.PerStudent["enc::alice"];
        Assert.Equal(1 * MartenParentDashboardCardSource.MinutesPerEventProxy,
                     alice.MonthlyMinutes);
        Assert.Equal(1, alice.TopicsPracticed);
        // Bob never appears — unlinked.
        Assert.False(bundle.PerStudent.ContainsKey("enc::bob"));
    }

    // ── Fold: multiple linked students, per-student accumulators separate ────

    [Fact]
    public void Fold_multiple_linked_students_events_do_not_cross_contaminate()
    {
        var linked = new HashSet<string>(
            new[] { "enc::alice", "enc::bob" }, StringComparer.Ordinal);
        var events = new List<MartenParentDashboardCardSource.HintEventEntry>
        {
            // Alice: 2 weekly events, 1 topic
            HintAt("enc::alice", Now.AddDays(-1), "c1"),
            HintAt("enc::alice", Now.AddDays(-2), "c1"),
            // Bob: 1 weekly event + 1 monthly-only event, 2 topics
            HintAt("enc::bob", Now.AddDays(-3), "c2"),
            HintAt("enc::bob", Now.AddDays(-15), "c3"),
        };

        var bundle = MartenParentDashboardCardSource.Fold(events, linked, Now);

        var alice = bundle.PerStudent["enc::alice"];
        var bob = bundle.PerStudent["enc::bob"];

        // Alice: both events are weekly, same topic.
        Assert.Equal(2 * 2, alice.WeeklyMinutes);
        Assert.Equal(2 * 2, alice.MonthlyMinutes);
        Assert.Equal(1, alice.TopicsPracticed);

        // Bob: 1 weekly + 1 monthly-only → weekly count 1, monthly 2,
        // distinct topics = 2.
        Assert.Equal(1 * 2, bob.WeeklyMinutes);
        Assert.Equal(2 * 2, bob.MonthlyMinutes);
        Assert.Equal(2, bob.TopicsPracticed);
    }

    // ── Fold: ReadinessScore always null (PRR-320 scope doc) ────────────────

    [Fact]
    public void Fold_readiness_score_is_null_in_v1_no_readiness_model_wired()
    {
        var linked = new HashSet<string>(new[] { "enc::alice" }, StringComparer.Ordinal);
        var events = new List<MartenParentDashboardCardSource.HintEventEntry>
        {
            HintAt("enc::alice", Now.AddDays(-1)),
        };

        var bundle = MartenParentDashboardCardSource.Fold(events, linked, Now);

        Assert.Null(bundle.PerStudent["enc::alice"].ReadinessScore);
    }

    // ── Fold: null-arg guards ───────────────────────────────────────────────

    [Fact]
    public void Fold_null_events_throws()
    {
        var linked = new HashSet<string>(StringComparer.Ordinal);
        Assert.Throws<ArgumentNullException>(() =>
            MartenParentDashboardCardSource.Fold(null!, linked, Now));
    }

    [Fact]
    public void Fold_null_linked_set_throws()
    {
        var events = new List<MartenParentDashboardCardSource.HintEventEntry>();
        Assert.Throws<ArgumentNullException>(() =>
            MartenParentDashboardCardSource.Fold(events, null!, Now));
    }

    // ── Ctor null guards for the concrete sources ───────────────────────────

    [Fact]
    public void MartenSource_ctor_throws_on_null_store()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new MartenParentDashboardCardSource(null!));
    }

    // ── Zero-activity student stays out of dictionary → GetOrZero zeros ─────

    [Fact]
    public void Fold_zero_activity_linked_student_absent_from_dictionary_then_zero_via_getorzero()
    {
        var linked = new HashSet<string>(
            new[] { "enc::alice", "enc::zero-activity" }, StringComparer.Ordinal);
        var events = new List<MartenParentDashboardCardSource.HintEventEntry>
        {
            HintAt("enc::alice", Now.AddDays(-1)),
        };

        var bundle = MartenParentDashboardCardSource.Fold(events, linked, Now);

        Assert.True(bundle.PerStudent.ContainsKey("enc::alice"));
        Assert.False(bundle.PerStudent.ContainsKey("enc::zero-activity"));

        var zero = bundle.GetOrZero("enc::zero-activity");
        Assert.Equal(0, zero.WeeklyMinutes);
        Assert.Equal(0, zero.MonthlyMinutes);
        Assert.Equal(0, zero.TopicsPracticed);
        Assert.Null(zero.LastActiveAt);
        Assert.Null(zero.ReadinessScore);
    }
}
