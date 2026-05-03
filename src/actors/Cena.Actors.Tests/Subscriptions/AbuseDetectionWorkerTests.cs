// =============================================================================
// Cena Platform — AbuseDetectionWorker.FindAbusers tests (EPIC-PRR-J PRR-403)
//
// Locks the detection kernel so the "no auto-action, no false blocks"
// DoD invariant cannot regress. The kernel is a pure function; these
// tests run in-process with no Marten / DB needed.
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class AbuseDetectionWorkerTests
{
    [Fact]
    public void Student_above_threshold_is_flagged()
    {
        var events = new[]
        {
            SoftCap("p_1", "s_1", 205),
        };

        var flags = AbuseDetectionWorker.FindAbusers(
            events, AbuseDetectionWorker.AbuseThreshold).ToList();

        Assert.Single(flags);
        Assert.Equal("s_1", flags[0].StudentSubjectIdEncrypted);
        Assert.Equal(205, flags[0].MaxUsageCount);
    }

    [Fact]
    public void Student_at_exactly_threshold_is_flagged()
    {
        // Boundary: 200 == threshold is a flag (>= semantics, not >).
        var events = new[]
        {
            SoftCap("p_bd", "s_bd", 200),
        };

        var flags = AbuseDetectionWorker.FindAbusers(
            events, AbuseDetectionWorker.AbuseThreshold).ToList();

        Assert.Single(flags);
    }

    [Fact]
    public void Student_below_threshold_is_not_flagged()
    {
        var events = new[]
        {
            SoftCap("p_ok", "s_ok", 199),
        };

        var flags = AbuseDetectionWorker.FindAbusers(
            events, AbuseDetectionWorker.AbuseThreshold).ToList();

        Assert.Empty(flags);
    }

    [Fact]
    public void Non_photo_cap_events_are_ignored()
    {
        // Weekly-Sonnet soft-cap + per-tier photo soft-cap must not
        // cross-contaminate: abuse detection is photo-specific.
        var events = new[]
        {
            new EntitlementSoftCapReached_V1(
                ParentSubjectIdEncrypted: "p_x",
                StudentSubjectIdEncrypted: "s_x",
                CapType: EntitlementSoftCapReached_V1.CapTypes.SonnetEscalationsWeekly,
                UsageCount: 999,
                CapLimit: 20,
                ReachedAt: DateTimeOffset.UtcNow),
        };

        var flags = AbuseDetectionWorker.FindAbusers(
            events, AbuseDetectionWorker.AbuseThreshold).ToList();

        Assert.Empty(flags);
    }

    [Fact]
    public void Multiple_events_per_student_collapse_to_max()
    {
        // A student with many soft-cap events in the window should be
        // flagged against the HIGHEST observed count — the dashboard
        // shows a single row per student, not a bar chart of every
        // incremental breach.
        var events = new[]
        {
            SoftCap("p_hi", "s_hi", 105),
            SoftCap("p_hi", "s_hi", 180),
            SoftCap("p_hi", "s_hi", 250),   // the one that crosses threshold
        };

        var flags = AbuseDetectionWorker.FindAbusers(
            events, AbuseDetectionWorker.AbuseThreshold).ToList();

        Assert.Single(flags);
        Assert.Equal(250, flags[0].MaxUsageCount);
    }

    [Fact]
    public void Parent_subject_id_is_preserved_for_admin_context()
    {
        // The admin review queue uses the parent id (not the student's)
        // as the actionable entity — freezing a subscription is a
        // parent-level action.
        var events = new[] { SoftCap("p_CONTEXT", "s_1", 300) };

        var flags = AbuseDetectionWorker.FindAbusers(
            events, AbuseDetectionWorker.AbuseThreshold).ToList();

        Assert.Equal("p_CONTEXT", flags[0].ParentSubjectIdEncrypted);
    }

    [Fact]
    public void Empty_input_yields_empty_output()
    {
        var flags = AbuseDetectionWorker.FindAbusers(
            Array.Empty<EntitlementSoftCapReached_V1>(),
            AbuseDetectionWorker.AbuseThreshold).ToList();

        Assert.Empty(flags);
    }

    [Fact]
    public void Two_students_two_flags_if_both_abuse()
    {
        var events = new[]
        {
            SoftCap("p_1", "s_1", 210),
            SoftCap("p_2", "s_2", 220),
            SoftCap("p_3", "s_3", 50),       // not flagged
        };

        var flags = AbuseDetectionWorker.FindAbusers(
            events, AbuseDetectionWorker.AbuseThreshold).ToList();

        Assert.Equal(2, flags.Count);
        var ids = flags.Select(f => f.StudentSubjectIdEncrypted).ToHashSet();
        Assert.Contains("s_1", ids);
        Assert.Contains("s_2", ids);
    }

    private static EntitlementSoftCapReached_V1 SoftCap(
        string parent, string student, int usage) =>
        new(
            ParentSubjectIdEncrypted: parent,
            StudentSubjectIdEncrypted: student,
            CapType: EntitlementSoftCapReached_V1.CapTypes.PhotoDiagnosticMonthly,
            UsageCount: usage,
            CapLimit: 100,
            ReachedAt: DateTimeOffset.UtcNow.AddDays(-5));
}
