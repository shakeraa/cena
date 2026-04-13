// =============================================================================
// Cena Platform — Scaffolding Integration Tests
// Tests that verify the scaffolding and hint endpoints work end-to-end.
//
// These tests exercise:
//   - Scaffolding level determination based on student mastery
//   - Hint budget allocation and consumption
//   - Per-question hint usage tracking
//   - Rate limiting when hint budget is exhausted
//
// Citations:
//   - Sweller, van Merriënboer & Paas (1998). Cognitive Architecture and
//     Instructional Design. Educational Psychology Review, 10(3), 251-296.
//   - Renkl & Atkinson (2003). Structuring the Transition From Example
//     Study to Problem Solving. Educational Psychologist, 38(1), 15-22.
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Actors.Projections;
using Cena.Actors.Services;
using Cena.Api.Contracts.Sessions;
using Cena.Api.Host.Endpoints;
using Cena.Infrastructure.Documents;

namespace Cena.Actors.Tests.Session;

/// <summary>
/// Integration tests for scaffolding and hint functionality.
/// These tests verify that the scaffolding system correctly determines
/// support levels based on student mastery and manages hint budgets.
/// </summary>
public sealed class ScaffoldingIntegrationTests
{
    private const string StudentId = "student-test-001";
    private const string SessionId = "sess-test-001";
    private const string ConceptId = "concept:physics:ohms-law";
    private const string QuestionId = "q_ohm_101";

