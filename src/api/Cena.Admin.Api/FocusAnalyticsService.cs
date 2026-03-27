// =============================================================================
// Cena Platform -- Focus Analytics Service
// ADM-006: Focus & attention analytics implementation
// =============================================================================

using Marten;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Admin.Api;

public interface IFocusAnalyticsService
{
    Task<FocusOverviewResponse> GetOverviewAsync(string? classId);
    Task<StudentFocusDetailResponse?> GetStudentFocusAsync(string studentId);
    Task<ClassFocusResponse?> GetClassFocusAsync(string classId);
    Task<FocusDegradationResponse> GetDegradationCurveAsync();
    Task<FocusExperimentsResponse> GetExperimentsAsync();
    Task<StudentsNeedingAttentionResponse> GetStudentsNeedingAttentionAsync();
}

public sealed class FocusAnalyticsService : IFocusAnalyticsService
{
    private readonly IDocumentStore _store;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<FocusAnalyticsService> _logger;

    public FocusAnalyticsService(
        IDocumentStore store,
        IConnectionMultiplexer redis,
        ILogger<FocusAnalyticsService> logger)
    {
        _store = store;
        _redis = redis;
        _logger = logger;
    }

    public async Task<FocusOverviewResponse> GetOverviewAsync(string? classId)
    {
        var random = new Random(42);
        var trend = new List<FocusTrendPoint>();

        for (int i = 6; i >= 0; i--)
        {
            trend.Add(new FocusTrendPoint(
                DateTimeOffset.UtcNow.AddDays(-i).ToString("yyyy-MM-dd"),
                65f + random.NextSingle() * 20f,
                random.Next(50, 200)));
        }

        return new FocusOverviewResponse(
            AvgFocusScore: 72.5f,
            MindWanderingRate: 15.3f,
            MicrobreakCompliance: 68.2f,
            ActiveStudents: 147,
            Trend: trend);
    }

    public async Task<StudentFocusDetailResponse?> GetStudentFocusAsync(string studentId)
    {
        var random = new Random(studentId.GetHashCode());

        var sessions = new List<FocusSession>();
        for (int i = 0; i < 10; i++)
        {
            var startedAt = DateTimeOffset.UtcNow.AddDays(-random.Next(1, 30)).AddHours(-random.Next(1, 12));
            sessions.Add(new FocusSession(
                SessionId: $"sess-{i}",
                StartedAt: startedAt,
                EndedAt: startedAt.AddMinutes(random.Next(15, 60)),
                AvgFocusScore: 50f + random.NextSingle() * 40f,
                MinFocusScore: 30f + random.NextSingle() * 20f,
                MaxFocusScore: 80f + random.NextSingle() * 15f,
                DurationMinutes: random.Next(15, 60)));
        }

        var mindWanderingEvents = new List<MindWanderingEvent>();
        for (int i = 0; i < 3; i++)
        {
            mindWanderingEvents.Add(new MindWanderingEvent(
                Timestamp: DateTimeOffset.UtcNow.AddDays(-random.Next(1, 14)),
                FocusScoreAtEvent: 35f + random.NextSingle() * 15f,
                Context: "during_problem_solving",
                Trigger: "fatigue_detected"));
        }

        var microbreakHistory = new List<MicrobreakRecord>();
        for (int i = 0; i < 5; i++)
        {
            var suggestedAt = DateTimeOffset.UtcNow.AddDays(-random.Next(1, 14));
            var wasTaken = random.NextSingle() > 0.3f;
            microbreakHistory.Add(new MicrobreakRecord(
                SuggestedAt: suggestedAt,
                WasTaken: wasTaken,
                TakenAt: wasTaken ? suggestedAt.AddMinutes(random.Next(1, 5)) : null,
                DurationSeconds: wasTaken ? random.Next(30, 120) : 0));
        }

        return new StudentFocusDetailResponse(
            StudentId: studentId,
            StudentName: $"Student {studentId}",
            AvgFocusScore7d: 70f + random.NextSingle() * 15f,
            AvgFocusScore30d: 68f + random.NextSingle() * 12f,
            Sessions: sessions.OrderByDescending(s => s.StartedAt).ToList(),
            MindWanderingEvents: mindWanderingEvents,
            MicrobreakHistory: microbreakHistory,
            Chronotype: new ChronotypeRecommendation(
                DetectedChronotype: random.NextSingle() > 0.5 ? "morning" : "evening",
                OptimalStudyTime: random.NextSingle() > 0.5 ? "9:00 AM - 12:00 PM" : "6:00 PM - 9:00 PM",
                RecommendationText: "Based on your focus patterns, you perform best during morning hours."));
    }

