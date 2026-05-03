// =============================================================================
// Cena Platform — BusMessageValidator QuestionType tests (postmortem 2026-04-19)
//
// Regression guards against the validator drift documented in
// docs/postmortems/mt-events-seq-collision-2026-04-19.md:
// BusMessageValidator.ValidQuestionTypes contained "multiplechoice"
// (no underscore) while every emitter in the codebase — the emulator,
// SimulationEventSeeder, ReferenceCalibratedGenerationService —
// emits "multiple_choice" (underscore). Every attempt from the
// emulator was silently rejected.
//
// These tests lock the bus-validator's acceptance set so a future
// change can't drop a form that callers are still emitting.
// =============================================================================

using Cena.Actors.Bus;

namespace Cena.Actors.Tests.Bus;

public class BusMessageValidatorQuestionTypeTests
{
    private static BusConceptAttempt WellFormed(string questionType) => new(
        StudentId: "stu-1",
        SessionId: "sess-1",
        ConceptId: "c-1",
        QuestionId: "q-1",
        QuestionType: questionType,
        Answer: "correct",
        ResponseTimeMs: 5000,
        HintCountUsed: 0,
        WasSkipped: false,
        BackspaceCount: 0,
        AnswerChangeCount: 0);

    [Theory]
    [InlineData("multiple_choice")]   // snake — emitter convention
    [InlineData("multiplechoice")]    // original (pre-drift) form
    [InlineData("multiple-choice")]   // kebab — accepted for completeness
    [InlineData("short_answer")]
    [InlineData("shortanswer")]
    [InlineData("true_false")]
    [InlineData("truefalse")]
    [InlineData("numeric")]
    [InlineData("ordering")]
    [InlineData("free_response")]
    [InlineData("proof")]
    public void ValidQuestionType_Accepted(string questionType)
    {
        var result = BusMessageValidator.Validate(WellFormed(questionType));
        Assert.True(result.IsValid,
            $"QuestionType '{questionType}' should be accepted but was rejected: {result.RejectionReason}");
    }

    [Theory]
    [InlineData("MultipleChoice")]    // case-insensitive
    [InlineData("MULTIPLE_CHOICE")]
    [InlineData("Multiple_Choice")]
    public void QuestionType_CaseInsensitive(string questionType)
    {
        var result = BusMessageValidator.Validate(WellFormed(questionType));
        Assert.True(result.IsValid, $"QuestionType '{questionType}' should be accepted (case-insensitive)");
    }

    [Theory]
    [InlineData("essay")]             // truly unknown type
    [InlineData("multiple choice")]   // space instead of separator
    [InlineData("mcq")]               // abbreviation
    [InlineData("")]                  // empty
    public void InvalidQuestionType_Rejected(string questionType)
    {
        var result = BusMessageValidator.Validate(WellFormed(questionType));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void EmulatorCanonicalForm_Accepted()
    {
        // The emulator's SessionSimulator hard-codes "multiple_choice"
        // at the BusConceptAttempt construction site. If this specific
        // string stops validating, every emulator attempt silently
        // drops — exactly the production-visible bug we postmortemed.
        var result = BusMessageValidator.Validate(WellFormed("multiple_choice"));
        Assert.True(result.IsValid,
            "Emulator's canonical 'multiple_choice' form MUST validate. " +
            "Drift here re-opens the 2026-04-19 bus-rejection bug.");
    }
}
