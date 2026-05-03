// =============================================================================
// Cena Platform — Publish calibration-gate decision tests (ADR-0062 Phase 1)
// Pins the decision rules in PublishCalibrationGate.RequiresConfirm +
// DeriveTrackHint. End-to-end Marten + endpoint coverage stays at the
// integration layer; this file pins just the predicates.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Mastery;
using Cena.Actors.Questions;
using Cena.Admin.Api.Concepts;
using Xunit;

namespace Cena.Admin.Api.Tests;

public sealed class QuestionBankPublishCalibrationGateTests
{
    private static QuestionConcept Concept(string skill, ConceptRole role) =>
        new(SkillCode.Parse(skill), role, 0.6, "", "rules");

    private static QuestionConceptsExtracted_V1 Extracted(params QuestionConcept[] concepts) =>
        new("q-1", concepts, "rules_v1", "test", DateTimeOffset.UtcNow);

    private static QuestionConceptsConfirmed_V1 Confirmed(params QuestionConcept[] concepts) =>
        new("q-1", concepts, CuratorAction.AcceptedAsExtracted, "curator", DateTimeOffset.UtcNow);

    [Fact]
    public void Legacy_NoExtractionEvent_GateSkips()
    {
        // Items predating the persister wiring have no extraction event;
        // gate must NOT fire — they were validated by the prior pipeline.
        var state = new QuestionState();
        Assert.False(PublishCalibrationGate.RequiresConfirm(state));
    }

    [Fact]
    public void Extracted_WithConcepts_NotConfirmed_GateFires()
    {
        var state = new QuestionState();
        state.Apply(Extracted(Concept("math.calculus.derivative-rules", ConceptRole.Primary)));
        Assert.True(PublishCalibrationGate.RequiresConfirm(state));
    }

    [Fact]
    public void Extracted_AlreadyConfirmed_GateSkips()
    {
        // Once the curator has confirmed, this question is out of the
        // calibration corpus — gate doesn't re-fire on re-extraction.
        var state = new QuestionState();
        state.Apply(Extracted(Concept("math.algebra.polynomials", ConceptRole.Primary)));
        state.Apply(Confirmed(Concept("math.algebra.polynomials", ConceptRole.Primary)));
        Assert.False(PublishCalibrationGate.RequiresConfirm(state));
    }

    [Fact]
    public void Extracted_RulesMissed_NoConcepts_GateSkips()
    {
        // Rules-tier produced nothing canonical: nothing for the curator
        // to confirm. Gate skips; curator picks via the concept-review
        // panel post-publish.
        var state = new QuestionState();
        state.Apply(Extracted(/* no concepts */));
        Assert.False(PublishCalibrationGate.RequiresConfirm(state));
    }

    [Fact]
    public void Confirmed_BeforeExtracted_GateSkips()
    {
        // Out-of-band confirm wins — flag is the authoritative signal
        // regardless of order.
        var state = new QuestionState();
        state.Apply(Confirmed(Concept("math.calculus.limits", ConceptRole.Primary)));
        Assert.False(PublishCalibrationGate.RequiresConfirm(state));
    }

    [Fact]
    public void DeriveTrackHint_RecognisesCanonicalForms()
    {
        Assert.Equal("math_5u", PublishCalibrationGate.DeriveTrackHint("math_5u"));
        Assert.Equal("math_4u", PublishCalibrationGate.DeriveTrackHint("math_4u"));
        Assert.Equal("math_3u", PublishCalibrationGate.DeriveTrackHint("math_3u"));
        Assert.Equal("math_5u", PublishCalibrationGate.DeriveTrackHint("5u"));
        Assert.Equal("math_5u", PublishCalibrationGate.DeriveTrackHint("5U"));
        Assert.Null(PublishCalibrationGate.DeriveTrackHint(null));
        Assert.Null(PublishCalibrationGate.DeriveTrackHint(""));
        Assert.Null(PublishCalibrationGate.DeriveTrackHint("12"));
        Assert.Null(PublishCalibrationGate.DeriveTrackHint("grade-12"));
    }
}
