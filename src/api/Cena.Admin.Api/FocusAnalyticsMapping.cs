// =============================================================================
// Cena Platform -- FocusAnalyticsService pure mapping helpers
// Split from FocusAnalyticsService.cs to keep both files under 500 LOC.
// All helpers are internal static so the Cena.Actors.Tests assembly can
// unit-test them without touching a real Marten store.
// =============================================================================

using Cena.Actors.Events;
using Cena.Infrastructure.Documents;

namespace Cena.Admin.Api;

public sealed partial class FocusAnalyticsService
{
    internal static FocusOverviewResponse BuildFocusOverview(
        IReadOnlyList<FocusSessionRollupDocument> rollups,
        DateTimeOffset today)
    {
        if (rollups.Count == 0)
            return new FocusOverviewResponse(0f, 0f, 0f, 0, new List<FocusTrendPoint>());

        var avg = rollups.Average(r => r.AvgFocusScore);
        var totalSessions = rollups.Sum(r => r.SessionCount);
        var totalMwEvents = rollups.Sum(r => r.MindWanderingEvents);
        var totalTaken = rollups.Sum(r => r.MicrobreaksTaken);
        var totalSkipped = rollups.Sum(r => r.MicrobreaksSkipped);
        var totalResponses = totalTaken + totalSkipped;

        var mindWanderingRate = totalSessions > 0
            ? (float)totalMwEvents / Math.Max(totalSessions, 1) * 100f
            : 0f;
        var microbreakCompliance = totalResponses > 0
            ? (float)totalTaken / totalResponses * 100f
            : 0f;

        var sevenDaysAgo = today.AddDays(-6);
        var active = rollups
            .Where(r => r.Date >= sevenDaysAgo)
            .Select(r => r.StudentId)
            .Distinct()
            .Count();

        var trend = new List<FocusTrendPoint>();
        for (int i = 6; i >= 0; i--)
        {
            var dayStart = today.AddDays(-i);
            var dayRollups = rollups.Where(r => r.Date == dayStart).ToList();
            var dayAvg = dayRollups.Count > 0
                ? dayRollups.Average(r => r.AvgFocusScore)
                : 0f;
            var sessionCount = dayRollups.Sum(r => r.SessionCount);
            trend.Add(new FocusTrendPoint(
                dayStart.ToString("yyyy-MM-dd"),
                MathF.Round(dayAvg, 1),
                sessionCount));
        }

        return new FocusOverviewResponse(
            AvgFocusScore: MathF.Round(avg, 1),
            MindWanderingRate: MathF.Round(mindWanderingRate, 1),
            MicrobreakCompliance: MathF.Round(microbreakCompliance, 1),
            ActiveStudents: active,
            Trend: trend);
    }

