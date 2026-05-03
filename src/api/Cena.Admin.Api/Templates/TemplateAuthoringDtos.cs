// =============================================================================
// Cena Platform — Template Authoring DTOs (prr-202)
//
// Wire shapes for the admin CRUD + preview surface at
// /api/admin/templates. DTOs mirror ParametricTemplateDocument without the
// audit/lifecycle fields the caller cannot set directly; those are populated
// from ClaimsPrincipal in ParametricTemplateAuthoringService.
//
// Kept separate from ParametricTemplateDto (TemplateGenerateEndpoint) because
// the dry-run endpoint from prr-200 takes a *transient* template by value,
// while these DTOs round-trip through persistence.
// =============================================================================

namespace Cena.Admin.Api.Templates;

public sealed record TemplateListFilterDto(
    string? Subject,
    string? Topic,
    string? Track,
    string? Difficulty,
    string? Methodology,
    string? Status,
    bool IncludeInactive,
    int Page,
    int PageSize);

public sealed record TemplateListItemDto(
    string Id,
    int Version,
    string Subject,
    string Topic,
    string Track,
    string Difficulty,
    string Methodology,
    int BloomsLevel,
    string Language,
    string Status,
    bool Active,
    DateTimeOffset UpdatedAt,
    string LastMutatedBy,
    int SlotCount);

public sealed record TemplateListResponseDto(
    IReadOnlyList<TemplateListItemDto> Items,
    int Page,
    int PageSize,
    int Total);

public sealed record TemplateDetailDto(
    string Id,
    int Version,
    string Subject,
    string Topic,
    string Track,
    string Difficulty,
    string Methodology,
    int BloomsLevel,
    string Language,
    string StemTemplate,
    string SolutionExpr,
    string? VariableName,
    IReadOnlyList<string> AcceptShapes,
    IReadOnlyList<ParametricSlotPayloadDto> Slots,
    IReadOnlyList<SlotConstraintPayloadDto> Constraints,
    IReadOnlyList<DistractorRulePayloadDto> DistractorRules,
    string Status,
    bool Active,
    DateTimeOffset CreatedAt,
    string CreatedBy,
    DateTimeOffset UpdatedAt,
    string LastMutatedBy);

public sealed record ParametricSlotPayloadDto(
    string Name,
    string Kind,
    int IntegerMin,
    int IntegerMax,
    IReadOnlyList<int> IntegerExclude,
    int NumeratorMin,
    int NumeratorMax,
    int DenominatorMin,
    int DenominatorMax,
    bool ReduceRational,
    IReadOnlyList<string> Choices);

public sealed record SlotConstraintPayloadDto(string Description, string PredicateExpr);

public sealed record DistractorRulePayloadDto(
    string MisconceptionId, string FormulaExpr, string? LabelHint);

public sealed record TemplateCreateRequestDto(
    string Id,
    string Subject,
    string Topic,
    string Track,
    string Difficulty,
    string Methodology,
    int BloomsLevel,
    string Language,
    string StemTemplate,
    string SolutionExpr,
    string? VariableName,
    IReadOnlyList<string> AcceptShapes,
    IReadOnlyList<ParametricSlotPayloadDto> Slots,
    IReadOnlyList<SlotConstraintPayloadDto>? Constraints,
    IReadOnlyList<DistractorRulePayloadDto>? DistractorRules,
    string? Status);

public sealed record TemplateUpdateRequestDto(
    string Subject,
    string Topic,
    string Track,
    string Difficulty,
    string Methodology,
    int BloomsLevel,
    string Language,
    string StemTemplate,
    string SolutionExpr,
    string? VariableName,
    IReadOnlyList<string> AcceptShapes,
    IReadOnlyList<ParametricSlotPayloadDto> Slots,
    IReadOnlyList<SlotConstraintPayloadDto>? Constraints,
    IReadOnlyList<DistractorRulePayloadDto>? DistractorRules,
    string? Status,
    int ExpectedVersion);

// ── Preview ──────────────────────────────────────────────────────────────

public sealed record TemplatePreviewRequestDto(
    long BaseSeed,
    int SampleCount);

public sealed record TemplatePreviewSampleDto(
    long Seed,
    bool Accepted,
    string? Stem,
    string? CanonicalAnswer,
    IReadOnlyList<TemplatePreviewDistractorDto> Distractors,
    string? FailureKind,
    string? FailureDetail,
    double LatencyMs);

public sealed record TemplatePreviewDistractorDto(
    string MisconceptionId, string Text, string? Rationale);

public sealed record TemplatePreviewResponseDto(
    string TemplateId,
    int TemplateVersion,
    int RequestedCount,
    int AcceptedCount,
    IReadOnlyList<TemplatePreviewSampleDto> Samples,
    string? OverallError,
    double TotalLatencyMs);
