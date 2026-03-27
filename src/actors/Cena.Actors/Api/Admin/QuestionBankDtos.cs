// =============================================================================
// Cena Platform -- Question Bank DTOs
// ADM-010: Question bank browser and management
// =============================================================================

namespace Cena.Actors.Api.Admin;

// Question List
public sealed record QuestionListResponse(
    IReadOnlyList<QuestionListItem> Questions,
    int Total,
    int Page,
    int PageSize);

public sealed record QuestionListItem(
    string Id,
    string StemPreview,
    string Subject,
    IReadOnlyList<string> Concepts,
    int BloomsLevel,
    float Difficulty,
    QuestionStatus Status,
    int QualityScore,
    int UsageCount,
    float? SuccessRate);

public enum QuestionStatus
{
    Draft,
    InReview,
    Approved,
    Published,
    Deprecated
}

// Question Filters
public sealed record QuestionFiltersResponse(
    IReadOnlyList<string> Subjects,
    IReadOnlyList<ConceptFilter> Concepts,
    IReadOnlyList<string> Grades);

public sealed record ConceptFilter(
    string Id,
    string Name,
    string Subject);

// Question Detail
public sealed record QuestionBankDetailResponse(
    string Id,
    string Stem,
    string StemHtml,
    IReadOnlyList<AnswerOptionDetail> Options,
    IReadOnlyList<string> CorrectAnswers,
    string Subject,
    string Topic,
    string Grade,
    int BloomsLevel,
    float Difficulty,
    IReadOnlyList<string> ConceptIds,
    IReadOnlyList<string> ConceptNames,
    QuestionStatus Status,
    int QualityScore,
    string SourceType,  // authored, ingested
    string? SourceItemId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string CreatedBy,
    QuestionStats? Performance,
    QuestionProvenance? Provenance);

public sealed record AnswerOptionDetail(
    string Id,
    string Label,
    string Text,
    string TextHtml,
    bool IsCorrect,
    string? DistractorRationale);

public sealed record QuestionStats(
    int TimesServed,
    float AccuracyRate,
    float AvgTimeSeconds,
    float DiscriminationIndex,
    IReadOnlyList<PerformanceByDifficulty> PerformanceBreakdown);

public sealed record PerformanceByDifficulty(
    string Difficulty,
    int Attempts,
    float Accuracy);

public sealed record QuestionProvenance(
    string SourceItemId,
    string SourceUrl,
    DateTimeOffset ImportedAt,
    string ImportedBy,
    string? OriginalText);

// Update Question
public sealed record UpdateBankQuestionRequest(
    string? Stem,
    IReadOnlyList<UpdateAnswerOption>? Options,
    string? CorrectAnswer,
    float? Difficulty,
    IReadOnlyList<string>? ConceptIds,
    QuestionStatus? Status);

public sealed record UpdateAnswerOption(
    string Id,
    string Text,
    bool IsCorrect);

// Deprecate Question
public sealed record DeprecateBankQuestionRequest(
    string Reason,
    bool RemoveFromServing);

// Concept Autocomplete
public sealed record ConceptAutocompleteResponse(
    IReadOnlyList<ConceptMatch> Matches);

public sealed record ConceptMatch(
    string Id,
    string Name,
    string Subject,
    int QuestionCount);