    internal static StudentFocusDetailResponse BuildStudentFocusDetail(
        string studentId,
        IReadOnlyList<FocusSessionRollupDocument> rollups,
        DateTimeOffset today)
    {
        var studentName = rollups.First().StudentName;
        var sevenDaysAgo = today.AddDays(-6);

        var recent = rollups.Where(r => r.Date >= sevenDaysAgo).ToList();
        var avg7d = recent.Count > 0 ? recent.Average(r => r.AvgFocusScore) : 0f;
        var avg30d = rollups.Average(r => r.AvgFocusScore);

        var sessions = rollups
            .Select(r => new FocusSession(
                SessionId: $"rollup:{r.Id}",
                StartedAt: r.Date,
                EndedAt: r.Date.AddMinutes(r.FocusMinutes),
                AvgFocusScore: MathF.Round(r.AvgFocusScore, 1),
                MinFocusScore: MathF.Round(r.MinFocusScore, 1),
                MaxFocusScore: MathF.Round(r.MaxFocusScore, 1),
                DurationMinutes: Math.Max(r.FocusMinutes, 1)))
            .ToList();

        var mwEvents = rollups
            .Where(r => r.MindWanderingEvents > 0)
            .Select(r => new MindWanderingEvent(
                Timestamp: r.Date,
                FocusScoreAtEvent: r.MinFocusScore,
                Context: "rollup_aggregated",
                Trigger: null))
            .ToList();

        var microbreaks = new List<MicrobreakRecord>();
        foreach (var r in rollups)
        {
            for (int i = 0; i < r.MicrobreaksTaken; i++)
                microbreaks.Add(new MicrobreakRecord(r.Date, true, r.Date.AddMinutes(5), 60));
            for (int i = 0; i < r.MicrobreaksSkipped; i++)
                microbreaks.Add(new MicrobreakRecord(r.Date, false, null, 0));
        }

        var morning = rollups.Sum(r => r.MorningSessionCount);
        var afternoon = rollups.Sum(r => r.AfternoonSessionCount);
        var evening = rollups.Sum(r => r.EveningSessionCount);

        var chronotype = morning > evening && morning > afternoon
            ? "morning"
            : evening > morning && evening > afternoon ? "evening" : "neutral";

        var (optimalTime, chronoText) = chronotype switch
        {
            "morning" => ("9:00 AM - 12:00 PM",
                "Based on your rollups, you perform best during morning hours."),
            "evening" => ("6:00 PM - 9:00 PM",
                "Based on your rollups, you perform best during evening hours."),
            _ => ("10:00 AM - 2:00 PM",
                "Your focus patterns are fairly consistent throughout the day.")
        };

        return new StudentFocusDetailResponse(
            StudentId: studentId,
            StudentName: studentName,
            AvgFocusScore7d: MathF.Round(avg7d, 1),
            AvgFocusScore30d: MathF.Round(avg30d, 1),
            Sessions: sessions,
            MindWanderingEvents: mwEvents,
            MicrobreakHistory: microbreaks,
            Chronotype: new ChronotypeRecommendation(chronotype, optimalTime, chronoText));
    }

    internal static ClassFocusResponse BuildClassFocus(
        string classId,
        ClassAttentionRollupDocument? classRollup,
        IReadOnlyList<FocusSessionRollupDocument> studentRollups)
    {
        var className = classRollup?.ClassName ?? $"Class {classId}";
        var classAvg = classRollup?.AvgAttentionScore
            ?? (studentRollups.Count > 0 ? studentRollups.Average(r => r.AvgFocusScore) : 0f);

        var students = studentRollups
            .GroupBy(r => r.StudentId)
            .Select(g =>
            {
                var list = g.OrderBy(r => r.Date).ToList();
                var avg = list.Average(r => r.AvgFocusScore);
                var trend = DetermineTrend(list);
                return new StudentFocusSummary(
                    StudentId: g.Key,
                    StudentName: list.First().StudentName,
                    AvgFocusScore: MathF.Round(avg, 1),
                    Trend: trend,
                    NeedsAttention: avg < 60f);
            })
            .OrderByDescending(s => s.AvgFocusScore)
            .ToList();

        var timeSlots = classRollup?.HourlyAttention.Select(h => new TimeSlotFocus(
                TimeSlot: $"{h.Hour:D2}:00-{h.Hour + 1:D2}:00",
                AvgFocusScore: h.AvgFocusScore,
                StudentCount: h.SampleSize)).ToList()
            ?? new List<TimeSlotFocus>();

        var subjects = classRollup?.SubjectAttention.Select(s => new SubjectFocus(
                Subject: s.Subject,
                AvgFocusScore: s.AvgFocusScore,
                SessionCount: s.SessionCount)).ToList()
            ?? new List<SubjectFocus>();

        return new ClassFocusResponse(
            ClassId: classId,
            ClassName: className,
            ClassAvgFocus: MathF.Round(classAvg, 1),
            Students: students,
            FocusByTimeSlot: timeSlots,
            FocusBySubject: subjects);
    }

    internal static string DetermineTrend(IReadOnlyList<FocusSessionRollupDocument> rollups)
    {
        if (rollups.Count < 2) return "stable";
        var firstHalf = rollups.Take(rollups.Count / 2).Average(r => r.AvgFocusScore);
        var secondHalf = rollups.Skip(rollups.Count / 2).Average(r => r.AvgFocusScore);
        var delta = secondHalf - firstHalf;
        if (delta > 3f) return "improving";
        if (delta < -3f) return "declining";
        return "stable";
    }

