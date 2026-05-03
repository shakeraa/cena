// =============================================================================
// Cena Platform — CasGatedQuestionPersister concept-extraction wiring (ADR-0062 Phase 1)
//
// The persister is the single writer of new QuestionState streams (RDY-037).
// ADR-0062 Phase 1 piggybacks on that single-writer guarantee: every creation
// path emits QuestionConceptsExtracted_V1 immediately after the creation
// event. These tests pin the contract so a future refactor can't quietly
// drop the extraction event.
//
// What we lock:
//   1. Extraction event lands on the stream when an extractor is bound.
//   2. Extraction event is positioned after the creation event (so
//      QuestionState.Apply<creation> bootstraps the aggregate before the
//      Apply<extracted> overwrites ConceptIds).
//   3. Stream is StartStream'd with all events in one atomic batch — no
//      partial-commit risk between creation event and extraction event.
//   4. Optional extractor: when null, no extraction event appended (back-
//      compat with hosts that haven't bound the extractor yet).
//   5. The context's TrackHint / RuleTierHint flow into the ExtractionInput
//      verbatim (so the rules-tier can canonicalise correctly).
// =============================================================================

using Cena.Actors.Cas;
using Cena.Actors.Events;
using Cena.Actors.Mastery;
using Cena.Actors.Mastery.Extraction;
using Cena.Actors.Questions;
using Cena.Infrastructure.Documents;
using Marten;
using Marten.Events;
using Marten.Events.Operations; // for IEventStoreOperations on a session
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Cena.Actors.Tests.Cas;

public sealed class CasGatedPersisterConceptExtractionTests
{
    private readonly ICasVerificationGate _gate = Substitute.For<ICasVerificationGate>();
    private readonly ICasGateModeProvider _mode = Substitute.For<ICasGateModeProvider>();
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IDocumentSession _session = Substitute.For<IDocumentSession>();
    private readonly IEventStoreOperations _events = Substitute.For<IEventStoreOperations>();

    public CasGatedPersisterConceptExtractionTests()
    {
        _store.LightweightSession().Returns(_session);
        _session.Events.Returns(_events);
        _mode.CurrentMode.Returns(CasGateMode.Off); // bypass gate for these tests; we're pinning extraction wiring
    }

    private static CasGatedQuestionPersister Sut(
        ICasVerificationGate gate, ICasGateModeProvider mode,
        IDocumentStore store, IQuestionConceptExtractor? extractor) =>
        new(gate, mode, store,
            NullLogger<CasGatedQuestionPersister>.Instance,
            extractor);

    [Fact]
    public async Task Persister_AppendsExtractedEvent_WhenExtractorBound()
    {
        // Stub extractor returns one Primary concept regardless of input.
        var extractor = Substitute.For<IQuestionConceptExtractor>();
        var skill = SkillCode.Parse("math.calculus.derivative-rules");
        var concept = new QuestionConcept(skill, ConceptRole.Primary, 0.7, "rules", "rules");
        extractor.ExtractAsync(Arg.Any<ExtractionInput>(), Arg.Any<CancellationToken>())
                 .Returns(new ExtractionResult(new[] { concept }, "rules_v1"));

        await Sut(_gate, _mode, _store, extractor).PersistAsync(
            questionId: "q-1",
            creationEvent: new TestEvent("q-1"),
            context: new GatedPersistContext(
                Subject:            "math",
                Stem:               "Find the derivative of f(x) = x^2.",
                CorrectAnswerRaw:   "2x",
                Language:           "en",
                Variable:           null,
                Latex:              null,
                TrackHint:          "math_5u",
                RuleTierHint:       "calculus.derivative_rules",
                RuleTierConfidence: 0.65));

        // StartStream<QuestionState>(id, events[]) must have been called
        // exactly once with creation event first and extraction event
        // second on the SAME stream — proves atomic single-batch append.
        _events.Received(1).StartStream<QuestionState>(
            "q-1",
            Arg.Is<object[]>(arr =>
                arr.Length == 2
                && IsTestEvent(arr[0])
                && IsExtractedEvent(arr[1])));
    }

