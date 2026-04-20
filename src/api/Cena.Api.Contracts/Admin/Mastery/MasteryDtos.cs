// =============================================================================
// Cena Platform -- Mastery Tracking DTOs
// ADM-007: Mastery & learning progress analytics
//
// prr-013 follow-up (2026-04-20): the at-risk admin surface was retired.
// Teacher-facing "students needing intervention" data is now session-scoped
// per ADR-0003 + RDY-080 (see SessionRiskAssessment in the session actor).
// `AtRiskStudentsResponse`, `AtRiskStudent`, `RiskLevel`, `AtRiskCount`,
// `ReadinessScore`, and `DecayRisk` are gone from this contract.
// =============================================================================

namespace Cena.Api.Contracts.Admin.Mastery;

// Mastery Overview Dashboard
public sealed record MasteryOverviewResponse(
    IReadOnlyList<MasteryDistributionPoint> Distribution,
    IReadOnlyList<SubjectMastery> SubjectBreakdown,
    float LearningVelocity,  // concepts mastered per week
    float LearningVelocityChange);

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
    float DecayFactor,           // 0..1 spaced-repetition decay (HLR-derived)
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
