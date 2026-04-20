// =============================================================================
// Cena Platform -- Mastery Rollup Documents (ADM-016)
// Marten-backed rollup docs that feed the mastery dashboards. These replace
// the Random-backed stubs in MasteryTrackingService and let admin queries
// read precomputed class/concept rollups instead of event scans.
//
// prr-013 retirement 2026-04-20: `AtRiskStudentDocument` moved to
// Documents/Legacy/AtRiskStudentDocument.Legacy.cs (type retained, [Obsolete],
// schema registration removed from MartenConfiguration). The
// `AtRiskCount` field on `ClassMasteryRollupDocument` was removed outright —
// "at-risk" is session-scoped per ADR-0003 + RDY-080 and lives on
// SessionRiskAssessment, never on a persisted rollup.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Class-wide mastery rollup for a specific day. Powers MasteryOverview and
/// ClassMastery views. One row per (classId, yyyy-MM-dd).
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

    // Velocity (the risk/at-risk counter was retired 2026-04-20 per prr-013).
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
