// =============================================================================
// Cena Platform — Session Answer Endpoint Helper Tests
// Regression suite for FIND-pedagogy-001 / -002 / -003.
//
// The SubmitAnswer lambda in SessionEndpoints.cs calls three pure static
// helpers — BuildBktParameters, BuildConceptAttempt, BuildAnswerFeedback —
// that are directly unit-testable. These tests assert the three pedagogy
// regressions stay fixed:
//
//   pedagogy-001 — BuildAnswerFeedback surfaces per-question Explanation and
//                  per-option DistractorRationale in the response DTO.
//   pedagogy-002 — BuildConceptAttempt produces an event whose IsCorrect
//                  field matches the real answer outcome (no hard-coded true).
//   pedagogy-003 — Posterior mastery runs through IBktService / BktService,
//                  producing a non-linear trajectory over N correct answers
//                  that respects the slip / guess parameters of the concept.
// =============================================================================

using Cena.Actors.Services;
using Cena.Api.Host.Endpoints; // FIND-arch-001: Now from Cena.Student.Api.Host via InternalsVisibleTo
using Cena.Infrastructure.Documents;

namespace Cena.Actors.Tests.Session;

/// <summary>
/// Unit tests for the internal helpers exposed by SessionEndpoints under
/// InternalsVisibleTo. These replace what would otherwise be an HTTP-level
/// integration test with fast, deterministic checks that still exercise the
/// real production code path (no mocks for the math).
/// </summary>
public sealed class SessionAnswerEndpointTests
{
    private const string StudentId = "student-42";
    private const string SessionId = "sess-01";
    private const string ConceptId = "concept:math:fractions";
    private const string QuestionId = "q_math_100";

    // ─────────────────────────────────────────────────────────────────────
    // FIND-pedagogy-001 — explanation plumbing
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildAnswerFeedback_CorrectAnswer_ShipsExplanationFromQuestionDoc()
    {
        // Arrange — a correctly-answered question whose document carries an
        // authored explanation. The helper must place that explanation into
        // the DTO's Explanation field so the UI can render it.
        var question = new QuestionDocument
        {
            QuestionId = QuestionId,
            ConceptId = ConceptId,
            Subject = "Mathematics",
            CorrectAnswer = "1/2",
            Explanation = "To add fractions with the same denominator, add the numerators.",
        };

        // Act
        var response = SessionEndpoints.BuildAnswerFeedback(
            questionDoc: question,
            studentAnswer: "1/2",
            isCorrect: true,
            priorMastery: 0.4,
            posteriorMastery: 0.55,
            nextQuestionId: "q_math_101");

        // Assert
        Assert.True(response.Correct);
        Assert.Equal("Correct", response.Feedback);
        Assert.Equal(
            "To add fractions with the same denominator, add the numerators.",
            response.Explanation);
        Assert.Null(response.DistractorRationale); // no distractor for correct answers
        Assert.Equal("q_math_101", response.NextQuestionId);
        // Mastery delta reflects the real BKT posterior, not +0.05 constant.
        Assert.InRange((double)response.MasteryDelta, 0.149, 0.151);
    }

    [Fact]
    public void BuildAnswerFeedback_WrongAnswer_ShipsDistractorRationaleForChosenOption()
    {
        // Arrange — student picks the "2" distractor; the authored rationale
        // for that specific option must end up in the response alongside the
        // full worked explanation.
        var question = new QuestionDocument
        {
            QuestionId = QuestionId,
            ConceptId = ConceptId,
            CorrectAnswer = "1/2",
            Explanation = "Adding 1/4 + 1/4 gives 2/4 which simplifies to 1/2.",
            DistractorRationales = new Dictionary<string, string>
            {
                ["2"]     = "You added numerators AND denominators — only numerators add when denominators match.",
                ["1/8"]   = "You multiplied the denominators instead of keeping them.",
                ["2/8"]   = "You multiplied denominators and kept the wrong numerator.",
            },
        };

        // Act
        var response = SessionEndpoints.BuildAnswerFeedback(
            questionDoc: question,
            studentAnswer: "2",
            isCorrect: false,
            priorMastery: 0.4,
            posteriorMastery: 0.25,
            nextQuestionId: null);

        // Assert
        Assert.False(response.Correct);
        Assert.Equal("Not quite", response.Feedback);
        Assert.Equal(
            "Adding 1/4 + 1/4 gives 2/4 which simplifies to 1/2.",
            response.Explanation);
        Assert.Equal(
            "You added numerators AND denominators — only numerators add when denominators match.",
            response.DistractorRationale);
        Assert.Equal(0, response.XpAwarded);
        // Wrong answer produces a negative mastery delta.
        Assert.True(response.MasteryDelta < 0m, "wrong answer should lower mastery");
    }

