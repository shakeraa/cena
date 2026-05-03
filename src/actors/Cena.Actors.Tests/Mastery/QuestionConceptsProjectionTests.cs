// =============================================================================
// Cena Platform — QuestionList projection + QuestionState event-sourcing tests
// for ADR-0062 Phase 1 concept events. Replays the actual event order through
// each projector method per memory rule `feedback_event_sourcing_replay_check`
// — happy-path test order is NOT enough; the architect-review FAIL flagged
// that the events.cs header claimed AggregateStreamAsync would rebuild
// concepts and the code didn't.
//
// Locks:
//   1. Extracted-then-empty-extracted does NOT blank an existing set.
//   2. Confirm-after-extract overwrites with the curator's set.
//   3. Confirm-with-empty-list (curator says "no concepts apply") DOES blank.
//   4. Late-arriving extracted overwrites a previous confirm (Phase 1 last-write-wins;
//      Phase 2 will add a freeze flag if telemetry justifies).
//   5. QuestionState and QuestionListProjection stay in sync — same event ordering
//      produces the same final concept list on both sides.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Mastery;
using Cena.Actors.Questions;

namespace Cena.Actors.Tests.Mastery;

public sealed class QuestionConceptsProjectionTests
{
    private static QuestionConcept Concept(string skill, ConceptRole role, string tier = "rules")
        => new(SkillCode.Parse(skill), role, 0.6, "", tier);

    private static QuestionConceptsExtracted_V1 Extracted(params QuestionConcept[] concepts)
        => new(
            QuestionId: "q-1",
            Concepts: concepts,
            ExtractionStrategy: "rules_v1",
            ExtractedBy: "test",
            Timestamp: DateTimeOffset.UtcNow);

    private static QuestionConceptsConfirmed_V1 Confirmed(CuratorAction action, params QuestionConcept[] concepts)
        => new(
            QuestionId: "q-1",
            Concepts: concepts,
            Action: action,
            ConfirmedBy: "curator-test",
            Timestamp: DateTimeOffset.UtcNow);

    // ---- Projection: QuestionListProjection.Apply ----

    [Fact]
    public void Projection_Extracted_PopulatesConceptsAndNames()
    {
        var p = new QuestionListProjection();
        var model = new QuestionReadModel { Id = "q-1" };

        p.Apply(Extracted(Concept("math.calculus.derivative-rules", ConceptRole.Primary)), model);

        Assert.Equal(new[] { "math.calculus.derivative-rules" }, model.Concepts);
        Assert.Single(model.ConceptNames);
        Assert.NotNull(model.UpdatedAt);
    }

    [Fact]
    public void Projection_EmptyExtracted_DoesNotBlankExistingSet()
    {
        var p = new QuestionListProjection();
        var model = new QuestionReadModel
        {
            Id = "q-1",
            Concepts = new List<string> { "math.calculus.derivative-rules" }
        };

        p.Apply(Extracted(/* no concepts */), model);

        Assert.Equal(new[] { "math.calculus.derivative-rules" }, model.Concepts);
    }

    [Fact]
    public void Projection_ConfirmedOverwritesExtracted()
    {
        var p = new QuestionListProjection();
        var model = new QuestionReadModel { Id = "q-1" };

        p.Apply(Extracted(Concept("math.calculus.derivative-rules", ConceptRole.Primary)), model);
        p.Apply(Confirmed(CuratorAction.PrimaryEdited,
            Concept("math.calculus.applications-of-derivatives", ConceptRole.Primary)), model);

        Assert.Equal(new[] { "math.calculus.applications-of-derivatives" }, model.Concepts);
    }

    [Fact]
    public void Projection_ConfirmedWithEmpty_DoesBlank()
    {
        // Curator override is authoritative — empty means "no concepts apply".
        var p = new QuestionListProjection();
        var model = new QuestionReadModel
        {
            Id = "q-1",
            Concepts = new List<string> { "math.calculus.derivative-rules" }
        };

        p.Apply(Confirmed(CuratorAction.FullyOverridden /* no concepts */), model);

        Assert.Empty(model.Concepts);
    }

