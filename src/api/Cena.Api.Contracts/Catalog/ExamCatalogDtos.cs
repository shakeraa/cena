// =============================================================================
// Cena Platform — Exam Catalog DTOs (prr-220, ADR-0050)
//
// Serialized shape of GET /api/v1/catalog/exam-targets[?locale=] and
// GET /api/v1/catalog/exam-targets/{code}/topics.
//
// Tenant overlay lives inside the top-level response so the admin SPA
// and student SPA can both distinguish "global menu" from "my tenant's
// filtered menu". ADR-0050 §2: Ministry codes are the primary key
// carried as metadata; display names are localized.
//
// Bagrut-reference-only note: this catalog carries no student-facing
// item text — only target metadata, topic ids, and Ministry codes
// (which are legitimate public identifiers, not Ministry item text).
// The reference-only invariant (ADR-0043) governs ITEM delivery, not
// catalog metadata.
// =============================================================================

namespace Cena.Api.Contracts.Catalog;

/// <summary>
/// A single sitting (moed) of an exam, with the canonical date the
/// Ministry/regulator published for it.
/// </summary>
public sealed record ExamSittingDto(
    string Code,
    string AcademicYear,
    string Season,   // summer | winter | spring | autumn
    string Moed,     // A | B | C | Special
    DateOnly CanonicalDate);

/// <summary>
/// Localized display metadata for a target or topic, resolved for the
/// requested locale (with English fallback).
/// </summary>
public sealed record LocalizedDisplayDto(
    string Name,
    string? ShortDescription);

/// <summary>
/// A topic inside a target — the grain the student self-assessment UI
/// iterates when checking coverage.
/// </summary>
public sealed record ExamTopicDto(
    string TopicId,
    LocalizedDisplayDto Display);

/// <summary>
/// A catalog entry for one exam target. Serialized both in the grouped
/// list response and in the per-target topics response.
/// </summary>
public sealed record ExamTargetDto(
    string ExamCode,
    string Family,
    string Region,
    string? Track,
    int? Units,
    string Regulator,
    string? MinistrySubjectCode,
    IReadOnlyList<string> MinistryQuestionPaperCodes,
    string Availability,      // launch | roadmap | queued
    string ItemBankStatus,    // full | reference-only | unavailable
    bool PassbackEligible,
    int DefaultLeadDays,
    IReadOnlyList<ExamSittingDto> Sittings,
    LocalizedDisplayDto Display,
    IReadOnlyList<ExamTopicDto> Topics);

/// <summary>
/// A group of targets belonging to the same region/family.
/// </summary>
public sealed record ExamTargetGroupDto(
    string Family,                          // BAGRUT | TAWJIHI | STANDARDIZED | INTERNATIONAL
    IReadOnlyList<ExamTargetDto> Targets);

/// <summary>
/// Tenant overlay applied on top of the global catalog. `EnabledExamCodes`
/// null = all global codes enabled for this tenant. Present list = explicit
/// allow-list. `DisabledExamCodes` subtracts from the allow-list.
/// </summary>
public sealed record TenantCatalogOverlayDto(
    string? TenantId,
    IReadOnlyList<string>? EnabledExamCodes,
    IReadOnlyList<string> DisabledExamCodes);

/// <summary>
/// Top-level response for GET /api/v1/catalog/exam-targets.
/// </summary>
public sealed record ExamTargetCatalogDto(
    string CatalogVersion,
    string Locale,
    string LocaleFallbackUsed,    // requested locale vs served (for the banner in the SPA)
    IReadOnlyList<string> FamilyOrder,
    IReadOnlyList<ExamTargetGroupDto> Groups,
    TenantCatalogOverlayDto Overlay);

/// <summary>
/// Response for GET /api/v1/catalog/exam-targets/{code}/topics.
/// </summary>
public sealed record ExamTargetTopicsDto(
    string ExamCode,
    string CatalogVersion,
    string Locale,
    LocalizedDisplayDto Display,
    IReadOnlyList<ExamTopicDto> Topics);

/// <summary>
/// Response for POST /api/admin/catalog/rebuild.
/// </summary>
public sealed record CatalogRebuildResultDto(
    string PreviousVersion,
    string CurrentVersion,
    int TargetsLoaded,
    IReadOnlyList<string> Warnings);
