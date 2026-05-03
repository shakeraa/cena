// =============================================================================
// Cena Platform — NatsOutboxPublisher.GetDurableSubject routing tests
//
// Regression cover for the 2026-04-23 bug where every snake_case event name
// (session_started__v1, xp_awarded__v1, focus_score_updated__v1, …) fell
// through to the default "cena.durable.system.*" branch because the switch
// did case-sensitive StartsWith against PascalCase prefixes. Only the
// running emulator caught it — SYSTEM_HEALTH accumulated 1,700 events that
// belonged in PEDAGOGY_EVENTS / LEARNER_EVENTS / ENGAGEMENT_EVENTS.
//
// These tests pin both the PascalCase form (typed projections) and the
// snake_case form (DocType.UnderscoreCaseTypeAlias) onto the correct
// per-bounded-context stream. Add a case to both blocks whenever a new
// event-type prefix is added.
// =============================================================================

using Cena.Actors.Infrastructure;
using Xunit;

namespace Cena.Actors.Tests.Infrastructure;

public class NatsOutboxSubjectRoutingTests
{
    // ---- PascalCase (typed projection) form --------------------------------

    [Theory]
    [InlineData("SessionStarted",          "cena.durable.pedagogy.SessionStarted")]
    [InlineData("ExerciseCompleted",       "cena.durable.pedagogy.ExerciseCompleted")]
    [InlineData("HintRequested",           "cena.durable.pedagogy.HintRequested")]
    [InlineData("ConceptAttempted",        "cena.durable.learner.ConceptAttempted")]
    [InlineData("MasteryUpdated",          "cena.durable.learner.MasteryUpdated")]
    [InlineData("CognitiveLoadSampled",    "cena.durable.learner.CognitiveLoadSampled")]
    [InlineData("FocusScoreUpdated",       "cena.durable.learner.FocusScoreUpdated")]
    [InlineData("MindWanderingFlagged",    "cena.durable.learner.MindWanderingFlagged")]
    [InlineData("XpAwarded",               "cena.durable.engagement.XpAwarded")]
    [InlineData("StreakReset",             "cena.durable.engagement.StreakReset")]
    [InlineData("BadgeEarned",             "cena.durable.engagement.BadgeEarned")]
    [InlineData("ReviewQueued",            "cena.durable.engagement.ReviewQueued")]
    [InlineData("OutreachDispatched",      "cena.durable.outreach.OutreachDispatched")]
    [InlineData("TutoringInteractionLogged", "cena.durable.pedagogy.TutoringInteractionLogged")]
    public void PascalCase_event_names_route_to_correct_bounded_context(
        string eventTypeName, string expectedSubject)
    {
        Assert.Equal(expectedSubject, NatsOutboxPublisher.GetDurableSubject(eventTypeName));
    }

    // ---- snake_case (UnderscoreCaseTypeAlias) form -------------------------

    [Theory]
    [InlineData("session_started__v1",          "cena.durable.pedagogy.session_started__v1")]
    [InlineData("exercise_completed__v1",       "cena.durable.pedagogy.exercise_completed__v1")]
    [InlineData("hint_requested__v1",           "cena.durable.pedagogy.hint_requested__v1")]
    [InlineData("concept_attempted__v1",        "cena.durable.learner.concept_attempted__v1")]
    [InlineData("mastery_updated__v1",          "cena.durable.learner.mastery_updated__v1")]
    [InlineData("cognitive_load_sampled__v1",   "cena.durable.learner.cognitive_load_sampled__v1")]
    [InlineData("focus_score_updated__v1",      "cena.durable.learner.focus_score_updated__v1")]
    [InlineData("mind_wandering_flagged__v1",   "cena.durable.learner.mind_wandering_flagged__v1")]
    [InlineData("xp_awarded__v1",               "cena.durable.engagement.xp_awarded__v1")]
    [InlineData("streak_reset__v1",             "cena.durable.engagement.streak_reset__v1")]
    [InlineData("badge_earned__v1",             "cena.durable.engagement.badge_earned__v1")]
    [InlineData("review_queued__v1",            "cena.durable.engagement.review_queued__v1")]
    [InlineData("outreach_dispatched__v1",      "cena.durable.outreach.outreach_dispatched__v1")]
    [InlineData("tutoring_interaction_logged__v1", "cena.durable.pedagogy.tutoring_interaction_logged__v1")]
    public void snake_case_event_names_route_to_correct_bounded_context(
        string eventTypeName, string expectedSubject)
    {
        Assert.Equal(expectedSubject, NatsOutboxPublisher.GetDurableSubject(eventTypeName));
    }

    // ---- Question branch — split between pedagogy (skipped) and curriculum
    // (everything else) per the original switch's intent --------------------

    [Fact]
    public void Question_with_Skipped_routes_to_pedagogy_PascalCase()
    {
        Assert.Equal(
            "cena.durable.pedagogy.QuestionSkipped",
            NatsOutboxPublisher.GetDurableSubject("QuestionSkipped"));
    }

    [Fact]
    public void Question_with_skipped_routes_to_pedagogy_snake_case()
    {
        Assert.Equal(
            "cena.durable.pedagogy.question_skipped__v1",
            NatsOutboxPublisher.GetDurableSubject("question_skipped__v1"));
    }

    [Fact]
    public void Question_without_skipped_routes_to_curriculum_PascalCase()
    {
        Assert.StartsWith(
            "cena.durable.curriculum.",
            NatsOutboxPublisher.GetDurableSubject("QuestionPublished"));
    }

    [Fact]
    public void Question_without_skipped_routes_to_curriculum_snake_case()
    {
        Assert.StartsWith(
            "cena.durable.curriculum.",
            NatsOutboxPublisher.GetDurableSubject("question_published__v1"));
    }

    // ---- Unknown / fall-through to system ----------------------------------

    [Fact]
    public void Unknown_prefix_routes_to_system_default_PascalCase()
    {
        Assert.Equal(
            "cena.durable.system.UnknownTopicThatMatchesNoPrefix",
            NatsOutboxPublisher.GetDurableSubject("UnknownTopicThatMatchesNoPrefix"));
    }

    [Fact]
    public void Unknown_prefix_routes_to_system_default_snake_case()
    {
        Assert.Equal(
            "cena.durable.system.unknown_topic_that_matches_no_prefix__v1",
            NatsOutboxPublisher.GetDurableSubject("unknown_topic_that_matches_no_prefix__v1"));
    }

    // ---- The explicit regression invariant ---------------------------------

    [Theory]
    [InlineData("session_started__v1")]
    [InlineData("concept_attempted__v1")]
    [InlineData("xp_awarded__v1")]
    [InlineData("focus_score_updated__v1")]
    public void snake_case_events_do_not_fall_through_to_system_default(string eventTypeName)
    {
        // Before the 2026-04-23 fix every one of these names fell to
        // "cena.durable.system.*" because StartsWith was case-sensitive.
        var subject = NatsOutboxPublisher.GetDurableSubject(eventTypeName);
        Assert.DoesNotContain("cena.durable.system.", subject);
    }
}