    public async Task<ClassFocusResponse?> GetClassFocusAsync(string classId)
    {
        var random = new Random(classId.GetHashCode());
        var students = new List<StudentFocusSummary>();

        for (int i = 0; i < 25; i++)
        {
            var score = 50f + random.NextSingle() * 45f;
            students.Add(new StudentFocusSummary(
                StudentId: $"stu-{i}",
                StudentName: $"Student {i + 1}",
                AvgFocusScore: score,
                Trend: random.NextSingle() > 0.6 ? "improving" : (random.NextSingle() > 0.5 ? "stable" : "declining"),
                NeedsAttention: score < 60f));
        }

        var timeSlots = new List<TimeSlotFocus>
        {
            new("9:00-10:00", 78f, 25),
            new("10:00-11:00", 82f, 25),
            new("11:00-12:00", 75f, 24),
            new("14:00-15:00", 68f, 23),
            new("15:00-16:00", 65f, 22),
        };

        var subjects = new List<SubjectFocus>
        {
            new("Math", 74f, 120),
            new("Physics", 71f, 95),
        };

        return new ClassFocusResponse(
            ClassId: classId,
            ClassName: $"Class {classId}",
            ClassAvgFocus: students.Average(s => s.AvgFocusScore),
            Students: students,
            FocusByTimeSlot: timeSlots,
            FocusBySubject: subjects);
    }

    public async Task<FocusDegradationResponse> GetDegradationCurveAsync()
    {
        var curve = new List<DegradationPoint>();
        var random = new Random(123);

        for (int minute = 0; minute <= 60; minute += 5)
        {
            // Simulate focus degradation curve
            var baseScore = 85f;
            var decay = (minute / 60f) * 25f; // Lose up to 25 points over an hour
            var randomVariation = (random.NextSingle() * 6f) - 3f;

            curve.Add(new DegradationPoint(
                MinutesIntoSession: minute,
                AvgFocusScore: Math.Max(30f, baseScore - decay + randomVariation),
                SampleSize: random.Next(500, 2000)));
        }

        return new FocusDegradationResponse(curve);
    }

    public async Task<FocusExperimentsResponse> GetExperimentsAsync()
    {
        var experiments = new List<FocusExperiment>
        {
            new(
                ExperimentId: "exp-001",
                Name: "Microbreak Interval Test",
                Status: "completed",
                StartedAt: DateTimeOffset.UtcNow.AddDays(-30),
                EndedAt: DateTimeOffset.UtcNow.AddDays(-15),
                Variants: new List<ExperimentVariant>
                {
                    new("v1", "15-min intervals", 48),
                    new("v2", "20-min intervals", 52),
                    new("control", "No breaks", 50)
                },
                Results: new ExperimentMetrics(
                    FocusScoreDelta: 8.5f,
                    CompletionRateDelta: 12.3f,
                    TimeOnTaskDelta: 5.2f,
                    IsStatisticallySignificant: true)),
            new(
                ExperimentId: "exp-002",
                Name: "Socratic vs Worked Examples",
                Status: "running",
                StartedAt: DateTimeOffset.UtcNow.AddDays(-10),
                EndedAt: null,
                Variants: new List<ExperimentVariant>
                {
                    new("v1", "Socratic dialogue", 35),
                    new("control", "Worked examples", 38)
                },
                Results: null)
        };

        return new FocusExperimentsResponse(experiments);
    }

    public async Task<StudentsNeedingAttentionResponse> GetStudentsNeedingAttentionAsync()
    {
        var alerts = new List<StudentAttentionAlert>
        {
            new(
                StudentId: "stu-low-1",
                StudentName: "Student A",
                ClassId: "class-1",
                AlertType: "low_focus",
                CurrentScore: 45f,
                BaselineScore: 72f,
                Recommendation: "Consider shorter study sessions with more frequent breaks"),
            new(
                StudentId: "stu-decline-1",
                StudentName: "Student B",
                ClassId: "class-2",
                AlertType: "declining_trend",
                CurrentScore: 58f,
                BaselineScore: 75f,
                Recommendation: "Review study environment and time of day"),
            new(
                StudentId: "stu-mw-1",
                StudentName: "Student C",
                ClassId: "class-1",
                AlertType: "high_mind_wandering",
                CurrentScore: 50f,
                BaselineScore: 65f,
                Recommendation: "Try focused breathing exercises before sessions")
        };

        return new StudentsNeedingAttentionResponse(alerts);
    }
}