    [Fact]
    public void Projection_LateExtracted_OverwritesConfirm_LWW()
    {
        // Phase 1 LWW: a fresh extraction after a confirm wins. Phase 2 will
        // add a freeze flag if the curator UI demands it.
        var p = new QuestionListProjection();
        var model = new QuestionReadModel { Id = "q-1" };

        p.Apply(Confirmed(CuratorAction.AcceptedAsExtracted,
            Concept("math.calculus.applications-of-derivatives", ConceptRole.Primary)), model);
        p.Apply(Extracted(
            Concept("math.calculus.derivative-rules", ConceptRole.Primary),
            Concept("math.functions.quadratic-functions", ConceptRole.Supporting)), model);

        Assert.Equal(2, model.Concepts.Count);
        Assert.Contains("math.calculus.derivative-rules", model.Concepts);
    }

    // ---- Aggregate: QuestionState.Apply (event-sourced rebuild) ----

    [Fact]
    public void State_RebuildsConceptIdsFromEventStream_InOrder()
    {
        var state = new QuestionState();

        state.Apply(Extracted(Concept("math.algebra.quadratic-equations", ConceptRole.Primary)));
        state.Apply(Confirmed(CuratorAction.SupportingEdited,
            Concept("math.algebra.quadratic-equations", ConceptRole.Primary),
            Concept("math.functions.function-basics", ConceptRole.Supporting)));

        // After a Confirmed, both should land on QuestionState.ConceptIds in
        // the order the curator submitted them (primary first).
        Assert.Equal(2, state.ConceptIds.Count);
        Assert.Equal("math.algebra.quadratic-equations", state.ConceptIds[0]);
        Assert.Equal("math.functions.function-basics", state.ConceptIds[1]);
    }

    [Fact]
    public void State_EmptyExtracted_DoesNotBlankConfirm()
    {
        var state = new QuestionState();
        state.Apply(Confirmed(CuratorAction.AcceptedAsExtracted,
            Concept("math.calculus.derivative-rules", ConceptRole.Primary)));
        var v0 = state.EventVersion;

        // A re-extraction with no rules-tier hit must NOT erase the curator's
        // prior confirm — that would be the silent-data-loss anti-pattern.
        state.Apply(Extracted(/* no concepts */));

        Assert.Equal(new[] { "math.calculus.derivative-rules" }, state.ConceptIds);
        // EventVersion still bumps (the event was real, the projection ran)
        Assert.Equal(v0 + 1, state.EventVersion);
    }

    [Fact]
    public void Projection_And_State_StayConsistent_AcrossSameStream()
    {
        // The architect-review FAIL flagged that the projection contract had
        // to spell out Confirmed-vs-Extracted ordering. This test pins both
        // projector implementations against the same stream and asserts they
        // produce the same final concept list — if they diverge in future,
        // this test fails loudly so future-self knows.
        var p = new QuestionListProjection();
        var model = new QuestionReadModel { Id = "q-1" };
        var state = new QuestionState();

        var stream = new object[]
        {
            Extracted(Concept("math.calculus.derivative-rules", ConceptRole.Primary)),
            Confirmed(CuratorAction.SupportingEdited,
                Concept("math.calculus.derivative-rules", ConceptRole.Primary),
                Concept("math.functions.quadratic-functions", ConceptRole.Supporting)),
        };

        foreach (var ev in stream)
        {
            switch (ev)
            {
                case QuestionConceptsExtracted_V1 e: p.Apply(e, model); state.Apply(e); break;
                case QuestionConceptsConfirmed_V1 c: p.Apply(c, model); state.Apply(c); break;
            }
        }

        Assert.Equal(model.Concepts, state.ConceptIds);
    }
}
