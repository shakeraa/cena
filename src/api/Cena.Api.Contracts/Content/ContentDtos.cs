// =============================================================================
// Cena Platform -- Content Contracts (DB-05)
// Content serving DTOs.
// =============================================================================

namespace Cena.Api.Contracts.Content;

public sealed record QuestionDto(
    string QuestionId,
    string Subject,
    IReadOnlyList<string> ConceptIds,
    string Stem,
    IReadOnlyList<QuestionOptionDto> Options,
    string BloomsLevel,
    float Difficulty,
    IReadOnlyDictionary<string, LanguageVersionDto> LanguageVersions,
    string? Explanation,
    int Version);

public sealed record QuestionOptionDto(
    string Id,
    string Label,
    string Text,
    bool IsCorrect);

public sealed record LanguageVersionDto(
    string Language,
    string Stem,
    IReadOnlyList<QuestionOptionDto> Options);

public sealed record SubjectSummaryDto(
    string SubjectId,
    string SubjectName,
    int QuestionCount);

public sealed record ExplanationDto(
    string QuestionId,
    string Explanation,
    string? AiPrompt,
    int Version);
