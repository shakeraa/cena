// =============================================================================
// Cena Platform — SessionContext tests (EPIC-PRR-I PRR-310, SLICE 1)
//
// Locks in the construction-time invariants for the session-pinned
// entitlement snapshot: non-empty string fields, non-null refs, and
// immutability (no public setters).
// =============================================================================

using System.Reflection;
using Cena.Actors.Sessions;
using Cena.Actors.Subscriptions;

namespace Cena.Actors.Tests.Sessions;

public class SessionContextTests
{
    private static UsageCaps SampleCaps() =>
        new(SonnetEscalationsPerWeek: 20,
            PhotoDiagnosticsPerMonth: 0,
            PhotoDiagnosticsHardCapPerMonth: 0,
            HintRequestsPerMonth: 500);

    private static TierFeatureFlags SampleFeatures() =>
        new(ParentDashboard: false,
            TutorHandoffPdf: false,
            ArabicDashboard: false,
            PrioritySupport: false,
            ClassroomDashboard: false,
            TeacherAssignedPractice: false,
            Sso: false);

    [Fact]
    public void Constructor_rejects_empty_session_id()
    {
        Assert.Throws<ArgumentException>(() => new SessionContext(
            sessionId: "",
            studentSubjectIdEncrypted: "student-enc-1",
            pinnedTier: SubscriptionTier.Basic,
            pinnedCaps: SampleCaps(),
            pinnedFeatures: SampleFeatures(),
            startedAt: DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Constructor_rejects_empty_student_id()
    {
        Assert.Throws<ArgumentException>(() => new SessionContext(
            sessionId: "sess-1",
            studentSubjectIdEncrypted: "",
            pinnedTier: SubscriptionTier.Basic,
            pinnedCaps: SampleCaps(),
            pinnedFeatures: SampleFeatures(),
            startedAt: DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Constructor_rejects_null_caps()
    {
        Assert.Throws<ArgumentNullException>(() => new SessionContext(
            sessionId: "sess-1",
            studentSubjectIdEncrypted: "student-enc-1",
            pinnedTier: SubscriptionTier.Basic,
            pinnedCaps: null!,
            pinnedFeatures: SampleFeatures(),
            startedAt: DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Constructor_rejects_null_features()
    {
        Assert.Throws<ArgumentNullException>(() => new SessionContext(
            sessionId: "sess-1",
            studentSubjectIdEncrypted: "student-enc-1",
            pinnedTier: SubscriptionTier.Basic,
            pinnedCaps: SampleCaps(),
            pinnedFeatures: null!,
            startedAt: DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Constructor_populates_all_fields()
    {
        var started = new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero);
        var ctx = new SessionContext(
            sessionId: "sess-1",
            studentSubjectIdEncrypted: "student-enc-1",
            pinnedTier: SubscriptionTier.Premium,
            pinnedCaps: SampleCaps(),
            pinnedFeatures: SampleFeatures(),
            startedAt: started);

        Assert.Equal("sess-1", ctx.SessionId);
        Assert.Equal("student-enc-1", ctx.StudentSubjectIdEncrypted);
        Assert.Equal(SubscriptionTier.Premium, ctx.PinnedTier);
        Assert.Equal(SampleCaps(), ctx.PinnedCaps);
        Assert.Equal(SampleFeatures(), ctx.PinnedFeatures);
        Assert.Equal(started, ctx.StartedAt);
    }

    [Fact]
    public void Record_has_no_public_setters_on_any_pinned_property()
    {
        // PRR-310 invariant: the snapshot is immutable for the duration of
        // the session. No public setter (even init-only would technically
        // satisfy "immutable after construction", but this record declares
        // get-only auto-properties on purpose — so reflection sees ZERO
        // public setters at all).
        var props = typeof(SessionContext).GetProperties(
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotEmpty(props);
        foreach (var p in props)
        {
            var setter = p.GetSetMethod(nonPublic: false);
            Assert.True(
                setter is null,
                $"{p.Name} must not expose a public setter; SessionContext is immutable.");
        }
    }

    [Fact]
    public void Record_value_equality_holds_for_identical_snapshots()
    {
        // record semantics — two snapshots with identical field values are
        // equal. This is what lets the cache treat "same sessionId +
        // same resolution" as idempotent.
        var started = new DateTimeOffset(2026, 4, 23, 12, 0, 0, TimeSpan.Zero);
        var a = new SessionContext(
            "sess-1", "student-enc-1", SubscriptionTier.Basic,
            SampleCaps(), SampleFeatures(), started);
        var b = new SessionContext(
            "sess-1", "student-enc-1", SubscriptionTier.Basic,
            SampleCaps(), SampleFeatures(), started);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
