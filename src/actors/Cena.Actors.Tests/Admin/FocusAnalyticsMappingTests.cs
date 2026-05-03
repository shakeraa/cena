// =============================================================================
// Cena Platform -- FocusAnalyticsService mapping tests (ADM-014 hardening)
// Exercises the pure internal static helpers that transform Marten rollup
// documents into admin DTOs. No Marten, no DB — deterministic unit tests.
// =============================================================================

using Cena.Actors.Events;
using Cena.Admin.Api;
using Cena.Infrastructure.Documents;

namespace Cena.Actors.Tests.Admin;

public sealed class FocusAnalyticsMappingTests
{
    private static readonly DateTimeOffset Today = new(new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void BuildFocusOverview_EmptyRollups_ReturnsZeros()
    {
        var result = FocusAnalyticsService.BuildFocusOverview(Array.Empty<FocusSessionRollupDocument>(), Today);

        Assert.Equal(0f, result.AvgFocusScore);
        Assert.Equal(0f, result.MindWanderingRate);
        Assert.Equal(0f, result.MicrobreakCompliance);
        Assert.Equal(0, result.ActiveStudents);
        Assert.Empty(result.Trend);
    }

    [Fact]
    public void BuildFocusOverview_ComputesAverageAndMicrobreakCompliance()
    {
        var rollups = new[]
        {
            MakeRollup("stu-a", Today, avg: 80f, mwEvents: 1, taken: 3, skipped: 1, sessions: 2),
            MakeRollup("stu-b", Today, avg: 60f, mwEvents: 2, taken: 2, skipped: 2, sessions: 2),
        };

        var result = FocusAnalyticsService.BuildFocusOverview(rollups, Today);

        Assert.Equal(70f, result.AvgFocusScore);
        // microbreaks: 5 taken, 3 skipped → 5/8 = 62.5%
        Assert.Equal(62.5f, result.MicrobreakCompliance);
        Assert.Equal(2, result.ActiveStudents);
        Assert.Equal(7, result.Trend.Count);
    }

    [Fact]
    public void BuildFocusOverview_TrendHasSevenBuckets_LatestDayHasAverage()
    {
        var rollups = new[]
        {
            MakeRollup("stu-a", Today, avg: 90f, sessions: 3),
        };

        var result = FocusAnalyticsService.BuildFocusOverview(rollups, Today);
        var latest = result.Trend.Last();
        Assert.Equal(Today.ToString("yyyy-MM-dd"), latest.Date);
        Assert.Equal(90f, latest.AvgScore);
        Assert.Equal(3, latest.SessionCount);
    }

    [Fact]
    public void BuildStudentFocusDetail_UsesRollupsForAveragesAndChronotype()
    {
        var rollups = new[]
        {
            MakeRollup("stu-a", Today, avg: 80f, sessions: 2, morning: 2, evening: 0),
            MakeRollup("stu-a", Today.AddDays(-1), avg: 70f, sessions: 2, morning: 2, evening: 0),
            MakeRollup("stu-a", Today.AddDays(-7), avg: 50f, sessions: 1, morning: 1, evening: 0),
        };

        var result = FocusAnalyticsService.BuildStudentFocusDetail("stu-a", rollups, Today);

        Assert.Equal("stu-a", result.StudentId);
        Assert.Equal(3, result.Sessions.Count);
        Assert.Equal("morning", result.Chronotype.DetectedChronotype);
        // avg7d = (80 + 70) / 2 = 75
        Assert.Equal(75f, result.AvgFocusScore7d);
        // avg30d = (80 + 70 + 50) / 3 ≈ 66.7
        Assert.InRange(result.AvgFocusScore30d, 66.5f, 66.7f);
    }

    [Fact]
    public void BuildAttentionAlerts_FiltersAboveThreshold_AndSortsAscending()
    {
        var rollups = new[]
        {
            MakeRollup("stu-low", Today, avg: 40f),
            MakeRollup("stu-ok", Today, avg: 85f),
            MakeRollup("stu-mid", Today, avg: 55f),
        };

        var alerts = FocusAnalyticsService.BuildAttentionAlerts(rollups);

        Assert.Equal(2, alerts.Count);
        Assert.Equal("stu-low", alerts[0].StudentId);
        Assert.Equal("stu-mid", alerts[1].StudentId);
        Assert.StartsWith("Consider shorter", alerts[0].Recommendation);
    }

    [Fact]
    public void BuildClassFocus_PropagatesHourlyAndSubjectBuckets()
    {
        var rollup = new ClassAttentionRollupDocument
        {
            Id = "c1:2026-04-10",
            ClassId = "c1",
            ClassName = "Class 1",
            SchoolId = "dev-school",
            Date = Today,
            AvgAttentionScore = 72f,
            TotalStudents = 20,
            HourlyAttention = new List<ClassAttentionHourSlot>
            {
                new() { Hour = 9, DayOfWeek = "Mon", AvgFocusScore = 80f, SampleSize = 20 },
                new() { Hour = 10, DayOfWeek = "Mon", AvgFocusScore = 75f, SampleSize = 20 },
            },
            SubjectAttention = new List<ClassAttentionSubjectSlot>
            {
                new() { Subject = "Math", AvgFocusScore = 78f, SessionCount = 10 },
            },
        };

        var studentRollups = new[]
        {
            MakeRollup("stu-a", Today, avg: 80f, classId: "c1"),
            MakeRollup("stu-b", Today, avg: 55f, classId: "c1"),
        };

        var response = FocusAnalyticsService.BuildClassFocus("c1", rollup, studentRollups);

        Assert.Equal("c1", response.ClassId);
        Assert.Equal("Class 1", response.ClassName);
        Assert.Equal(72f, response.ClassAvgFocus);
        Assert.Equal(2, response.Students.Count);
        Assert.Equal(2, response.FocusByTimeSlot.Count);
        Assert.Single(response.FocusBySubject);
        Assert.Contains(response.Students, s => s.StudentId == "stu-b" && s.NeedsAttention);
    }

    [Fact]
    public void DetermineTrend_ClassifiesByDelta()
    {
        var improving = new[]
        {
            MakeRollup("x", Today.AddDays(-6), avg: 60f),
            MakeRollup("x", Today.AddDays(-5), avg: 62f),
            MakeRollup("x", Today.AddDays(-1), avg: 78f),
            MakeRollup("x", Today, avg: 82f),
        };

        var declining = new[]
        {
            MakeRollup("x", Today.AddDays(-3), avg: 85f),
            MakeRollup("x", Today.AddDays(-2), avg: 80f),
            MakeRollup("x", Today.AddDays(-1), avg: 65f),
            MakeRollup("x", Today, avg: 60f),
        };

        var stable = new[]
        {
            MakeRollup("x", Today.AddDays(-1), avg: 70f),
            MakeRollup("x", Today, avg: 71f),
        };

        Assert.Equal("improving", FocusAnalyticsService.DetermineTrend(improving));
        Assert.Equal("declining", FocusAnalyticsService.DetermineTrend(declining));
        Assert.Equal("stable", FocusAnalyticsService.DetermineTrend(stable));
    }

    [Fact]
    public void BuildTimelinePoints_ProducesOneEntryPerDay()
    {
        var rollups = new[]
        {
            MakeRollup("stu-a", Today, avg: 80f, mwEvents: 2, taken: 3),
            MakeRollup("stu-a", Today.AddDays(-1), avg: 70f, mwEvents: 1, taken: 2),
        };

        var points = FocusAnalyticsService.BuildTimelinePoints(rollups, Today, days: 7);
        Assert.Equal(8, points.Count); // 0..7 inclusive
        Assert.Equal(80f, points.Last().FocusScore);
        Assert.Equal(2, points.Last().MindWanderingCount);
        Assert.Equal(3, points.Last().MicrobreakCount);
    }

    [Fact]
    public void BuildExperimentsResponse_EmptySnapshots_ReturnsEmptyList()
    {
        var result = FocusAnalyticsService.BuildExperimentsResponse(Array.Empty<StudentProfileSnapshot>());
        Assert.Empty(result.Experiments);
    }

    [Fact]
    public void BuildExperimentsResponse_GroupsByCohort()
    {
        var snapshots = new[]
        {
            new StudentProfileSnapshot { StudentId = "s1", ExperimentCohort = "control" },
            new StudentProfileSnapshot { StudentId = "s2", ExperimentCohort = "control" },
            new StudentProfileSnapshot { StudentId = "s3", ExperimentCohort = "variant-a" },
        };

        var result = FocusAnalyticsService.BuildExperimentsResponse(snapshots);
        Assert.Single(result.Experiments);
        var experiment = result.Experiments[0];
        Assert.Equal(2, experiment.Variants.Count);
        Assert.Contains(experiment.Variants, v => v.VariantId == "control" && v.ParticipantCount == 2);
    }

    // ── helpers ──
    private static FocusSessionRollupDocument MakeRollup(
        string studentId,
        DateTimeOffset date,
        float avg = 70f,
        int sessions = 1,
        int mwEvents = 0,
        int taken = 0,
        int skipped = 0,
        int morning = 0,
        int evening = 0,
        string? classId = null) => new()
    {
        Id = $"{studentId}:{date:yyyy-MM-dd}",
        StudentId = studentId,
        StudentName = $"Student {studentId}",
        SchoolId = "dev-school",
        ClassId = classId,
        Date = date,
        AvgFocusScore = avg,
        MinFocusScore = avg - 10f,
        MaxFocusScore = avg + 10f,
        SessionCount = sessions,
        FocusMinutes = 60,
        MindWanderingEvents = mwEvents,
        MicrobreaksTaken = taken,
        MicrobreaksSkipped = skipped,
        MorningSessionCount = morning,
        EveningSessionCount = evening,
    };
}
