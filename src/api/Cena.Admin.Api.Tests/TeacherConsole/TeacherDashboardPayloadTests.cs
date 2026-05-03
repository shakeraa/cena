// =============================================================================
// Cena Platform — Teacher Dashboard payload shape tests (prr-049)
//
// Proves the structural invariants of TeacherDashboardResponse:
//
//   1. The DTO MUST NOT expose any field whose name matches the banned
//      vanity-metric glossary (streak, totalMinutes, minutesWatched,
//      totalQuestions, etc.). This is the ship-gate GD-004 extension to
//      the teacher-facing surface.
//
//   2. The DTO MUST expose the three actionable pillars (struggling
//      topics, hint-ladder usage, intervention recommendations).
//
//   3. Constants sit in the Bjork/educator ranges stated in the endpoint
//      preamble (MinClassSize ≥ 3, StrugglingTopicThreshold = 0.60,
//      InterventionIncorrectStreak = 3).
// =============================================================================

using System.Reflection;
using System.Text.RegularExpressions;
using Cena.Admin.Api.Features.TeacherConsole;
using Cena.Actors.Mastery;

namespace Cena.Admin.Api.Tests.TeacherConsole;

public class TeacherDashboardPayloadTests
{
    private static readonly Regex BannedVanityField = new(
        @"(?ix)
          \b(
              streak
            | totalminutes
            | minutesspent
            | minuteswatched
            | minuteslogged
            | totalquestions
            | questionsanswered
            | xptotal
            | xpearned
            | badgescount
            | badgecount
          )\b",
        RegexOptions.Compiled);

    [Fact]
    public void TeacherDashboardResponse_has_no_banned_vanity_fields()
    {
        var props = typeof(TeacherDashboardResponse).GetProperties(
            BindingFlags.Public | BindingFlags.Instance);
        var banned = props
            .Where(p => BannedVanityField.IsMatch(p.Name))
            .Select(p => p.Name)
            .ToList();

        Assert.True(banned.Count == 0,
            "prr-049 + ship-gate GD-004: TeacherDashboardResponse exposes "
            + $"banned vanity field(s): {string.Join(", ", banned)}. "
            + "These metrics correlate weakly with learning and (per "
            + "Deci & Ryan 2000 + Strathern's Goodhart's-law formulation) "
            + "cause teachers to coach for engagement proxies rather than "
            + "mastery. Move the signal to the three actionable pillars: "
            + "StrugglingTopics, HintLadderUsageByStudent, "
            + "InterventionsRecommended.");
    }

    [Fact]
    public void TeacherDashboardResponse_exposes_the_three_actionable_pillars()
    {
        var props = typeof(TeacherDashboardResponse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(TeacherDashboardResponse.StrugglingTopics), props);
        Assert.Contains(nameof(TeacherDashboardResponse.HintLadderUsageByStudent), props);
        Assert.Contains(nameof(TeacherDashboardResponse.InterventionsRecommended), props);
    }

    [Fact]
    public void Anonymity_floor_is_at_least_three()
    {
        // A class of 2 is, in practice, a 1:1 tutoring row — aggregates
        // over 2 students leak individual mastery signal. Floor at 3.
        Assert.True(
            TeacherDashboardEndpoint.MinClassSizeForAggregates >= 3,
            "prr-049 anonymity floor dropped below 3. This ships per-student "
            + "data labelled 'class-wide'.");
    }

    [Fact]
    public void Struggling_topic_threshold_matches_Bjork_floor()
    {
        Assert.Equal(
            DifficultyTarget.BjorkMinTarget,
            TeacherDashboardEndpoint.StrugglingTopicThreshold);
    }

    [Fact]
    public void Intervention_streak_is_at_least_three_consecutive_incorrect()
    {
        Assert.True(TeacherDashboardEndpoint.InterventionIncorrectStreak >= 3,
            "prr-049: an intervention after two wrong answers is too noisy; "
            + "the persona-educator review calibrated the trigger at three.");
    }

    [Fact]
    public void Rollup_window_is_one_week()
    {
        Assert.Equal(TimeSpan.FromDays(7), TeacherDashboardEndpoint.RollupWindow);
    }

    [Fact]
    public void StrugglingTopicDto_reports_mastery_deficit()
    {
        var dto = new StrugglingTopicDto(
            TopicSlug: "algebra.linear-equations",
            MeanMastery: 0.42,
            StudentCount: 12,
            MasteryDeficit: 0.18);

        Assert.Equal(0.18, dto.MasteryDeficit, precision: 6);
        Assert.Equal(12, dto.StudentCount);
    }

    [Fact]
    public void HintLadderUsageDto_carries_only_L2_and_L3()
    {
        // L1 hints (normal scaffolded nudges) are not diagnostic — a
        // student asking for one is not in trouble. We expose L2 + L3
        // only. Regression lock: if someone adds L1Hints, this test
        // fails and the reviewer reconsiders.
        var props = typeof(HintLadderUsageDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(HintLadderUsageDto.L2HintsLast7Days), props);
        Assert.Contains(nameof(HintLadderUsageDto.L3HintsLast7Days), props);
        Assert.DoesNotContain("L1HintsLast7Days", props);
    }

    [Fact]
    public void InterventionRecommendationDto_does_not_carry_misconception_tag()
    {
        // ADR-0003 says misconception data is session-scoped. The
        // teacher dashboard payload MUST NOT export a misconception
        // tag — the intervention signal is attempt-outcome history only.
        var props = typeof(InterventionRecommendationDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var p in props)
        {
            Assert.False(
                p.Contains("misconception", StringComparison.OrdinalIgnoreCase),
                $"ADR-0003 violation: InterventionRecommendationDto exposes "
                + $"'{p}' which carries misconception-scoped data outside the "
                + "session aggregate.");
        }
    }
}
