// =============================================================================
// Cena Platform -- Mastery Rollup Documents (ADM-016)
// Marten-backed rollup docs that feed the mastery dashboards. These replace
// the Random-backed stubs in MasteryTrackingService and let admin queries
// read precomputed class/concept rollups instead of event scans.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Class-wide mastery rollup for a specific day. Powers MasteryOverview,
/// ClassMastery, and AtRiskStudents views. One row per (classId, yyyy-MM-dd).
/// </summary>
public class ClassMasteryRollupDocument
{
    public string Id { get; set; } = "";              // "{classId}:{yyyy-MM-dd}"
    public string ClassId { get; set; } = "";
    public string ClassName { get; set; } = "";
    public string SchoolId { get; set; } = "";
    public DateTimeOffset Date { get; set; }

    public float AvgMastery { get; set; }             // 0..1
    public int TotalStudents { get; set; }

    // Mastery bands (bagrut-style 4-tier)
    public int BeginnerCount { get; set; }
    public int DevelopingCount { get; set; }
    public int ProficientCount { get; set; }
    public int MasterCount { get; set; }

    // Risk and velocity
    public int AtRiskCount { get; set; }              // students below threshold
    public float LearningVelocity { get; set; }       // concepts mastered per week
    public float LearningVelocityChange { get; set; }

    // Subject breakdown
    public List<ClassMasterySubjectSlot> SubjectBreakdown { get; set; } = new();

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ClassMasterySubjectSlot
{
    public string Subject { get; set; } = "";
    public float AvgMasteryLevel { get; set; }
    public int ConceptCount { get; set; }
    public int MasteredCount { get; set; }
}

/// <summary>
/// At-risk student row persisted by the mastery rollup service. Makes
/// "students needing intervention" queryable without scanning snapshots.
/// One row per (schoolId, studentId, date).
/// </summary>
public class AtRiskStudentDocument
{
    public string Id { get; set; } = "";              // "{schoolId}:{studentId}:{yyyy-MM-dd}"
    public string StudentId { get; set; } = "";
    public string StudentName { get; set; } = "";
    public string ClassId { get; set; } = "";
    public string SchoolId { get; set; } = "";
    public DateTimeOffset Date { get; set; }

    public string RiskLevel { get; set; } = "medium"; // high | medium | low
    public float CurrentAvgMastery { get; set; }
    public float MasteryDeclineLast14d { get; set; }
    public string RecommendedIntervention { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Concept-level difficulty aggregated across a school's students.
/// Powers the difficulty analysis panel. One row per (schoolId, conceptId).
/// </summary>
public class ConceptDifficultyDocument
{
    public string Id { get; set; } = "";              // "{schoolId}:{conceptId}"
    public string SchoolId { get; set; } = "";
    public string ConceptId { get; set; } = "";
    public string ConceptName { get; set; } = "";
    public string Subject { get; set; } = "";

    public int TotalAttempts { get; set; }
    public int CorrectAttempts { get; set; }
    public float AvgMastery { get; set; }             // 0..1
    public float StruggleRate { get; set; }           // % of students below 0.5 mastery
    public int StudentSampleSize { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
