// =============================================================================
// prr-029 — LD-Anxious Hint Governor unit tests
//
// Coverage goals:
//   1. No-op when the profile flag is OFF — every HintLevel returns the
//      original HintContent byte-for-byte.
//   2. Rewrite L1 when the profile flag is ON — body contains the worked-
//      example marker, HasMoreHints is preserved.
//   3. L2 / L3 are always left intact (governor is scoped to L1).
//   4. Empty-context defence: when neither prereq name nor concept id
//      carry signal, the governor falls back to the original body rather
//      than producing a degraded rewrite.
//   5. Metric emission is observable via a MeterListener probe.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Accommodations;
using Cena.Actors.Hints;
using Cena.Actors.Mastery;
using Cena.Actors.Questions;
using Cena.Actors.Services;
using Xunit;

namespace Cena.Actors.Tests.Hints;

public class LdAnxiousHintGovernorTests
{
    private const string OriginalNudge = "Consider how **algebra-linear** applies here.";

    // ─── Fixture helpers ────────────────────────────────────────────────

    private static AccommodationProfile ProfileWith(params AccommodationDimension[] enabled) =>
        new(
            StudentAnonId: "stu-anon-ld-governor-test",
            EnabledDimensions: new HashSet<AccommodationDimension>(enabled),
            Assigner: AccommodationAssigner.Parent,
            AssignerSignature: "parent-hmac-test",
            AssignedAtUtc: DateTimeOffset.UtcNow);

    private static HintRequest Request(
        int level,
        IReadOnlyList<string>? prereqNames = null,
        string conceptId = "concept-alg-linear-eq")
        => new(
            HintLevel: level,
            QuestionId: "q-1",
            ConceptId: conceptId,
            PrerequisiteConceptNames: prereqNames ?? new[] { "linear equations" },
            Options: Array.Empty<QuestionOptionState>(),
            Explanation: null,
            StudentAnswer: null,
            Prerequisites: null,
            ConceptState: null);

    private static HintContent Original(int level) =>
        new(
            Text: OriginalNudge,
            HasMoreHints: level < 3);

    // ─── Test 1 — governor is a pure no-op for the majority flow ────────

    [Fact]
    public void No_accommodation_profile_leaves_hint_content_unchanged()
    {
        var governor = new LdAnxiousHintGovernor();
        var profile = AccommodationProfile.Default("stu-anon-default");

        foreach (var level in new[] { 1, 2, 3 })
        {
            var original = Original(level);
            var result = governor.Apply(original, Request(level), profile, "inst-a");

            Assert.Same(original, result);
        }
    }

    [Fact]
    public void Profile_with_only_other_dimensions_leaves_hint_content_unchanged()
    {
        var governor = new LdAnxiousHintGovernor();
        var profile = ProfileWith(
            AccommodationDimension.ExtendedTime,
            AccommodationDimension.TtsForProblemStatements,
            AccommodationDimension.DistractionReducedLayout,
            AccommodationDimension.NoComparativeStats);

        var original = Original(1);
        var result = governor.Apply(original, Request(1), profile, "inst-a");

        Assert.Same(original, result);
    }

    // ─── Test 2 — L1 rewrite on flag enabled ────────────────────────────

    [Fact]
    public void LdAnxious_profile_rewrites_L1_body_with_worked_example_marker()
    {
        var governor = new LdAnxiousHintGovernor();
        var profile = ProfileWith(AccommodationDimension.LdAnxiousFriendly);

        var original = Original(1);
        var rewritten = governor.Apply(original, Request(1), profile, "inst-a");

        Assert.NotSame(original, rewritten);
        Assert.Contains(LdAnxiousHintGovernor.WorkedExampleMarker, rewritten.Text);
        Assert.Contains("linear equations", rewritten.Text);
        Assert.Equal(original.HasMoreHints, rewritten.HasMoreHints);
    }

    [Fact]
    public void LdAnxious_L1_rewrite_falls_back_to_concept_id_when_prereq_empty()
    {
        var governor = new LdAnxiousHintGovernor();
        var profile = ProfileWith(AccommodationDimension.LdAnxiousFriendly);

        var req = Request(1, prereqNames: Array.Empty<string>(), conceptId: "concept-calc-deriv");
        var rewritten = governor.Apply(Original(1), req, profile, "inst-a");

        Assert.Contains(LdAnxiousHintGovernor.WorkedExampleMarker, rewritten.Text);
        Assert.Contains("concept-calc-deriv", rewritten.Text);
    }

