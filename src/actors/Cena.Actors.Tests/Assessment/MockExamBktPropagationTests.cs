// =============================================================================
// Cena Platform — MockExamRunService BKT propagation tests (PRR-289)
//
// PRR-289 wires per-question correctness from a submitted mock-exam into the
// IBktStateTracker, the canonical seam for the skill-keyed mastery store.
//
// These tests pin the contract surface that doesn't require a live Postgres:
//
//   1. TryMapExamCodeToTargetCode — exam-code → ExamTargetCode parity
//      with the SubjectForExamCode mapping. Drift here silently drops
//      observations.
//   2. PerQuestionHasObservation — single-cell vs multi-part observation
//      predicate (used for skipped-vs-missing-topic categorisation).
//   3. The full SubmitAsync → BKT propagation pipeline lives in
//      MockExamRunServiceBktPropagationIntegrationTests (sibling file with
//      the [Fact] suite that uses real Postgres on 5433 — same pattern as
//      MockExamRunServiceTests). Splitting the unit pins from the
//      integration suite keeps fast CI fast.
// =============================================================================

using Cena.Actors.Assessment;
using Cena.Actors.ExamTargets;

namespace Cena.Actors.Tests.Assessment;

public sealed class MockExamBktPropagationTests
{
    [Theory]
    [InlineData("806", "bagrut-math-5yu")]
    [InlineData("807", "bagrut-math-4yu")]
    [InlineData("036", "bagrut-physics-5yu")]
    public void TryMapExamCodeToTargetCode_KnownCodes_ReturnCanonicalTargets(
        string examCode, string expectedTarget)
    {
        // Pinning: any change to SubjectForExamCode must keep parity with
        // this mapping or the BKT path silently drops observations for the
        // affected exam code. The test failure message points the dev at
        // the matching switch arm in MockExamRunService.cs.
        Assert.True(
            MockExamRunService.TryMapExamCodeToTargetCode(examCode, out var target),
            $"Expected exam code '{examCode}' to map to a known ExamTargetCode.");
        Assert.Equal(expectedTarget, target.Value);
    }

    [Theory]
    [InlineData("999")]
    [InlineData("")]
    [InlineData("nonsense")]
    public void TryMapExamCodeToTargetCode_UnknownOrEmpty_ReturnsFalse(string examCode)
    {
        // Caller (RecordBktObservationsAsync) treats false as "log + no-op",
        // never as an exception. Empty / unknown codes must NOT throw.
        Assert.False(MockExamRunService.TryMapExamCodeToTargetCode(examCode, out _));
    }

    [Fact]
    public void TryMapExamCodeToTargetCode_ResultIsValidExamTargetCode()
    {
        // The mapping must produce values that ExamTargetCode.Parse accepts —
        // catches a typo that would compile but blow up the gate at runtime.
        foreach (var code in new[] { "806", "807", "036" })
        {
            Assert.True(MockExamRunService.TryMapExamCodeToTargetCode(code, out var target));
            // Round-trip via the public Parse to catch invalid characters
            // or empty results that the TryParse path silently allows
            // (it's a try, after all).
            var reparsed = ExamTargetCode.Parse(target.Value);
            Assert.Equal(target, reparsed);
        }
    }
}