    internal static FocusDegradationResponse BuildFromRollup(FocusDegradationRollupDocument rollup)
    {
        var points = rollup.Buckets
            .OrderBy(b => b.MinutesIntoSession)
            .Select(b => new DegradationPoint(
                MinutesIntoSession: b.MinutesIntoSession,
                AvgFocusScore: MathF.Round(b.AvgFocusScore, 1),
                SampleSize: b.SampleSize))
            .ToList();
        return new FocusDegradationResponse(points);
    }

    internal static FocusExperimentsResponse BuildExperimentsResponse(
        IReadOnlyList<StudentProfileSnapshot> snapshots)
    {
        var byCohort = snapshots
            .Where(s => !string.IsNullOrEmpty(s.ExperimentCohort))
            .GroupBy(s => s.ExperimentCohort!)
            .ToDictionary(g => g.Key, g => g.Count());

        var experiments = new List<FocusExperiment>();
        if (byCohort.Count == 0)
            return new FocusExperimentsResponse(experiments);

        var variants = byCohort
            .Select(kv => new ExperimentVariant(kv.Key, kv.Key, kv.Value))
            .ToList();

        experiments.Add(new FocusExperiment(
            ExperimentId: "focus-cohort-v1",
            Name: "Focus Cohort Allocation",
            Status: "running",
            StartedAt: DateTimeOffset.UtcNow.AddDays(-30),
            EndedAt: null,
            Variants: variants,
            Results: null));

        return new FocusExperimentsResponse(experiments);
    }

    internal static List<StudentAttentionAlert> BuildAttentionAlerts(
        IReadOnlyList<FocusSessionRollupDocument> rollups)
    {
        return rollups
            .GroupBy(r => r.StudentId)
            .Select(g =>
            {
                var list = g.ToList();
                var avg = list.Average(r => r.AvgFocusScore);
                return new
                {
                    StudentId = g.Key,
                    StudentName = list.First().StudentName,
                    ClassId = list.First().ClassId ?? "unknown",
                    Avg = avg
                };
            })
            .Where(x => x.Avg < 65f)
            .Select(x => new StudentAttentionAlert(
                StudentId: x.StudentId,
                StudentName: x.StudentName,
                ClassId: x.ClassId,
                AlertType: "low_focus",
                CurrentScore: MathF.Round(x.Avg, 1),
                BaselineScore: 70f,
                Recommendation: x.Avg < 45f
                    ? "Consider shorter study sessions with more frequent breaks"
                    : "Review study environment and time of day"))
            .OrderBy(a => a.CurrentScore)
            .ToList();
    }

    internal static List<FocusTimelinePoint> BuildTimelinePoints(
        IReadOnlyList<FocusSessionRollupDocument> rollups,
        DateTimeOffset today,
        int days)
    {
        var byDate = rollups.ToLookup(r => r.Date);
        var points = new List<FocusTimelinePoint>();
        for (int i = days; i >= 0; i--)
        {
            var dayStart = today.AddDays(-i);
            var dayRows = byDate[dayStart].ToList();
            var dayAvg = dayRows.Count > 0 ? dayRows.Average(r => r.AvgFocusScore) : 0f;
            var mwTotal = dayRows.Sum(r => r.MindWanderingEvents);
            var mbTotal = dayRows.Sum(r => r.MicrobreaksTaken);
            points.Add(new FocusTimelinePoint(
                dayStart,
                MathF.Round(dayAvg, 1),
                mwTotal,
                mbTotal));
        }
        return points;
    }

    internal static List<HeatmapCell> BuildHeatmapCells(
        IReadOnlyList<ClassAttentionRollupDocument> rollups)
    {
        var grouped = rollups
            .SelectMany(r => r.HourlyAttention)
            .GroupBy(h => (h.DayOfWeek, h.Hour))
            .Select(g => new HeatmapCell(
                Day: g.Key.DayOfWeek,
                Hour: $"{g.Key.Hour:D2}:00",
                AvgFocusScore: MathF.Round(g.Average(x => x.AvgFocusScore), 1),
                StudentCount: g.Sum(x => x.SampleSize)))
            .OrderBy(c => c.Day)
            .ThenBy(c => c.Hour)
            .ToList();
        return grouped;
    }
}