    // ─── Test 3 — L2/L3 untouched ────────────────────────────────────────

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void LdAnxious_profile_does_not_rewrite_L2_or_L3(int level)
    {
        var governor = new LdAnxiousHintGovernor();
        var profile = ProfileWith(AccommodationDimension.LdAnxiousFriendly);

        var original = Original(level);
        var result = governor.Apply(original, Request(level), profile, "inst-a");

        Assert.Same(original, result);
    }

    // ─── Test 4 — Empty-context defence (never produce low-signal body) ─

    [Fact]
    public void LdAnxious_L1_with_empty_concept_and_no_prereq_returns_original()
    {
        var governor = new LdAnxiousHintGovernor();
        var profile = ProfileWith(AccommodationDimension.LdAnxiousFriendly);

        var req = Request(1, prereqNames: Array.Empty<string>(), conceptId: "   ");
        var original = Original(1);
        var result = governor.Apply(original, req, profile, "inst-a");

        Assert.Same(original, result);
    }

    // ─── Test 5 — Metric emission is observable ─────────────────────────

    [Fact]
    public void LdAnxious_L1_rewrite_increments_engagement_counter_with_institute_and_rung()
    {
        var governor = new LdAnxiousHintGovernor();
        var profile = ProfileWith(AccommodationDimension.LdAnxiousFriendly);

        var recorded = new List<(long Value, string Institute, string Rung)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, ml) =>
        {
            if (instrument.Meter.Name == "Cena.Actors.Hints"
                && instrument.Name == "cena_hint_ld_governor_engaged_total")
            {
                ml.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
        {
            string institute = "";
            string rung = "";
            foreach (var t in tags)
            {
                if (t.Key == "institute_id") institute = t.Value?.ToString() ?? "";
                else if (t.Key == "rung") rung = t.Value?.ToString() ?? "";
            }
            recorded.Add((value, institute, rung));
        });
        listener.Start();

        governor.Apply(Original(1), Request(1), profile, "inst-alpha");

        Assert.Single(recorded);
        Assert.Equal(1L, recorded[0].Value);
        Assert.Equal("inst-alpha", recorded[0].Institute);
        Assert.Equal("L1", recorded[0].Rung);
    }

    [Fact]
    public void LdAnxious_L1_rewrite_with_unknown_institute_tags_with_unknown()
    {
        var governor = new LdAnxiousHintGovernor();
        var profile = ProfileWith(AccommodationDimension.LdAnxiousFriendly);

        string? observedInstitute = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, ml) =>
        {
            if (instrument.Meter.Name == "Cena.Actors.Hints"
                && instrument.Name == "cena_hint_ld_governor_engaged_total")
                ml.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
        {
            foreach (var t in tags)
                if (t.Key == "institute_id") observedInstitute = t.Value?.ToString();
        });
        listener.Start();

        governor.Apply(Original(1), Request(1), profile, instituteId: string.Empty);

        Assert.Equal("unknown", observedInstitute);
    }

    [Fact]
    public void No_accommodation_profile_does_not_emit_engagement_counter()
    {
        var governor = new LdAnxiousHintGovernor();
        var profile = AccommodationProfile.Default("stu-anon-default");

        var recorded = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, ml) =>
        {
            if (instrument.Meter.Name == "Cena.Actors.Hints"
                && instrument.Name == "cena_hint_ld_governor_engaged_total")
                ml.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) =>
            recorded.Add(value));
        listener.Start();

        governor.Apply(Original(1), Request(1), profile, "inst-a");
        governor.Apply(Original(2), Request(2), profile, "inst-a");
        governor.Apply(Original(3), Request(3), profile, "inst-a");

        Assert.Empty(recorded);
    }

    // ─── Ship-gate cross-check — rewrite body never uses banned copy ────

    [Fact]
    public void Rewritten_L1_body_has_no_loss_aversion_or_streak_language()
    {
        var governor = new LdAnxiousHintGovernor();
        var profile = ProfileWith(AccommodationDimension.LdAnxiousFriendly);

        var rewritten = governor.Apply(Original(1), Request(1), profile, "inst-a");

        // Cross-checked against scripts/shipgate/banned-mechanics.yml patterns
        // and scripts/shipgate/scan.mjs English banned list (streak, last
        // chance, don't break, countdown, lose your, crisis mode, …).
        var banned = new[]
        {
            "streak", "last chance", "don't break", "countdown", "lose your",
            "crisis", "don't give up", "urgent", "hurry", "running out"
        };
        foreach (var term in banned)
            Assert.DoesNotContain(term, rewritten.Text, StringComparison.OrdinalIgnoreCase);
    }
}