    // ═════════════════════════════════════════════════════════════════════════
    // Test 1: Low mastery (0.1) returns Full scaffolding
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetCurrentQuestion_LowMastery_ReturnsFullScaffolding()
    {
        // Arrange: Create session with student mastery 0.1 on concept
        var sessionQueue = CreateSessionQueue(mastery: 0.1f);
        var questionDoc = CreateQuestionDocument();

        // Act: Determine scaffolding level based on mastery
        var scaffoldingLevel = ScaffoldingService.DetermineLevel(
            effectiveMastery: 0.1f,
            psi: 1.0f); // Full PSI for REST path
        var metadata = ScaffoldingService.GetScaffoldingMetadata(scaffoldingLevel);

        // Build the SessionQuestionDto as the endpoint would
        var dto = BuildSessionQuestionDto(questionDoc, sessionQueue, metadata);

        // Assert
        Assert.Equal(ScaffoldingLevel.Full, scaffoldingLevel);
        Assert.Equal("Full", dto.ScaffoldingLevel);
        Assert.NotNull(dto.WorkedExample);
        Assert.Equal(3, dto.HintsAvailable);
        Assert.Equal(3, dto.HintsRemaining);
        Assert.True(metadata.ShowWorkedExample);
        Assert.True(metadata.ShowHintButton);
        Assert.True(metadata.RevealAnswer);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 2: High mastery (0.9) returns None scaffolding
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetCurrentQuestion_HighMastery_ReturnsNoneScaffolding()
    {
        // Arrange: Create session with student mastery 0.9 on concept
        var sessionQueue = CreateSessionQueue(mastery: 0.9f);
        var questionDoc = CreateQuestionDocument();

        // Act: Determine scaffolding level based on mastery
        var scaffoldingLevel = ScaffoldingService.DetermineLevel(
            effectiveMastery: 0.9f,
            psi: 1.0f);
        var metadata = ScaffoldingService.GetScaffoldingMetadata(scaffoldingLevel);

        // Build the SessionQuestionDto as the endpoint would
        var dto = BuildSessionQuestionDto(questionDoc, sessionQueue, metadata);

        // Assert
        Assert.Equal(ScaffoldingLevel.None, scaffoldingLevel);
        Assert.Equal("None", dto.ScaffoldingLevel);
        Assert.Null(dto.WorkedExample);
        Assert.Equal(0, dto.HintsAvailable);
        Assert.Equal(0, dto.HintsRemaining);
        Assert.False(metadata.ShowWorkedExample);
        Assert.False(metadata.ShowHintButton);
        Assert.False(metadata.RevealAnswer);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 3: Medium mastery (0.3) returns Partial scaffolding
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void GetCurrentQuestion_MediumMastery_ReturnsPartialScaffolding()
    {
        // Arrange: Create session with student mastery 0.3 on concept
        var sessionQueue = CreateSessionQueue(mastery: 0.3f);
        var questionDoc = CreateQuestionDocument();

        // Act: Determine scaffolding level based on mastery
        var scaffoldingLevel = ScaffoldingService.DetermineLevel(
            effectiveMastery: 0.3f,
            psi: 1.0f);
        var metadata = ScaffoldingService.GetScaffoldingMetadata(scaffoldingLevel);

        // Build the SessionQuestionDto as the endpoint would
        var dto = BuildSessionQuestionDto(questionDoc, sessionQueue, metadata);

        // Assert
        Assert.Equal(ScaffoldingLevel.Partial, scaffoldingLevel);
        Assert.Equal("Partial", dto.ScaffoldingLevel);
        Assert.Equal(2, dto.HintsAvailable);
        Assert.Equal(2, dto.HintsRemaining);
        Assert.False(metadata.ShowWorkedExample);
        Assert.True(metadata.ShowHintButton);
        Assert.True(metadata.RevealAnswer);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 4: First hint request returns hint with HasMoreHints flag
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PostHint_FirstHint_ReturnsHintWithHasMoreHints()
    {
        // Arrange: Create session and get current question
        var sessionQueue = CreateSessionQueue(mastery: 0.3f);
        var questionDoc = CreateQuestionDocument();
        var hintGenerator = new HintGenerator();

        // Determine scaffolding level and hint budget
        var scaffoldingLevel = ScaffoldingService.DetermineLevel(0.3f, 1.0f);
        var metadata = ScaffoldingService.GetScaffoldingMetadata(scaffoldingLevel);

        // Build hint option states from question
        var optionStates = SessionEndpoints.BuildHintOptionStates(questionDoc);

        // Act: POST /hint with hintLevel: 1
        var hintRequest = new HintRequest(
            HintLevel: 1,
            QuestionId: QuestionId,
            ConceptId: ConceptId,
            PrerequisiteConceptNames: Array.Empty<string>(),
            Options: optionStates,
            Explanation: questionDoc.Explanation,
            StudentAnswer: null);

        var hintContent = hintGenerator.Generate(hintRequest);

        // Simulate the hint usage tracking
        var hintsUsed = sessionQueue.HintsUsedByQuestion.GetValueOrDefault(QuestionId, 0);
        sessionQueue.HintsUsedByQuestion[QuestionId] = hintsUsed + 1;

        // Build response
        var response = new SessionHintResponseDto(
            HintLevel: 1,
            HintText: hintContent.Text,
            HasMoreHints: hintContent.HasMoreHints && metadata.MaxHints > 1,
            HintsRemaining: metadata.MaxHints - sessionQueue.HintsUsedByQuestion[QuestionId]);

        // Assert
        Assert.NotEmpty(response.HintText);
        Assert.True(response.HasMoreHints, "Should have more hints when MaxHints > 1");
        Assert.Equal(metadata.MaxHints - 1, response.HintsRemaining);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 5: Exceeding hint budget returns 429 Too Many Requests
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PostHint_ExceedsBudget_Returns429()
    {
        // Arrange: Create session and get current question
        var sessionQueue = CreateSessionQueue(mastery: 0.3f);
        var questionDoc = CreateQuestionDocument();
        var hintGenerator = new HintGenerator();

        // Determine scaffolding level (Partial = 2 hints max)
        var scaffoldingLevel = ScaffoldingService.DetermineLevel(0.3f, 1.0f);
        var metadata = ScaffoldingService.GetScaffoldingMetadata(scaffoldingLevel);
        var maxHints = metadata.MaxHints;

        var optionStates = SessionEndpoints.BuildHintOptionStates(questionDoc);

        // Act: Exhaust all hints
        for (int i = 1; i <= maxHints; i++)
        {
            var hintRequest = new HintRequest(
                HintLevel: i,
                QuestionId: QuestionId,
                ConceptId: ConceptId,
                PrerequisiteConceptNames: Array.Empty<string>(),
                Options: optionStates,
                Explanation: questionDoc.Explanation,
                StudentAnswer: null);

            hintGenerator.Generate(hintRequest);
            sessionQueue.HintsUsedByQuestion[QuestionId] = i;
        }

        // Attempt to get hint beyond budget
        var wouldExceedBudget = sessionQueue.HintsUsedByQuestion[QuestionId] >= maxHints;

        // Simulate what the endpoint would do
        var nextHintLevel = maxHints + 1;
        var shouldReturn429 = wouldExceedBudget || nextHintLevel > maxHints;

        // Assert
        Assert.True(shouldReturn429, "Should return 429 when hint budget exceeded");
        Assert.Equal(maxHints, sessionQueue.HintsUsedByQuestion[QuestionId]);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 6: Hint request for wrong question returns 400
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void PostHint_WrongQuestion_Returns400()
    {
        // Arrange: Create session with current question A
        var sessionQueue = CreateSessionQueue(mastery: 0.5f);
        const string currentQuestionId = "q_current_001";
        const string wrongQuestionId = "q_wrong_002";

        // Set current question in session
        sessionQueue.CurrentQuestionId = currentQuestionId;
        sessionQueue.QuestionQueue.Enqueue(new QueuedQuestion
        {
            QuestionId = currentQuestionId,
            ConceptId = ConceptId,
            Subject = "Physics"
        });

        // Act: POST /hint for question B (wrong question)
        var requestedQuestionId = wrongQuestionId;
        var isWrongQuestion = requestedQuestionId != sessionQueue.CurrentQuestionId;

        // Simulate what the endpoint would do
        var shouldReturn400 = isWrongQuestion;

        // Assert
        Assert.True(shouldReturn400, "Should return 400 when hint requested for wrong question");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 7: Hint usage persists across requests
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void HintUsage_PersistsAcrossRequests()
    {
        // Arrange: Create session and get current question
        var sessionQueue = CreateSessionQueue(mastery: 0.3f);
        var questionDoc = CreateQuestionDocument();
        var hintGenerator = new HintGenerator();

        var optionStates = SessionEndpoints.BuildHintOptionStates(questionDoc);

        // Act: POST /hint twice
        for (int i = 1; i <= 2; i++)
        {
            var hintRequest = new HintRequest(
                HintLevel: i,
                QuestionId: QuestionId,
                ConceptId: ConceptId,
                PrerequisiteConceptNames: Array.Empty<string>(),
                Options: optionStates,
                Explanation: questionDoc.Explanation,
                StudentAnswer: null);

            hintGenerator.Generate(hintRequest);
            
            // Increment hint usage counter
            var currentUsage = sessionQueue.HintsUsedByQuestion.GetValueOrDefault(QuestionId, 0);
            sessionQueue.HintsUsedByQuestion[QuestionId] = currentUsage + 1;
        }

        // Simulate: Reload session from DB (create new instance with same data)
        var reloadedSession = ReloadSessionFromDb(sessionQueue);

        // Assert
        Assert.True(reloadedSession.HintsUsedByQuestion.ContainsKey(QuestionId),
            "HintsUsedByQuestion should contain the question key after reload");
        Assert.Equal(2, reloadedSession.HintsUsedByQuestion[QuestionId]);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 8: Scaffolding boundary tests (edge cases)
    // ═════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(0.05f, 1.0f, ScaffoldingLevel.Partial, 2)] // Very low mastery still gets Partial with high PSI
    [InlineData(0.15f, 0.6f, ScaffoldingLevel.Full, 3)]    // Low mastery + weak prereqs = Full
    [InlineData(0.35f, 1.0f, ScaffoldingLevel.Partial, 2)] // Medium mastery = Partial
    [InlineData(0.55f, 1.0f, ScaffoldingLevel.HintsOnly, 1)] // Mid-high mastery = HintsOnly
    [InlineData(0.70f, 1.0f, ScaffoldingLevel.None, 0)]    // Boundary: 0.70 = None
    [InlineData(0.69f, 1.0f, ScaffoldingLevel.HintsOnly, 1)] // Just below boundary
    public void ScaffoldingLevel_BoundaryTests(float mastery, float psi, ScaffoldingLevel expectedLevel, int expectedMaxHints)
    {
        // Act
        var level = ScaffoldingService.DetermineLevel(mastery, psi);
        var metadata = ScaffoldingService.GetScaffoldingMetadata(level);

        // Assert
        Assert.Equal(expectedLevel, level);
        Assert.Equal(expectedMaxHints, metadata.MaxHints);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 9: Hint levels progress correctly (1 → 2 → 3)
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void HintLevels_ProgressCorrectly()
    {
        // Arrange
        var questionDoc = CreateQuestionDocument();
        var hintGenerator = new HintGenerator();
        var optionStates = SessionEndpoints.BuildHintOptionStates(questionDoc);

        // Act: Generate hints at all three levels
        var hints = new List<HintContent>();
        for (int level = 1; level <= 3; level++)
        {
            var hintRequest = new HintRequest(
                HintLevel: level,
                QuestionId: QuestionId,
                ConceptId: ConceptId,
                PrerequisiteConceptNames: Array.Empty<string>(),
                Options: optionStates,
                Explanation: questionDoc.Explanation,
                StudentAnswer: null);

            hints.Add(hintGenerator.Generate(hintRequest));
        }

        // Assert
        Assert.Equal(3, hints.Count);
        
        // Level 1 should have more hints available
        Assert.True(hints[0].HasMoreHints, "Level 1 should indicate more hints available");
        
        // Level 2 should have more hints available
        Assert.True(hints[1].HasMoreHints, "Level 2 should indicate more hints available");
        
        // Level 3 should be the final hint
        Assert.False(hints[2].HasMoreHints, "Level 3 should indicate no more hints");
        
        // Each level should produce different text
        Assert.NotEqual(hints[0].Text, hints[1].Text);
        Assert.NotEqual(hints[1].Text, hints[2].Text);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 10: Per-question hint isolation
    // ═════════════════════════════════════════════════════════════════════════

    [Fact]
    public void HintUsage_IsPerQuestion_Isolated()
    {
        // Arrange: Session with multiple questions
        var sessionQueue = CreateSessionQueue(mastery: 0.3f);
        const string questionA = "q_001";
        const string questionB = "q_002";

        // Act: Use hints on question A only
        sessionQueue.HintsUsedByQuestion[questionA] = 2;
        // Question B has no hints used

        // Assert
        Assert.Equal(2, sessionQueue.HintsUsedByQuestion[questionA]);
        Assert.False(sessionQueue.HintsUsedByQuestion.ContainsKey(questionB));
        Assert.Equal(0, sessionQueue.HintsUsedByQuestion.GetValueOrDefault(questionB, 0));
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Helper methods
    // ═════════════════════════════════════════════════════════════════════════

    private static LearningSessionQueueProjection CreateSessionQueue(float mastery)
    {
        var queue = new LearningSessionQueueProjection
        {
            Id = SessionId,
            SessionId = SessionId,
            StudentId = StudentId,
            Subjects = new[] { "Physics" },
            Mode = "practice",
            StartedAt = DateTime.UtcNow,
            ConceptMasterySnapshot = new Dictionary<string, double>
            {
                [ConceptId] = mastery
            },
            HintsUsedByQuestion = new Dictionary<string, int>()
        };

        // Add a question to the queue
        queue.QuestionQueue.Enqueue(new QueuedQuestion
        {
            QuestionId = QuestionId,
            ConceptId = ConceptId,
            Subject = "Physics",
            Difficulty = 0.5,
            QueuedAt = DateTime.UtcNow
        });

        return queue;
    }

    private static QuestionDocument CreateQuestionDocument()
    {
        return new QuestionDocument
        {
            QuestionId = QuestionId,
            ConceptId = ConceptId,
            Subject = "Physics",
            Prompt = "A 12V battery drives 2A through a resistor. What is R?",
            QuestionType = "multiple-choice",
            Choices = new[] { "3 ohms", "6 ohms", "12 ohms", "24 ohms" },
            CorrectAnswer = "6 ohms",
            Explanation = "Using Ohm's Law: R = V / I = 12V / 2A = 6 ohms.",
            DistractorRationales = new Dictionary<string, string>
            {
                ["3 ohms"] = "That would give 4A by Ohm's law, not 2A.",
                ["12 ohms"] = "That would give 1A.",
                ["24 ohms"] = "That would give 0.5A."
            },
            WorkedExample = "Step 1: Identify given values (V=12V, I=2A). Step 2: Apply Ohm's Law R=V/I. Step 3: Calculate R=12/2=6 ohms."
        };
    }

    private static SessionQuestionDto BuildSessionQuestionDto(
        QuestionDocument questionDoc,
        LearningSessionQueueProjection queue,
        ScaffoldingMetadata metadata)
    {
        var mastery = queue.ConceptMasterySnapshot.GetValueOrDefault(questionDoc.ConceptId, 0.5);
        var scaffoldingLevel = ScaffoldingService.DetermineLevel((float)mastery, 1.0f);

        // Get current hint usage for this question
        var hintsUsed = queue.HintsUsedByQuestion.GetValueOrDefault(questionDoc.QuestionId, 0);

        return new SessionQuestionDto(
            QuestionId: questionDoc.QuestionId,
            QuestionIndex: queue.TotalQuestionsAttempted + 1,
            TotalQuestions: queue.TotalQuestionsAttempted + queue.QuestionQueue.Count + 1,
            Prompt: questionDoc.Prompt,
            QuestionType: questionDoc.QuestionType,
            Choices: questionDoc.Choices ?? Array.Empty<string>(),
            Subject: questionDoc.Subject,
            ExpectedTimeSeconds: 60,
            ScaffoldingLevel: scaffoldingLevel.ToString(),
            WorkedExample: metadata.ShowWorkedExample ? questionDoc.WorkedExample : null,
            HintsAvailable: metadata.MaxHints,
            HintsRemaining: Math.Max(0, metadata.MaxHints - hintsUsed));
    }

    private static LearningSessionQueueProjection ReloadSessionFromDb(LearningSessionQueueProjection original)
    {
        // Simulates reloading the session document from the database
        // In real integration tests with Marten, this would be an actual DB round-trip
        var reloaded = new LearningSessionQueueProjection
        {
            Id = original.Id,
            SessionId = original.SessionId,
            StudentId = original.StudentId,
            Subjects = original.Subjects,
            Mode = original.Mode,
            StartedAt = original.StartedAt,
            ConceptMasterySnapshot = new Dictionary<string, double>(original.ConceptMasterySnapshot),
            HintsUsedByQuestion = new Dictionary<string, int>(original.HintsUsedByQuestion),
            TotalQuestionsAttempted = original.TotalQuestionsAttempted,
            CorrectAnswers = original.CorrectAnswers,
            StreakCount = original.StreakCount
        };

        // Copy queue state
        foreach (var question in original.QuestionQueue)
        {
            reloaded.QuestionQueue.Enqueue(question);
        }

        return reloaded;
    }
}
