// =============================================================================
// Cena Platform — BagrutTaxonomyCatalog tests (ADR-0062 Phase 0)
//
// Locks the closed-set canonicalizer contract:
//   - Multiple alias forms (conceptId / leafId / track-relative path /
//     SkillCode form) all resolve to the same (SkillCode, LeafEntry).
//   - Track-relative paths disambiguate via track hint, falling back to
//     5u → 4u → 3u when no hint is given.
//   - Unknown aliases return false (no silent fork; ADR-0062 §Risks).
//   - Hyphen / underscore folding is normalised so callers don't have to
//     worry about which form they're holding.
//   - The on-disk taxonomy JSON parses end-to-end without throwing
//     (sanity check that the loader keeps tracking the published shape).
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Tests.Mastery;

public sealed class BagrutTaxonomyCatalogTests
{
    private const string SyntheticJson = """
    {
      "version": "test",
      "tracks": {
        "math_5u": {
          "name": "5-unit",
          "topics": {
            "calculus": {
              "name": "Calculus",
              "subtopics": {
                "derivative_rules":           { "conceptId": "CAL-003", "bloom_range": [3, 5] },
                "applications_of_derivatives":{ "conceptId": "CAL-004", "bloom_range": [4, 6] }
              }
            },
            "algebra": {
              "name": "Algebra",
              "subtopics": {
                "quadratic_equations": { "conceptId": "ALG-004", "bloom_range": [2, 4] }
              }
            }
          }
        },
        "math_4u": {
          "name": "4-unit",
          "topics": {
            "algebra": {
              "name": "Algebra",
              "subtopics": {
                "quadratic_equations": { "conceptId": "ALG-004", "bloom_range": [2, 4] }
              }
            }
          }
        }
      }
    }
    """;

    private static BagrutTaxonomyCatalog Synthetic() => BagrutTaxonomyCatalog.Parse(SyntheticJson);

    [Fact]
    public void Parse_LoadsAllLeaves_DeterministicOrder()
    {
        var cat = Synthetic();

        // 3 leaves total: 4u/algebra/quadratic_equations + 5u/algebra/quadratic_equations + 5u/calculus/derivative_rules + 5u/calculus/applications
        // = 4 leaves
        Assert.Equal(4, cat.AllLeaves.Count);

        // Ordered by track, topic, subtopic — deterministic regardless of
        // System.Text.Json enumeration quirks.
        Assert.Equal("math_4u.algebra.quadratic_equations", cat.AllLeaves[0].LeafId);
        Assert.Equal("math_5u.algebra.quadratic_equations", cat.AllLeaves[1].LeafId);
        Assert.Equal("math_5u.calculus.applications_of_derivatives", cat.AllLeaves[2].LeafId);
        Assert.Equal("math_5u.calculus.derivative_rules", cat.AllLeaves[3].LeafId);
    }

    [Fact]
    public void TryCanonicalize_ConceptId_Resolves()
    {
        var cat = Synthetic();
        Assert.True(cat.TryCanonicalize("CAL-003", out var sc, out var leaf));
        Assert.Equal("math.calculus.derivative-rules", sc.Value);
        Assert.Equal("CAL-003", leaf!.ConceptId);
        Assert.Equal("math_5u", leaf.TrackId);
    }

    [Fact]
    public void TryCanonicalize_FullLeafId_Resolves()
    {
        var cat = Synthetic();
        Assert.True(cat.TryCanonicalize("math_5u.calculus.derivative_rules", out var sc, out var leaf));
        Assert.Equal("math.calculus.derivative-rules", sc.Value);
        Assert.Equal("math_5u", leaf!.TrackId);
    }

    [Fact]
    public void TryCanonicalize_SkillCodeForm_Resolves()
    {
        var cat = Synthetic();
        Assert.True(cat.TryCanonicalize("math.calculus.derivative-rules", out var sc, out var leaf));
        Assert.Equal("math.calculus.derivative-rules", sc.Value);
        Assert.NotNull(leaf);
    }

