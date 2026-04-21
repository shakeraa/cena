// =============================================================================
// Cena Platform — In-memory exam-catalog model (prr-220, ADR-0050)
//
// Immutable read-side shape produced by ExamCatalogLoader. The HTTP
// surface in CatalogEndpoints.cs projects this model through the
// locale fallback and tenant overlay into the Cena.Api.Contracts DTOs.
//
// No raw YAML parsing here — see ExamCatalogYamlLoader for the
// deserializer. This file is pure data records so the test suite can
// construct catalogs in-memory without touching disk.
// =============================================================================

namespace Cena.Student.Api.Host.Catalog;

/// <summary>Localized name + short description for a single locale.</summary>
public sealed record LocalizedEntry(string Name, string? ShortDescription);

/// <summary>Sitting record used across the loader and the DTO projection.</summary>
public sealed record CatalogSitting(
    string Code,
    string AcademicYear,
    string Season,
    string Moed,
    DateOnly CanonicalDate);

/// <summary>A single topic inside a target, with per-locale display.</summary>
public sealed record CatalogTopic(
    string TopicId,
    IReadOnlyDictionary<string, LocalizedEntry> Display);

/// <summary>
/// Catalog entry for a single exam target. Locale-neutral — the
/// per-locale selection is done at the endpoint layer.
/// </summary>
public sealed record CatalogTarget(
    string ExamCode,
    string Family,
    string Region,
    string? Track,
    int? Units,
    string Regulator,
    string? MinistrySubjectCode,
    IReadOnlyList<string> MinistryQuestionPaperCodes,
    string Availability,
    string ItemBankStatus,
    bool PassbackEligible,
    int DefaultLeadDays,
    IReadOnlyList<CatalogSitting> Sittings,
    IReadOnlyDictionary<string, LocalizedEntry> Display,
    IReadOnlyList<CatalogTopic> Topics);

/// <summary>
/// Full in-memory catalog snapshot. Immutable; replaced atomically on
/// admin rebuild so readers never see a half-applied state.
/// </summary>
public sealed record CatalogSnapshot(
    string CatalogVersion,
    DateTimeOffset LoadedAt,
    IReadOnlyList<string> FamilyOrder,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Families,
    IReadOnlyDictionary<string, CatalogTarget> TargetsByCode);

/// <summary>
/// Tenant overlay — resolved by the catalog service from tenant config.
/// `EnabledExamCodes` null means "all codes are enabled for this tenant".
/// </summary>
public sealed record CatalogTenantOverlay(
    string? TenantId,
    IReadOnlyList<string>? EnabledExamCodes,
    IReadOnlyList<string> DisabledExamCodes)
{
    public static CatalogTenantOverlay Empty { get; } =
        new(null, null, Array.Empty<string>());
}
