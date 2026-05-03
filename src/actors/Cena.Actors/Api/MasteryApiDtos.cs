// =============================================================================
// Cena Platform -- Mastery REST API DTOs
// MST-017: Response types for mastery endpoints (REST, not GraphQL)
// =============================================================================

namespace Cena.Actors.Api;

/// <summary>
/// Per-concept mastery state for API consumers.
/// </summary>
public sealed record ConceptMasteryDto(
    string ConceptId,
    string? Name,
    string? TopicCluster,
    float MasteryProbability,
    float RecallProbability,
    float EffectiveMastery,
    float HalfLifeHours,
    int BloomLevel,
    string QualityQuadrant,
    string MasteryLevel,
    DateTimeOffset LastInteraction,
    int AttemptCount,
    int CorrectCount,
    int CurrentStreak,
    string? ActiveMethodology = null,
    string? MethodologySource = null,
    string? MethodologyResolvedLevel = null);

/// <summary>
/// Aggregated progress for a topic cluster.
/// </summary>
public sealed record TopicProgressDto(
    string TopicClusterId,
    string? Name,
    int ConceptCount,
    int MasteredCount,
    float AverageMastery,
    ConceptMasteryDto? WeakestConcept,
    string? TopicMethodology = null,
    string? TopicMethodologySource = null,
    bool TopicHasSufficientData = false,
    int TopicMethodologyAttempts = 0);

/// <summary>
/// A concept on the learning frontier.
/// </summary>
public sealed record FrontierConceptDto(
    string ConceptId,
    string? Name,
    string? TopicCluster,
    float PSI,
    float CurrentMastery,
    float Rank);

/// <summary>
/// A concept needing review due to decay.
/// </summary>
public sealed record DecayAlertDto(
    string ConceptId,
    string? Name,
    float RecallProbability,
    float HoursSinceReview,
    float ReviewPriority);

/// <summary>
/// Full mastery overview for a student.
/// </summary>
public sealed record StudentMasteryResponse(
    string StudentId,
    IReadOnlyList<ConceptMasteryDto> Concepts,
    int TotalConcepts,
    int MasteredCount,
    float OverallMastery);
