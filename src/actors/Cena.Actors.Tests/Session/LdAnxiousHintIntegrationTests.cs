// =============================================================================
// prr-029 — LD-Anxious hint integration tests
//
// Composes the real HintGenerator (IHintGenerator) and the real
// LdAnxiousHintGovernor (ILdAnxiousHintGovernor) in the exact shape the
// REST hint endpoint (POST /api/sessions/{id}/question/{qid}/hint) wires
// them. Asserts that:
//
//   * A student with the LdAnxiousFriendly profile flag receives a
//     worked-example L1 body (carries LdAnxiousHintGovernor.WorkedExampleMarker).
//   * The same request for a student WITHOUT the flag gets the unchanged
//     terse nudge — the governor is strictly additive.
//   * L2 / L3 bodies are identical for both students.
//
// The endpoint itself is exercised end-to-end by the Actor-side hint
// delivery tests; this suite pins the pedagogical seam that would
// silently drift if a future refactor dropped the governor call from
// the endpoint composition.
// =============================================================================

using Cena.Actors.Accommodations;
using Cena.Actors.Hints;
using Cena.Actors.Mastery;
using Cena.Actors.Questions;
using Cena.Actors.Services;
using Cena.Api.Host.Endpoints;
using Cena.Infrastructure.Documents;
using Xunit;

namespace Cena.Actors.Tests.Session;

public sealed class LdAnxiousHintIntegrationTests
{
    private const string ConceptId = "concept:physics:ohms-law";
    private const string QuestionId = "q_ohm_101";

    private static QuestionDocument BuildQuestion() =>
        new()
        {
            QuestionId = QuestionId,
            ConceptId = ConceptId,
            Subject = "Physics",
            Prompt = "A 12V battery drives 2A through a resistor. What is R?",
            QuestionType = "multiple-choice",
            Choices = new[] { "3 ohms", "6 ohms", "12 ohms", "24 ohms" },
            CorrectAnswer = "6 ohms",
            Explanation = "Ohm's law: R = V / I = 12 / 2 = 6 ohms.",
            DistractorRationales = new Dictionary<string, string>
            {
                ["3 ohms"] = "That would give 4A by Ohm's law, not 2A.",
            },
        };

    private static AccommodationProfile ProfileWith(params AccommodationDimension[] enabled) =>
        new(
            StudentAnonId: "stu-anon-ld-integration",
            EnabledDimensions: new HashSet<AccommodationDimension>(enabled),
            Assigner: AccommodationAssigner.Parent,
            AssignerSignature: "parent-hmac-integration",
            AssignedAtUtc: DateTimeOffset.UtcNow);

    /// <summary>
    /// Compose HintGenerator + LdAnxiousHintGovernor exactly the way the
    /// student API endpoint does. Returns the post-governor content so
    /// assertions run against the body the student actually receives.
    /// </summary>
    private static HintContent HintPipeline(
        int level,
        AccommodationProfile profile,
        IReadOnlyList<string> prereqNames)
    {
        var q = BuildQuestion();
        var req = new HintRequest(
            HintLevel: level,
            QuestionId: QuestionId,
            ConceptId: ConceptId,
            PrerequisiteConceptNames: prereqNames,
            Options: SessionEndpoints.BuildHintOptionStates(q),
            Explanation: q.Explanation,
            StudentAnswer: null);

        var gen = new HintGenerator();
        var governor = new LdAnxiousHintGovernor();

        var base_ = gen.Generate(req);
        return governor.Apply(base_, req, profile, "inst-pilot-1");
    }

    // ─── Integration — L1 rewrite for LD-anxious students ───────────────

    [Fact]
    public void LdAnxious_student_receives_worked_example_marker_on_L1()
    {
        var profile = ProfileWith(AccommodationDimension.LdAnxiousFriendly);
        var prereqs = new[] { "Ohm's law" };

        var content = HintPipeline(1, profile, prereqs);

        Assert.Contains(LdAnxiousHintGovernor.WorkedExampleMarker, content.Text);
        Assert.Contains("Ohm's law", content.Text);
    }

    [Fact]
    public void NonAnxious_student_receives_unchanged_L1_nudge()
    {
        var profile = AccommodationProfile.Default("stu-anon-control");
        var prereqs = new[] { "Ohm's law" };

        var content = HintPipeline(1, profile, prereqs);

        Assert.DoesNotContain(LdAnxiousHintGovernor.WorkedExampleMarker, content.Text);
        // Baseline L1 nudge copy from HintGenerator.GenerateNudge(...)
        Assert.Contains("Ohm's law", content.Text);
        Assert.Contains("Consider", content.Text);
    }

    // ─── L2 / L3 are identical regardless of accommodation profile ──────

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void LdAnxious_and_NonAnxious_students_see_identical_L2_and_L3(int level)
    {
        var anxious = ProfileWith(AccommodationDimension.LdAnxiousFriendly);
        var control = AccommodationProfile.Default("stu-anon-control");
        var prereqs = new[] { "Ohm's law" };

        var anxiousContent = HintPipeline(level, anxious, prereqs);
        var controlContent = HintPipeline(level, control, prereqs);

        Assert.Equal(controlContent.Text, anxiousContent.Text);
        Assert.Equal(controlContent.HasMoreHints, anxiousContent.HasMoreHints);
    }

    // ─── Pedagogy assertion — L1 body for anxious student is concrete ──

    [Fact]
    public void LdAnxious_L1_body_contains_imperative_verb_and_prereq_name()
    {
        // WHY this test exists:
        //   Renkl & Atkinson 2003 define the worked-example first rung as
        //   "name the operation and the object it acts on." If the governor
        //   regresses to a vague "think about …" phrasing, the pedagogical
        //   guarantee collapses to the same abstract nudge the baseline
        //   already ships. This test pins the imperative shape.
        var profile = ProfileWith(AccommodationDimension.LdAnxiousFriendly);
        var prereqs = new[] { "Ohm's law" };

        var content = HintPipeline(1, profile, prereqs);

        // "Start by …" or equivalent imperative is required by the
        // governor's contract.
        Assert.Contains("Start", content.Text);
        Assert.Contains("Ohm's law", content.Text);
    }
}
