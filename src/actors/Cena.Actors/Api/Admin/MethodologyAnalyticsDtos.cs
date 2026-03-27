// =============================================================================
// Cena Platform -- Methodology Analytics DTOs
// ADM-011: Methodology effectiveness and stagnation monitoring
// =============================================================================

namespace Cena.Actors.Api.Admin;

// Methodology Effectiveness Dashboard
public sealed record MethodologyEffectivenessResponse(
    IReadOnlyList<MethodologyComparison> Comparisons,
    IReadOnlyList<SwitchTriggerBreakdown> SwitchTriggers,
    IReadOnlyList<StagnationTrendPoint> StagnationTrend,
    float EscalationRate);

public sealed record MethodologyComparison(
    string Methodology,
    IReadOnlyList<ErrorTypeEffectiveness> ByErrorType);

public sealed record ErrorTypeEffectiveness(
    string ErrorType,
    float AvgTimeToMastery,
    float SuccessRate,
    int SampleSize);

public sealed record SwitchTriggerBreakdown(
    string TriggerType,  // stagnation, student_requested, mcm_recommendation
    int Count,
    float Percentage);

public sealed record StagnationTrendPoint(
    string Date,
    int StagnationEvents,
    int ResolvedEvents);

// Stagnation Monitor
public sealed record StagnationMonitorResponse(
    IReadOnlyList<StagnatingStudent> CurrentlyStagnating,
    IReadOnlyList<MentorResistantConcept> MentorResistantConcepts);

public sealed record StagnatingStudent(
    string StudentId,
    string StudentName,
    string ClassId,
    string ConceptCluster,
    float CompositeScore,
    int AttemptCount,
    int DaysStuck,
    IReadOnlyList<string> AttemptedMethodologies);

public sealed record MentorResistantConcept(
    string ConceptId,
    string ConceptName,
    string Subject,
    int StuckStudentCount,
    IReadOnlyList<string> ExhaustedMethodologies);

// MCM Graph
public sealed record McmGraphResponse(
    IReadOnlyList<McmNode> Nodes,
    IReadOnlyList<McmEdge> Edges);

public sealed record McmNode(
    string Id,
    string Type,  // error_type, concept_category, methodology
    string Label,
    string? Category);

public sealed record McmEdge(
    string Source,
    string Target,
    float Confidence,
    int SampleSize,
    bool CanEdit);

public sealed record UpdateMcmEdgeRequest(
    string Source,
    string Target,
    float Confidence);