    [Fact]
    public void TryCanonicalize_TrackRelative_FallsBackTo5u()
    {
        var cat = Synthetic();
        Assert.True(cat.TryCanonicalize("calculus.derivative_rules", trackHint: null, out var sc, out var leaf));
        Assert.Equal("math_5u", leaf!.TrackId);
        Assert.Equal("math.calculus.derivative-rules", sc.Value);
    }

    [Fact]
    public void TryCanonicalize_TrackRelative_HonoursHint()
    {
        var cat = Synthetic();
        Assert.True(cat.TryCanonicalize("algebra.quadratic_equations", trackHint: "math_4u", out _, out var leaf));
        Assert.Equal("math_4u", leaf!.TrackId);
    }

    [Fact]
    public void TryCanonicalize_HyphenUnderscoreFolding_BothFormsAccepted()
    {
        var cat = Synthetic();
        Assert.True(cat.TryCanonicalize("math.calculus.derivative_rules", out _, out var leafA));
        Assert.True(cat.TryCanonicalize("math.calculus.derivative-rules", out _, out var leafB));
        Assert.Equal(leafA!.LeafId, leafB!.LeafId);
    }

    [Fact]
    public void TryCanonicalize_CaseInsensitive()
    {
        var cat = Synthetic();
        Assert.True(cat.TryCanonicalize("Cal-003", out var sc, out _));
        Assert.Equal("math.calculus.derivative-rules", sc.Value);
    }

    [Fact]
    public void TryCanonicalize_Unknown_ReturnsFalse()
    {
        var cat = Synthetic();
        Assert.False(cat.TryCanonicalize("not-a-real-skill", out _, out _));
        Assert.False(cat.TryCanonicalize("math.unicorn.glitter", out _, out _));
        Assert.False(cat.TryCanonicalize("XYZ-999", out _, out _));
    }

    [Fact]
    public void TryCanonicalize_NullOrEmpty_ReturnsFalse()
    {
        var cat = Synthetic();
        Assert.False(cat.TryCanonicalize(null, out _, out _));
        Assert.False(cat.TryCanonicalize("", out _, out _));
        Assert.False(cat.TryCanonicalize("   ", out _, out _));
    }

    [Fact]
    public void Parse_MissingTracks_Throws()
    {
        Assert.Throws<InvalidDataException>(() =>
            BagrutTaxonomyCatalog.Parse("""{"version":"test"}"""));
    }

    [Fact]
    public void LoadFromDisk_RealTaxonomy_ParsesEndToEnd()
    {
        // Sanity check that the production taxonomy JSON shape stays
        // tracked. If `scripts/bagrut-taxonomy.json` adds a field or
        // restructures, this test fails loudly so the loader gets updated
        // alongside the data — instead of silently shipping a smaller
        // catalog at runtime.
        var cat = BagrutTaxonomyCatalog.LoadFromDisk();

        // Empirical floor: the published taxonomy has 73 conceptId entries
        // across 3 tracks. The catalog's leaf count is the sum of leaves
        // across tracks (one per (track, topic, subtopic) tuple); we
        // expect that to be at least the number of distinct conceptIds.
        Assert.True(cat.AllLeaves.Count >= 70,
            $"expected ≥70 leaves in the production taxonomy, found {cat.AllLeaves.Count}");

        // Anchor a couple of well-known leaves so a future taxonomy
        // edit that drops them shows up loudly here.
        Assert.True(cat.TryCanonicalize("CAL-003", out _, out var derivatives));
        Assert.Equal("derivative_rules", derivatives!.SubtopicKey);

        Assert.True(cat.TryCanonicalize("ALG-004", out _, out var quadratics));
        Assert.Equal("quadratic_equations", quadratics!.SubtopicKey);

        // Track hint actually disambiguates when the same skill exists at
        // multiple tracks (e.g. quadratic equations across 3u/4u/5u).
        Assert.True(cat.TryCanonicalize(
            "algebra.quadratic_equations", trackHint: "math_4u", out _, out var leaf4u));
        Assert.Equal("math_4u", leaf4u!.TrackId);
    }
}