    [Fact]
    public void BuildAnswerFeedback_MissingExplanation_ReturnsNullField_NoBinaryFallback()
    {
        // Arrange — a question with NO authored explanation. The helper must
        // leave Explanation null so the UI renders only the short pill — no
        // empty cards, no placeholder text.
        var question = new QuestionDocument
        {
            QuestionId = QuestionId,
            ConceptId = ConceptId,
            CorrectAnswer = "42",
            Explanation = null,
        };

        // Act
        var response = SessionEndpoints.BuildAnswerFeedback(
            questionDoc: question,
            studentAnswer: "42",
            isCorrect: true,
            priorMastery: 0.4,
            posteriorMastery: 0.55,
            nextQuestionId: null);

        // Assert — no explanation, no distractor, just the short pill.
        Assert.Equal("Correct", response.Feedback);
        Assert.Null(response.Explanation);
        Assert.Null(response.DistractorRationale);
    }

    // ─────────────────────────────────────────────────────────────────────
    // FIND-pedagogy-002 — ConceptAttempted_V1 reflects real isCorrect flag
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildConceptAttempt_WrongAnswer_EmitsIsCorrectFalse()
    {
        // Previously: the ConceptAttempted_V1 event was only emitted inside
        // `if (isCorrect)` AND hard-coded `IsCorrect: true`. This test locks
        // in that the helper now respects the flag.
        var question = new QuestionDocument
        {
            QuestionId = QuestionId,
            ConceptId = ConceptId,
            QuestionType = "multiple-choice",
        };

        var evt = SessionEndpoints.BuildConceptAttempt(
            studentId: StudentId,
            sessionId: SessionId,
            questionDoc: question,
            currentQuestionId: QuestionId,
            methodology: "practice",
            isCorrect: false,
            responseTimeMs: 4_200,
            priorMastery: 0.55,
            posteriorMastery: 0.37,
            errorType: "Conceptual"); // FIND-pedagogy-007: ErrorType now required

        Assert.False(evt.IsCorrect);
        Assert.Equal("Conceptual", evt.ErrorType);
        Assert.Equal(ConceptId, evt.ConceptId);
        Assert.Equal(QuestionId, evt.QuestionId);
        Assert.Equal(StudentId, evt.StudentId);
        Assert.Equal(SessionId, evt.SessionId);
        Assert.Equal(4_200, evt.ResponseTimeMs);
        Assert.Equal(0.55, evt.PriorMastery);
        Assert.Equal(0.37, evt.PosteriorMastery);
        Assert.Equal("practice", evt.MethodologyActive);
    }

    [Fact]
    public void BuildConceptAttempt_CorrectAnswer_EmitsIsCorrectTrue()
    {
        var question = new QuestionDocument
        {
            QuestionId = QuestionId,
            ConceptId = ConceptId,
            QuestionType = "multiple-choice",
        };

        var evt = SessionEndpoints.BuildConceptAttempt(
            studentId: StudentId,
            sessionId: SessionId,
            questionDoc: question,
            currentQuestionId: QuestionId,
            methodology: "practice",
            isCorrect: true,
            responseTimeMs: 3_100,
            priorMastery: 0.4,
            posteriorMastery: 0.55,
            errorType: "None"); // FIND-pedagogy-007: Correct answers have ErrorType "None"

        Assert.True(evt.IsCorrect);
        Assert.Equal(0.4, evt.PriorMastery);
        Assert.Equal(0.55, evt.PosteriorMastery);
    }

