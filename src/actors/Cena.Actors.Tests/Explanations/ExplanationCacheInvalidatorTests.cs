// =============================================================================
// Cena Platform — ExplanationCacheInvalidator Tests (FIND-arch-003)
// Regression coverage for NATS subject drift. The invalidator previously
// subscribed to "cena.events.QuestionStemEdited_V1" while NatsOutboxPublisher
// emits on "cena.durable.curriculum.QuestionStemEdited_V1", so L2 Redis was
// never invalidated on question edits. These tests pin both ends of the
// contract so a future change to either side breaks the build, not production.
// =============================================================================

using Cena.Actors.Bus;
using Cena.Actors.Events;
using Cena.Actors.Infrastructure;
using Cena.Actors.Tests.Bus;

namespace Cena.Actors.Tests.Explanations;

public sealed class ExplanationCacheInvalidatorTests
{
    // ── Subject contract: subscriber subjects match outbox publisher output ──

    [Fact]
    public void DurableCurriculumSubject_MatchesOutboxForQuestionStemEdited()
    {
        var outboxSubject = NatsOutboxPublisher.GetDurableSubject(nameof(QuestionStemEdited_V1));
        var subscriberSubject = NatsSubjects.DurableCurriculumEvent(nameof(QuestionStemEdited_V1));

        Assert.Equal("cena.durable.curriculum.QuestionStemEdited_V1", outboxSubject);
        Assert.Equal(outboxSubject, subscriberSubject);
    }

    [Fact]
    public void DurableCurriculumSubject_MatchesOutboxForQuestionOptionChanged()
    {
        var outboxSubject = NatsOutboxPublisher.GetDurableSubject(nameof(QuestionOptionChanged_V1));
        var subscriberSubject = NatsSubjects.DurableCurriculumEvent(nameof(QuestionOptionChanged_V1));

        Assert.Equal("cena.durable.curriculum.QuestionOptionChanged_V1", outboxSubject);
        Assert.Equal(outboxSubject, subscriberSubject);
    }

    [Fact]
    public void DurableCurriculumSubject_RejectsLegacyLiteralPattern()
    {
        // The invalidator used to subscribe on "cena.events.Question*" literally.
        // Those subjects must no longer be produced by the outbox for Question* events.
        var outboxSubject = NatsOutboxPublisher.GetDurableSubject(nameof(QuestionStemEdited_V1));
        Assert.NotEqual("cena.events.QuestionStemEdited_V1", outboxSubject);
        Assert.NotEqual("cena.events.QuestionOptionChanged_V1",
            NatsOutboxPublisher.GetDurableSubject(nameof(QuestionOptionChanged_V1)));
    }

    [Fact]
    public void AllDurableCurriculumEventsWildcard_MatchesQuestionEvents()
    {
        var wildcard = NatsSubjects.AllDurableCurriculumEvents;
        Assert.Equal("cena.durable.curriculum.>", wildcard);

        var stemSubject = NatsOutboxPublisher.GetDurableSubject(nameof(QuestionStemEdited_V1));
        var optionSubject = NatsOutboxPublisher.GetDurableSubject(nameof(QuestionOptionChanged_V1));

        Assert.True(NatsSubjectMatcher.IsMatch(wildcard, stemSubject),
            $"wildcard '{wildcard}' must match '{stemSubject}'");
        Assert.True(NatsSubjectMatcher.IsMatch(wildcard, optionSubject),
            $"wildcard '{wildcard}' must match '{optionSubject}'");
    }

    [Fact]
    public void OutboxCategorisesPipelineAndFileEventsAsCurriculum()
    {
        // FIND-arch-003 found that the outbox routes Pipeline* and File* to
        // cena.durable.curriculum.* alongside Question*. Pin this so a future
        // refactor does not silently split the stream.
        Assert.StartsWith("cena.durable.curriculum.",
            NatsOutboxPublisher.GetDurableSubject("PipelineRunStarted_V1"));
        Assert.StartsWith("cena.durable.curriculum.",
            NatsOutboxPublisher.GetDurableSubject("FileUploaded_V1"));
    }

    [Fact]
    public void OutboxRoutesNonCurriculumEventsElsewhere()
    {
        // Sanity: the invalidator subscribes to curriculum only; other bounded
        // contexts must land on their own stream so the invalidator does not
        // receive irrelevant traffic.
        Assert.StartsWith("cena.durable.engagement.",
            NatsOutboxPublisher.GetDurableSubject("XpAwarded_V1"));
        Assert.StartsWith("cena.durable.learner.",
            NatsOutboxPublisher.GetDurableSubject("ConceptMastered_V1"));
        Assert.StartsWith("cena.durable.pedagogy.",
            NatsOutboxPublisher.GetDurableSubject("SessionStarted_V1"));
    }
}
