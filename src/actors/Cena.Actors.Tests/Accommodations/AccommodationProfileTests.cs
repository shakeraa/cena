// =============================================================================
// RDY-066 Phase 1A — AccommodationProfile + MinistryAccommodationMapping tests
//
// Proves the guardrails that keep accommodations opt-in, phase-bounded,
// and auditable:
//   * Profile defaults to empty — no silent enablement.
//   * Phase 1B dimensions refuse to activate even if asked.
//   * Unapproved Ministry codes produce an empty mapping (no silent
//     equivalence claim).
//   * Event record carries the consent-document hash when present.
// =============================================================================

using Cena.Actors.Accommodations;
using Xunit;

namespace Cena.Actors.Tests.Accommodations;

public class AccommodationProfileTests
{
    [Fact]
    public void Default_profile_has_no_enabled_dimensions()
    {
        var p = AccommodationProfile.Default("stu-anon-1");
        Assert.Empty(p.EnabledDimensions);
        foreach (var d in Enum.GetValues<AccommodationDimension>())
            Assert.False(p.IsEnabled(d));
    }

    [Fact]
    public void IsEnabled_returns_true_for_shipped_phase1a_dimension()
    {
        var p = new AccommodationProfile(
            StudentAnonId: "stu-anon-1",
            EnabledDimensions: new HashSet<AccommodationDimension>
            {
                AccommodationDimension.ExtendedTime,
                AccommodationDimension.TtsForProblemStatements
            },
            Assigner: AccommodationAssigner.Parent,
            AssignerSignature: "parent-hmac-abcdef",
            AssignedAtUtc: DateTimeOffset.UtcNow);

        Assert.True(p.IsEnabled(AccommodationDimension.ExtendedTime));
        Assert.True(p.IsEnabled(AccommodationDimension.TtsForProblemStatements));
        Assert.False(p.IsEnabled(AccommodationDimension.DistractionReducedLayout));
    }

    [Theory]
    [InlineData(AccommodationDimension.TtsForHints)]
    [InlineData(AccommodationDimension.HighContrastTheme)]
    [InlineData(AccommodationDimension.ReducedAnimations)]
    [InlineData(AccommodationDimension.ProgressIndicatorToggle)]
    // NOTE: LdAnxiousFriendly (prr-029) intentionally not listed — it IS
    // shipped in Phase 1B and must activate. See LdAnxiousHintGovernorTests.
    public void IsEnabled_refuses_unshipped_phase_1b_dimensions_even_if_present_in_set(
        AccommodationDimension phase1bDimension)
    {
        // Even if an older event / replay has a Phase 1B dimension in
        // the set, IsEnabled must refuse — the UI doesn't render these
        // yet and silently activating would produce a degraded UX.
        var p = new AccommodationProfile(
            StudentAnonId: "stu-anon-1",
            EnabledDimensions: new HashSet<AccommodationDimension> { phase1bDimension },
            Assigner: AccommodationAssigner.Parent,
            AssignerSignature: "parent-hmac",
            AssignedAtUtc: DateTimeOffset.UtcNow);
        Assert.False(p.IsEnabled(phase1bDimension));
    }

    [Fact]
    public void Phase1A_shipped_set_contains_exactly_four_dimensions()
    {
        Assert.Equal(4, Phase1ADimensions.Shipped.Count);
        Assert.Contains(AccommodationDimension.ExtendedTime, Phase1ADimensions.Shipped);
        Assert.Contains(AccommodationDimension.TtsForProblemStatements, Phase1ADimensions.Shipped);
        Assert.Contains(AccommodationDimension.DistractionReducedLayout, Phase1ADimensions.Shipped);
        Assert.Contains(AccommodationDimension.NoComparativeStats, Phase1ADimensions.Shipped);
    }

    [Fact]
    public void Event_record_carries_consent_document_hash_when_present()
    {
        var ev = new AccommodationProfileAssignedV1(
            StudentAnonId: "stu-anon-1",
            EnabledDimensions: new[] { AccommodationDimension.ExtendedTime },
            Assigner: AccommodationAssigner.Parent,
            AssignerSignature: "parent-hmac",
            MinistryHatamaCode: "1",
            ConsentDocumentHash: new string('a', 64),
            AssignedAtUtc: DateTimeOffset.UtcNow);

        Assert.NotNull(ev.ConsentDocumentHash);
        Assert.Equal(64, ev.ConsentDocumentHash!.Length);
    }
}

public class MinistryAccommodationMappingTests
{
    [Fact]
    public void Lookup_returns_null_for_unknown_code()
    {
        Assert.Null(MinistryAccommodationMapping.Lookup("999"));
    }

    [Fact]
    public void Lookup_returns_row_for_known_code()
    {
        var row = MinistryAccommodationMapping.Lookup("1");
        Assert.NotNull(row);
        Assert.Equal("הארכת זמן", row!.HebrewLabel);
        Assert.Contains(AccommodationDimension.ExtendedTime, row.Dimensions);
    }

    [Fact]
    public void Translate_returns_empty_for_draft_rows()
    {
        // Every Phase 1A row ships in Draft status pending expert
        // sign-off; Translate must refuse to emit a mapping claim.
        foreach (var row in MinistryAccommodationMapping.Rows)
        {
            Assert.Equal(MinistryMappingStatus.Draft, row.Status);
            Assert.Empty(MinistryAccommodationMapping.Translate(row.MinistryCode));
        }
    }

    [Fact]
    public void Translate_returns_empty_for_unknown_code()
    {
        Assert.Empty(MinistryAccommodationMapping.Translate("999"));
    }

    [Fact]
    public void Every_row_has_a_citation()
    {
        foreach (var row in MinistryAccommodationMapping.Rows)
            Assert.False(string.IsNullOrWhiteSpace(row.Citation),
                $"Ministry code {row.MinistryCode} is missing a citation; "
                + "every mapping row must cite its source or expert-judgment provenance.");
    }

    [Fact]
    public void Rows_cover_phase_1a_dimensions_at_minimum()
    {
        // The four Phase 1A dimensions must each have at least one
        // Ministry-code row that maps to them (even if currently Draft),
        // so when Prof. Amjad signs off, the parent-console immediately
        // has a working mapping table for those dimensions.
        var covered = MinistryAccommodationMapping.Rows
            .SelectMany(r => r.Dimensions)
            .ToHashSet();

        foreach (var d in Phase1ADimensions.Shipped)
            Assert.Contains(d, covered);
    }

    [Fact]
    public void Phase_1b_only_rows_emit_empty_dimension_set_today()
    {
        // Rows whose intended mapping is a Phase 1B dimension (e.g.
        // HighContrastTheme) SHOULD carry an empty Dimensions collection
        // in the Phase 1A shipping table so the parent-console falls
        // back to 'manual setup required' until the dimension ships.
        var highContrast = MinistryAccommodationMapping.Lookup("5");
        Assert.NotNull(highContrast);
        Assert.Empty(highContrast!.Dimensions);
    }
}
