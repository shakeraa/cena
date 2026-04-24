// =============================================================================
// Cena Platform -- Question Bank DTOs
// ADM-010: Question bank browser and management
// =============================================================================

namespace Cena.Api.Contracts.Admin.QuestionBank;

public enum QuestionStatus
{
    Draft,
    InReview,
    Approved,
    Published,
    Deprecated
}

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
    float? SuccessRate,
    /// <summary>
    /// FIND-pedagogy-008 — learning-objective id if the question carries one.
    /// Null for V1 questions that have not been backfilled yet.
    /// </summary>
    string? LearningObjectiveId = null);

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
    string? Explanation,
    QuestionStats? Performance,
    QuestionProvenance? Provenance,
    QualityGateDetail? QualityGate = null,
    /// <summary>
    /// FIND-pedagogy-008 — learning-objective id. Null means "not assigned";
    /// the admin UI should prompt the author to pick one.
    /// </summary>
    string? LearningObjectiveId = null,
    /// <summary>
    /// FIND-pedagogy-008 — resolved learning-objective title for display.
    /// Null when <c>LearningObjectiveId</c> is null or the LO has been deleted.
    /// </summary>
    string? LearningObjectiveTitle = null);

/// <summary>8-dimension quality gate breakdown for frontend display.</summary>
public sealed record QualityGateDetail(
    float CompositeScore,
    string GateDecision,
    int FactualAccuracy,
    int LanguageQuality,
    int PedagogicalQuality,
    int DistractorQuality,
    int StemClarity,
    int BloomAlignment,
    int StructuralValidity,
    int CulturalSensitivity,
    int ViolationCount,
    DateTimeOffset? EvaluatedAt);

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
    QuestionStatus? Status,
    /// <summary>
    /// FIND-pedagogy-008 — update or backfill the learning objective.
    /// Null means "leave unchanged"; an empty string is rejected.
    /// </summary>
    string? LearningObjectiveId = null);

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

// Create Question (3 paths: authored, ingested, ai-generated)
public sealed record CreateQuestionRequest(
    string SourceType,             // "authored" | "ingested" | "ai-generated"
    string Stem,
    string? StemHtml,
    IReadOnlyList<CreateOptionRequest> Options,
    string Subject,
    string? Topic,
    string? Grade,
    int BloomsLevel,
    float Difficulty,
    IReadOnlyList<string>? ConceptIds,
    string Language,               // "he" | "ar" | "en"
    // Ingestion-specific
    string? SourceDocId,
    string? SourceUrl,
    string? SourceFilename,
    string? OriginalText,
    // AI-specific
    string? PromptText,
    string? ModelId,
    float? ModelTemperature,
    string? RawModelOutput,
    // Explanation (any source type)
    string? Explanation,
    /// <summary>
    /// FIND-pedagogy-008 — learning-objective id. Optional at create time to
    /// preserve back-compat with existing admin forms; the service logs a
    /// warning when it is omitted so curriculum authors can audit coverage.
    /// </summary>
    string? LearningObjectiveId = null);

public sealed record CreateOptionRequest(
    string Label,
    string Text,
    string? TextHtml,
    bool IsCorrect,
    string? DistractorRationale);

// Update Explanation
public sealed record UpdateExplanationRequest(string Explanation);

// Publish Question
public sealed record PublishQuestionRequest(string? Reason);

// Add Language Version
public sealed record AddLanguageVersionRequest(
    string Language,
    string Stem,
    string? StemHtml,
    IReadOnlyList<CreateOptionRequest> Options);

// =============================================================================
// FIND-pedagogy-008 — Learning objectives
// =============================================================================

/// <summary>
/// List response for <c>GET /api/admin/learning-objectives</c>. Flat list so
/// the admin Vue picker can render it without additional joins.
/// </summary>
public sealed record LearningObjectiveListResponse(
    IReadOnlyList<LearningObjectiveListItem> Objectives,
    int Total);

/// <summary>
/// Single learning-objective row for the admin picker.
/// </summary>
/// <param name="Id">Stable Marten document id.</param>
/// <param name="Code">Human-readable curriculum code (e.g., MATH-ALG-LINEAR-001).</param>
/// <param name="Title">Short student-facing title.</param>
/// <param name="Description">Long-form description (SWBAT statement).</param>
/// <param name="Subject">Subject slug.</param>
/// <param name="Grade">Grade band (nullable for cross-grade objectives).</param>
/// <param name="CognitiveProcess">
/// Anderson &amp; Krathwohl (2001) cognitive-process dimension
/// (Remember/Understand/Apply/Analyze/Evaluate/Create) as a string.
/// </param>
/// <param name="KnowledgeType">
/// Anderson &amp; Krathwohl (2001) knowledge dimension
/// (Factual/Conceptual/Procedural/Metacognitive) as a string.
/// </param>
/// <param name="BloomsLevel">
/// Back-compat single-int view of the cognitive-process (1..6).
/// </param>
/// <param name="ConceptIds">Concepts the objective covers.</param>
/// <param name="StandardsAlignment">Framework → code mapping (Bagrut, Common Core, etc.).</param>
public sealed record LearningObjectiveListItem(
    string Id,
    string Code,
    string Title,
    string Description,
    string Subject,
    string? Grade,
    string CognitiveProcess,
    string KnowledgeType,
    int BloomsLevel,
    IReadOnlyList<string> ConceptIds,
    IReadOnlyDictionary<string, string> StandardsAlignment);