    // ─────────────────────────────────────────────────────────────────────
    // FIND-pedagogy-003 — Real BKT posterior, not linear +0.05
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void RealBktPosterior_TenCorrectAnswers_IsNotLinear_AndRespectsSlipGuess()
    {
        // Under the OLD bug: PosteriorMastery = Prior + 0.05, so 10 correct
        // answers always walked the mastery from 0.1 → 0.6. Under real BKT
        // the trajectory depends on slip/guess, is non-linear, and passes
        // the progression threshold well before the 10th attempt.
        var question = new QuestionDocument
        {
            QuestionId = QuestionId,
            ConceptId = ConceptId,
            CorrectAnswer = "42",
            // Mild slip, moderate guess — canonical "not trivially gameable"
            // knowledge component.
            BktSlip = 0.05,
            BktGuess = 0.20,
            BktLearning = 0.10,
        };

        var bkt = new BktService();
        var parameters = SessionEndpoints.BuildBktParameters(question);

        var trajectory = new List<double>();
        double mastery = 0.10; // canonical P_L0

        for (int i = 0; i < 10; i++)
        {
            var result = bkt.Update(new BktUpdateInput(
                PriorMastery: mastery,
                IsCorrect: true,
                Parameters: parameters));
            mastery = result.PosteriorMastery;
            trajectory.Add(mastery);
        }

        // The trajectory MUST NOT match the old linear +0.05 walk.
        // Old bug: [0.15, 0.20, 0.25, 0.30, 0.35, 0.40, 0.45, 0.50, 0.55, 0.60]
        // Each step should deviate from that linear path by more than 0.01.
        for (int i = 0; i < trajectory.Count; i++)
        {
            double linearBug = 0.15 + (0.05 * i);
            Assert.True(
                Math.Abs(trajectory[i] - linearBug) > 0.01,
                $"Step {i}: real BKT ({trajectory[i]:F4}) must differ from linear " +
                $"bug ({linearBug:F4}) by at least 0.01");
        }

        // Non-linearity sanity check: the derivative (delta per step) is NOT
        // constant under real BKT. The first-step delta must differ from the
        // last-step delta by at least 0.005 — proves the curve has shape.
        double firstStepDelta = trajectory[0] - 0.10;
        double lastStepDelta  = trajectory[9] - trajectory[8];
        Assert.True(
            Math.Abs(firstStepDelta - lastStepDelta) > 0.005,
            $"BKT trajectory should be non-linear. First-step delta " +
            $"{firstStepDelta:F4}, last-step delta {lastStepDelta:F4}. " +
            $"Linear +0.05 stub would yield equal deltas.");

        // Mastery must eventually cross the default progression threshold
        // (0.85 by convention in MasteryConstants). With non-zero slip and
        // forgetting factor, the BktService clamps below 1.0, so the cross
        // happens but the value stays under 0.99.
        Assert.Contains(trajectory, m => m >= parameters.ProgressionThreshold);
        Assert.True(trajectory.Max() < 1.0);
    }

    [Fact]
    public void RealBktPosterior_WrongAnswer_DecreasesMastery()
    {
        // Direct assertion that the BKT pipeline we route through lowers
        // mastery on wrong answers — the core property the OLD if-branch
        // wrapping broke by never even emitting the event on failure.
        var question = new QuestionDocument
        {
            QuestionId = QuestionId,
            ConceptId = ConceptId,
            BktSlip = 0.05,
            BktGuess = 0.20,
            BktLearning = 0.10,
        };

        var bkt = new BktService();
        var parameters = SessionEndpoints.BuildBktParameters(question);

        var result = bkt.Update(new BktUpdateInput(
            PriorMastery: 0.7,
            IsCorrect: false,
            Parameters: parameters));

        Assert.True(
            result.PosteriorMastery < 0.7,
            $"Wrong answer must lower mastery. Got {result.PosteriorMastery:F4}, " +
            $"prior was 0.7.");
    }

    [Fact]
    public void BuildBktParameters_UsesDocumentOverrides_WhenPresent()
    {
        // Per-concept slip/guess/learning rates override the library defaults.
        var question = new QuestionDocument
        {
            QuestionId = QuestionId,
            ConceptId = ConceptId,
            BktSlip = 0.11,
            BktGuess = 0.22,
            BktLearning = 0.33,
        };

        var parameters = SessionEndpoints.BuildBktParameters(question);

        Assert.Equal(0.11, parameters.PSlip);
        Assert.Equal(0.22, parameters.PGuess);
        Assert.Equal(0.33, parameters.PLearning);
    }

    [Fact]
    public void BuildBktParameters_FallsBackToDefaults_WhenDocumentFieldsNull()
    {
        var question = new QuestionDocument
        {
            QuestionId = QuestionId,
            ConceptId = ConceptId,
            BktSlip = null,
            BktGuess = null,
            BktLearning = null,
        };

        var parameters = SessionEndpoints.BuildBktParameters(question);
        var defaults   = BktParameters.Default;

        Assert.Equal(defaults.PSlip, parameters.PSlip);
        Assert.Equal(defaults.PGuess, parameters.PGuess);
        Assert.Equal(defaults.PLearning, parameters.PLearning);
    }
}