    [Fact]
    public async Task Persister_FlowsContextHintsToExtractor()
    {
        var extractor = Substitute.For<IQuestionConceptExtractor>();
        extractor.ExtractAsync(Arg.Any<ExtractionInput>(), Arg.Any<CancellationToken>())
                 .Returns(new ExtractionResult(Array.Empty<QuestionConcept>(), "rules_v1"));

        await Sut(_gate, _mode, _store, extractor).PersistAsync(
            "q-1",
            new TestEvent("q-1"),
            new GatedPersistContext(
                Subject:            "math",
                Stem:               "stem text",
                CorrectAnswerRaw:   "ans",
                Language:           "he",
                Variable:           null,
                Latex:              "x^2",
                TrackHint:          "math_4u",
                RuleTierHint:       "algebra.polynomials",
                RuleTierConfidence: 0.55));

        await extractor.Received(1).ExtractAsync(
            Arg.Is<ExtractionInput>(i =>
                i.QuestionId         == "q-1"
                && i.Prompt          == "stem text"
                && i.Latex           == "x^2"
                && i.TrackHint       == "math_4u"
                && i.RuleTierHint    == "algebra.polynomials"
                && i.RuleTierConfidence == 0.55),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Persister_AppendsEvenWhenExtractorReturnsEmpty()
    {
        // Per ADR-0062 + QuestionState.Apply: an empty extraction is still
        // a real pipeline pass; we mark HasConceptExtractionEvent=true so
        // the publish gate can distinguish "ran through pipeline" from
        // "legacy item". The event MUST be emitted even on a rules-miss.
        var extractor = Substitute.For<IQuestionConceptExtractor>();
        extractor.ExtractAsync(Arg.Any<ExtractionInput>(), Arg.Any<CancellationToken>())
                 .Returns(new ExtractionResult(Array.Empty<QuestionConcept>(), "rules_v1"));

        await Sut(_gate, _mode, _store, extractor).PersistAsync(
            "q-1",
            new TestEvent("q-1"),
            new GatedPersistContext("math", "stem", "ans", "en"));

        _events.Received(1).StartStream<QuestionState>(
            "q-1",
            Arg.Is<object[]>(arr =>
                arr.Length == 2
                && AsExtracted(arr[1]) != null
                && AsExtracted(arr[1])!.Concepts.Count == 0
                && AsExtracted(arr[1])!.ExtractionStrategy == "rules_v1"));
    }

    [Fact]
    public async Task Persister_NoExtractionEvent_WhenExtractorIsNull()
    {
        // Back-compat: hosts that haven't bound the extractor (legacy
        // tests, future ad-hoc compositions) keep working. The persister
        // simply skips the extraction-event append.
        await Sut(_gate, _mode, _store, extractor: null).PersistAsync(
            "q-1",
            new TestEvent("q-1"),
            new GatedPersistContext("math", "stem", "ans", "en"));

        _events.Received(1).StartStream<QuestionState>(
            "q-1",
            Arg.Is<object[]>(arr =>
                arr.Length == 1 && IsTestEvent(arr[0])));
    }

    [Fact]
    public async Task Persister_ExtractionEvent_RecordsPersisterAsExtractedBy()
    {
        // The audit trail should attribute the event to the persister so
        // ops know it came from the canonical write path (vs. e.g.
        // BagrutDraftPersistence which attributes to itself for the
        // upstream draft stream).
        var extractor = Substitute.For<IQuestionConceptExtractor>();
        extractor.ExtractAsync(Arg.Any<ExtractionInput>(), Arg.Any<CancellationToken>())
                 .Returns(new ExtractionResult(Array.Empty<QuestionConcept>(), "rules_v1"));

        await Sut(_gate, _mode, _store, extractor).PersistAsync(
            "q-1",
            new TestEvent("q-1"),
            new GatedPersistContext("math", "stem", "ans", "en"));

        _events.Received(1).StartStream<QuestionState>(
            "q-1",
            Arg.Is<object[]>(arr =>
                arr.Length == 2
                && AsExtracted(arr[1]) != null
                && AsExtracted(arr[1])!.ExtractedBy == nameof(CasGatedQuestionPersister)));
    }

    [Fact]
    public async Task Persister_ExtractionEventOrder_CreationEventLeads()
    {
        // QuestionState.Apply<QuestionConceptsExtracted_V1> overwrites
        // ConceptIds, so the creation event MUST land first to bootstrap
        // the aggregate. If this regresses, AggregateStreamAsync would
        // return an aggregate with empty Stem/Subject because the create
        // event hadn't run yet.
        var extractor = Substitute.For<IQuestionConceptExtractor>();
        extractor.ExtractAsync(Arg.Any<ExtractionInput>(), Arg.Any<CancellationToken>())
                 .Returns(new ExtractionResult(Array.Empty<QuestionConcept>(), "rules_v1"));

        await Sut(_gate, _mode, _store, extractor).PersistAsync(
            "q-1",
            new TestEvent("q-1"),
            new GatedPersistContext("math", "stem", "ans", "en"),
            extraEventsOnNewStream: new object[]
            {
                new ExtraStubEvent("q-1"),
            });

        _events.Received(1).StartStream<QuestionState>(
            "q-1",
            Arg.Is<object[]>(arr =>
                arr.Length == 3
                && IsTestEvent(arr[0])
                && IsExtractedEvent(arr[1])
                && IsExtraStubEvent(arr[2])));
    }

    private sealed record TestEvent(string QuestionId);
    private sealed record ExtraStubEvent(string QuestionId);

    // Helpers — NSubstitute Arg.Is<T>(Expression<Func<T,bool>>) compiles
    // to an expression tree, which forbids `is` pattern matching. Use
    // ordinary methods so the predicate stays an expression-compatible
    // call.
    private static bool IsTestEvent(object o) => o is TestEvent;
    private static bool IsExtractedEvent(object o) => o is QuestionConceptsExtracted_V1;
    private static bool IsExtraStubEvent(object o) => o is ExtraStubEvent;
    private static QuestionConceptsExtracted_V1? AsExtracted(object o) =>
        o as QuestionConceptsExtracted_V1;
}
