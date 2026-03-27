// =============================================================================
// Cena Platform -- Focus Analytics DTOs
// ADM-006: Focus & attention analytics for teachers and admins
// =============================================================================

namespace Cena.Actors.Api.Admin;

// Focus Overview Dashboard
public sealed record FocusOverviewResponse(
    float AvgFocusScore,
    float MindWanderingRate,
    float MicrobreakCompliance,
    int ActiveStudents,
    IReadOnlyList<FocusTrendPoint> Trend);

public sealed record FocusTrendPoint(
    string Date,
    float AvgScore,
    int SessionCount);

// Student Focus Detail
public sealed record StudentFocusDetailResponse(
    string StudentId,
    string StudentName,
    float AvgFocusScore7d,
    float AvgFocusScore30d,
    IReadOnlyList<FocusSession> Sessions,
    IReadOnlyList<MindWanderingEvent> MindWanderingEvents,
    IReadOnlyList<MicrobreakRecord> MicrobreakHistory,
    ChronotypeRecommendation Chronotype);

public sealed record FocusSession(
    string SessionId,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    float AvgFocusScore,
    float MinFocusScore,
    float MaxFocusScore,
    int DurationMinutes);

public sealed record MindWanderingEvent(
    DateTimeOffset Timestamp,
    float FocusScoreAtEvent,
    string? Context,  // e.g., "during_problem_solving"
    string? Trigger); // e.g., "fatigue_detected"

public sealed record MicrobreakRecord(
    DateTimeOffset SuggestedAt,
    bool WasTaken,
    DateTimeOffset? TakenAt,
    int DurationSeconds);

public sealed record ChronotypeRecommendation(
    string DetectedChronotype,  // morning, evening, neutral
    string OptimalStudyTime,
    string RecommendationText);

// Class-Level Focus View
public sealed record ClassFocusResponse(
    string ClassId,
    string ClassName,
    float ClassAvgFocus,
    IReadOnlyList<StudentFocusSummary> Students,
    IReadOnlyList<TimeSlotFocus> FocusByTimeSlot,
    IReadOnlyList<SubjectFocus> FocusBySubject);

public sealed record StudentFocusSummary(
    string StudentId,
    string StudentName,
    float AvgFocusScore,
    string Trend,  // improving, declining, stable
    bool NeedsAttention);

public sealed record TimeSlotFocus(
    string TimeSlot,
    float AvgFocusScore,
    int StudentCount);

public sealed record SubjectFocus(
    string Subject,
    float AvgFocusScore,
    int SessionCount);

// Focus Degradation Curve
public sealed record FocusDegradationResponse(
    IReadOnlyList<DegradationPoint> Curve);

public sealed record DegradationPoint(
    int MinutesIntoSession,
    float AvgFocusScore,
    int SampleSize);

// Focus Experiment Results
public sealed record FocusExperimentsResponse(
    IReadOnlyList<FocusExperiment> Experiments);

public sealed record FocusExperiment(
    string ExperimentId,
    string Name,
    string Status,  // running, completed, cancelled
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyList<ExperimentVariant> Variants,
    ExperimentMetrics? Results);

public sealed record ExperimentVariant(
    string VariantId,
    string Name,
    int ParticipantCount);

public sealed record ExperimentMetrics(
    float FocusScoreDelta,
    float CompletionRateDelta,
    float TimeOnTaskDelta,
    bool IsStatisticallySignificant);

// Students Needing Attention
public sealed record StudentsNeedingAttentionResponse(
    IReadOnlyList<StudentAttentionAlert> Alerts);

public sealed record StudentAttentionAlert(
    string StudentId,
    string StudentName,
    string ClassId,
    string AlertType,  // low_focus, declining_trend, high_mind_wandering
    float CurrentScore,
    float BaselineScore,
    string Recommendation);
