// =============================================================================
// Cena Platform — Ministry Accommodation Code Mapping (RDY-066 Phase 1A)
//
// Israeli Ministry of Education publishes formal accommodation codes
// (התאמות, hatamot) for students with diagnosed learning differences.
// A student's Ministry-issued code is the primary input when a parent
// enrols a minor in Cena with formal accommodations.
//
// This mapping table translates those codes into the typed
// AccommodationDimension set that Cena's runtime understands. The
// table is explicitly scoped to Phase 1A dimensions; codes that
// require Phase 1B dimensions (e.g. high-contrast theme, reduced
// animations) are listed but produce a PARTIAL mapping until Phase 1B
// lands.
//
// Source: Ministry guidance documents + Prof. Amjad (curriculum
// expert) sign-off required before production use. Every row carries
// a citation field so the sign-off can be tracked per row.
//
// WARNING — this file is a DRAFT. Prof. Amjad + Ran (compliance) MUST
// sign off row by row before any parent sees the mapping. The ship
// guard: an unapproved mapping causes the parent-console to fall back
// to "pick your accommodations manually" rather than claiming a
// Ministry-code-to-Cena-profile equivalence.
// =============================================================================

using System.Collections.Immutable;

namespace Cena.Actors.Accommodations;

/// <summary>Review state of a single mapping row.</summary>
public enum MinistryMappingStatus
{
    /// <summary>Engineering guess pending expert review; not production-safe.</summary>
    Draft = 0,

    /// <summary>Under Prof. Amjad / Ran review; pilot-only.</summary>
    UnderReview = 1,

    /// <summary>Signed off; safe for production parent-console.</summary>
    Approved = 2
}

/// <summary>One row in the Ministry-code → Cena-profile table.</summary>
public sealed record MinistryAccommodationRow(
    string MinistryCode,
    string HebrewLabel,
    string EnglishGloss,
    IReadOnlyCollection<AccommodationDimension> Dimensions,
    MinistryMappingStatus Status,
    string Citation);

/// <summary>
/// Static catalogue. Today every row is <see cref="MinistryMappingStatus.Draft"/>;
/// the admin parent-console must show the status to the parent so they
/// can confirm or override the mapping.
/// </summary>
public static class MinistryAccommodationMapping
{
    public static readonly ImmutableArray<MinistryAccommodationRow> Rows =
        ImmutableArray.Create(
            new MinistryAccommodationRow(
                MinistryCode: "1",
                HebrewLabel: "הארכת זמן",
                EnglishGloss: "Extended time",
                Dimensions: new[] { AccommodationDimension.ExtendedTime },
                Status: MinistryMappingStatus.Draft,
                Citation: "Ministry hatama 1 (extended-time) maps directly to Cena.ExtendedTime. Pending Prof. Amjad sign-off."),

            new MinistryAccommodationRow(
                MinistryCode: "2",
                HebrewLabel: "הקראה",
                EnglishGloss: "Text read aloud",
                Dimensions: new[] { AccommodationDimension.TtsForProblemStatements },
                Status: MinistryMappingStatus.Draft,
                Citation: "Ministry hatama 2 (הקראה) maps to Cena.TtsForProblemStatements. Pending expert sign-off; Cena voice quality review required before Approved."),

            new MinistryAccommodationRow(
                MinistryCode: "3",
                HebrewLabel: "הפחתת הסחות דעת",
                EnglishGloss: "Distraction-reduced environment",
                Dimensions: new[] { AccommodationDimension.DistractionReducedLayout },
                Status: MinistryMappingStatus.Draft,
                Citation: "Ministry hatama 3 (reduced-distraction) maps to Cena.DistractionReducedLayout."),

            new MinistryAccommodationRow(
                MinistryCode: "4",
                HebrewLabel: "ללא השוואת עמיתים",
                EnglishGloss: "No peer comparison displayed",
                Dimensions: new[] { AccommodationDimension.NoComparativeStats },
                Status: MinistryMappingStatus.Draft,
                Citation: "Ministry hatama 4 (no peer comparison) maps to Cena.NoComparativeStats — the only Cena dimension that a general-purpose platform can honour out-of-box for ALL students (RDY-066 panel argument)."),

            new MinistryAccommodationRow(
                MinistryCode: "5",
                HebrewLabel: "ניגודיות גבוהה",
                EnglishGloss: "High contrast display",
                // Phase 1B dimension — mapping emits an empty set for
                // Phase 1A so the parent-console falls back to
                // "manually select" until Tamar's contrast audit
                // unblocks the HighContrastTheme dimension.
                Dimensions: Array.Empty<AccommodationDimension>(),
                Status: MinistryMappingStatus.Draft,
                Citation: "Ministry hatama 5 (high-contrast) requires Cena.HighContrastTheme which is Phase 1B; Phase 1A maps to empty set so the parent sees 'manual setup required'."),

            new MinistryAccommodationRow(
                MinistryCode: "6",
                HebrewLabel: "כפתור 'דלג' / 'חזור'",
                EnglishGloss: "Reduced-animation UI",
                Dimensions: Array.Empty<AccommodationDimension>(),
                Status: MinistryMappingStatus.Draft,
                Citation: "Ministry hatama 6 (reduced-animation) maps to Cena.ReducedAnimations (Phase 1B).")
        );

    /// <summary>Lookup by Ministry code. Null when unknown.</summary>
    public static MinistryAccommodationRow? Lookup(string ministryCode)
        => Rows.FirstOrDefault(r => r.MinistryCode == ministryCode);

    /// <summary>
    /// Translate a Ministry code into the Cena dimension set.
    /// ONLY rows with <see cref="MinistryMappingStatus.Approved"/>
    /// produce a mapping — Draft / UnderReview fall through to empty
    /// so the parent-console shows "manually confirm" rather than
    /// silently applying an unapproved Ministry equivalence.
    ///
    /// Callers that still want the *draft* mapping (e.g. the admin
    /// review tool) should call <see cref="Lookup"/> directly.
    /// </summary>
    public static IReadOnlyCollection<AccommodationDimension> Translate(string ministryCode)
    {
        var row = Lookup(ministryCode);
        if (row is null) return Array.Empty<AccommodationDimension>();
        if (row.Status != MinistryMappingStatus.Approved) return Array.Empty<AccommodationDimension>();
        return row.Dimensions;
    }
}
