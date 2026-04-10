// =============================================================================
// Cena Platform -- Mastery Tracking DTOs
// ADM-007: Mastery & learning progress analytics
// =============================================================================

namespace Cena.Api.Contracts.Admin.Mastery;

// Mastery Overview Dashboard
public sealed record MasteryOverviewResponse(
    IReadOnlyList<MasteryDistributionPoint> Distribution,
    IReadOnlyList<SubjectMastery> SubjectBreakdown,
    float LearningVelocity,  // concepts mastered per week
    float LearningVelocityChange,
    int AtRiskCount);

public sealed record MasteryDistributionPoint(
    string Level,  // beginner, developing, proficient, master
    int Count,
    float Percentage);

public sealed record SubjectMastery(
    string Subject,
    float AvgMasteryLevel,
    int ConceptCount,
    int MasteredCount);

// Student Mastery Detail
public sealed record StudentMasteryDetailResponse(
    string StudentId,
    string StudentName,
    IReadOnlyList<ConceptMasteryNode> KnowledgeMap,
    IReadOnlyList<LearningFrontierItem> LearningFrontier,
    IReadOnlyList<MasteryHistoryPoint> MasteryHistory,
    IReadOnlyList<ScaffoldingRecommendation> Scaffolding,
    IReadOnlyList<ReviewPriorityItem> ReviewQueue);

public sealed record ConceptMasteryNode(
    string ConceptId,
    string ConceptName,
    string Subject,
    float MasteryLevel,
    string Status,  // locked, available, in_progress, mastered
    IReadOnlyList<string> PrerequisiteIds,
    IReadOnlyList<string> UnlocksIds);

public sealed record LearningFrontierItem(
    string ConceptId,
    string ConceptName,
    float ReadinessScore,
    string Reason);  // prerequisites_met, spiral_review, gap_filled

public sealed record MasteryHistoryPoint(
    string Date,
    float AvgMastery,
    int ConceptsAttempted,
    int ConceptsMastered);

public sealed record ScaffoldingRecommendation(
    string ConceptId,
    string ConceptName,
    string RecommendedLevel,  // minimal, moderate, extensive
    string Rationale);

public sealed record ReviewPriorityItem(
    string ConceptId,
    string ConceptName,
    float DecayRisk,
    float LastMasteryLevel,
    DateTimeOffset LastAttempted,
    int Priority);  // 1 = highest

// Class Progress View
public sealed record ClassMasteryResponse(
    string ClassId,
    string ClassName,
    IReadOnlyList<string> Concepts,
    IReadOnlyList<StudentMasteryRow> Students,
    IReadOnlyList<ConceptDifficulty> DifficultyAnalysis,
    PacingRecommendation Pacing);

public sealed record StudentMasteryRow(
    string StudentId,
    string StudentName,
    IReadOnlyList<float> MasteryLevels,  // parallel to Concepts
    float OverallProgress);

public sealed record ConceptDifficulty(
    string ConceptId,
    string ConceptName,
    float AvgMastery,
    float StruggleRate,  // % of students below threshold
    int AttemptCount);

public sealed record PacingRecommendation(
    bool ReadyToAdvance,
    string Recommendation,
    IReadOnlyList<string> ConceptsToReview,
    IReadOnlyList<string> ConceptsReadyToIntroduce);

// At-Risk Students
public sealed record AtRiskStudentsResponse(
    IReadOnlyList<AtRiskStudent> Students);

public sealed record AtRiskStudent(
    string StudentId,
    string StudentName,
    string ClassId,
    string RiskLevel,  // high, medium
    float CurrentAvgMastery,
    float MasteryDecline,
    string RecommendedIntervention);
