// =============================================================================
// Cena Platform -- Knowledge/Content API DTOs (STB-08 Phase 1)
// Concepts and learning path contracts
// =============================================================================

namespace Cena.Api.Contracts.Content;

// ═════════════════════════════════════════════════════════════════════════════
// Concept DTOs
// ═════════════════════════════════════════════════════════════════════════════

public record ConceptListDto(ConceptSummary[] Items);

public record ConceptSummary(
    string ConceptId,
    string Name,
    string Subject,
    string? Topic,
    string Difficulty,    // 'beginner' | 'intermediate' | 'advanced'
    string Status);       // 'locked' | 'available' | 'in-progress' | 'mastered'

public record ConceptDetailDto(
    string ConceptId,
    string Name,
    string Description,
    string Subject,
    string? Topic,
    string Difficulty,
    string Status,
    double? CurrentMastery,  // 0.0 - 1.0, null if not started
    string[] Prerequisites,  // ConceptIds that must be completed first
    string[] Dependencies,   // ConceptIds that depend on this one
    int EstimatedMinutes,
    int QuestionCount);

// ═════════════════════════════════════════════════════════════════════════════
// Knowledge Path DTOs
// ═════════════════════════════════════════════════════════════════════════════

public record PathDto(
    string FromConceptId,
    string ToConceptId,
    PathNode[] Nodes,
    PathEdge[] Edges,
    int TotalSteps,
    int EstimatedMinutes);

public record PathNode(
    string ConceptId,
    string Name,
    int StepNumber,
    string Status);

public record PathEdge(
    string FromConceptId,
    string ToConceptId,
    string Relationship);  // 'prerequisite' | 'dependency'
