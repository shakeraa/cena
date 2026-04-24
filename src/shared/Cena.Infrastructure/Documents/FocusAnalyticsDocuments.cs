// =============================================================================
// Cena Platform -- Focus Analytics Documents (ADM-014)
// Marten-backed rollup docs that feed the focus dashboards. These replace
// the hand-crafted Random-backed stubs in FocusAnalyticsService.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Per-student daily focus rollup. One row per (studentId, yyyy-MM-dd).
/// Rolls up focus_score_updated_v1 / mind_wandering_detected_v1 /
/// microbreak_* events for a single day so admin queries don't have to
/// scan the raw event stream.
/// </summary>
public class FocusSessionRollupDocument
{
    public string Id { get; set; } = "";              // "{studentId}:{yyyy-MM-dd}"
    public string StudentId { get; set; } = "";
    public string StudentName { get; set; } = "";
    public string? ClassId { get; set; }
    public string SchoolId { get; set; } = "";
    public DateTimeOffset Date { get; set; }          // start-of-day UTC

    // Focus metrics
    public float AvgFocusScore { get; set; }          // 0..100
    public float MinFocusScore { get; set; }
    public float MaxFocusScore { get; set; }
    public int SessionCount { get; set; }
    public int FocusMinutes { get; set; }

    // Engagement metrics
    public int MindWanderingEvents { get; set; }
    public int MicrobreaksTaken { get; set; }
    public int MicrobreaksSkipped { get; set; }

    // Chronotype signal
    public int MorningSessionCount { get; set; }      // 6:00-12:00
    public int AfternoonSessionCount { get; set; }    // 12:00-17:00
    public int EveningSessionCount { get; set; }      // 17:00-23:00

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Per-class daily attention rollup. Powers the class heatmap and the
/// "students needing attention" widget. One row per (classId, yyyy-MM-dd).
/// </summary>
public class ClassAttentionRollupDocument
{
    public string Id { get; set; } = "";              // "{classId}:{yyyy-MM-dd}"
    public string ClassId { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string SchoolId { get; set; } = "";
    public DateTimeOffset Date { get; set; }

    public float AvgAttentionScore { get; set; }      // 0..100
    public int TotalStudents { get; set; }
    public int AtRiskStudentCount { get; set; }       // students below threshold

    // Rolling distribution for dashboards
    public List<ClassAttentionHourSlot> HourlyAttention { get; set; } = new();
    public List<ClassAttentionSubjectSlot> SubjectAttention { get; set; } = new();

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ClassAttentionHourSlot
{
    public int Hour { get; set; }                     // 0..23
    public string DayOfWeek { get; set; } = "";       // Mon..Sun
    public float AvgFocusScore { get; set; }
    public int SampleSize { get; set; }
}

public class ClassAttentionSubjectSlot
{
    public string Subject { get; set; } = "";
    public float AvgFocusScore { get; set; }
    public int SessionCount { get; set; }
}

/// <summary>
/// Focus degradation curve — concept-wide. One row per (schoolId, period).
/// Replaces inline computation of QuestionNumber→AvgFocusScore for
/// dashboards that need fast reads over long windows.
/// </summary>
public class FocusDegradationRollupDocument
{
    public string Id { get; set; } = "";              // "{schoolId}:{period}"
    public string SchoolId { get; set; } = "";
    public string Period { get; set; } = "30d";       // 7d | 14d | 30d
    public List<DegradationBucket> Buckets { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class DegradationBucket
{
    public int MinutesIntoSession { get; set; }
    public float AvgFocusScore { get; set; }
    public int SampleSize { get; set; }
}
