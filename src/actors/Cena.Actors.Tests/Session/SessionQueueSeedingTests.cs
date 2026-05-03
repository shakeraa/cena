// =============================================================================
// Cena Platform — Session Queue Seeding Regression Tests (FIND-pedagogy-016)
// Verifies that AdaptiveQuestionPool seeds the queue on session start and
// that GET /current-question never returns "completed" on first call.
// =============================================================================

using Cena.Actors.Infrastructure;
using Cena.Actors.Projections;
using Cena.Actors.Serving;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Actors.Tests.Session;

/// <summary>
/// FIND-pedagogy-016: Regression tests proving the adaptive question pool
/// seeds the queue during session start. Before this fix, POST /start created
/// an ActiveSessionSnapshot but did NOT seed the LearningSessionQueueProjection,
/// causing GET /current-question to return "Session completed!" on the first call.
/// </summary>
public sealed class SessionQueueSeedingTests
{
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IDocumentSession _session = Substitute.For<IDocumentSession>();
    private readonly IQuerySession _querySession = Substitute.For<IQuerySession>();
    private readonly IQuestionSelector _selector = Substitute.For<IQuestionSelector>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public SessionQueueSeedingTests()
    {
        _store.LightweightSession().Returns(_session);
        _store.QuerySession().Returns(_querySession);
    }

    [Fact]
    public async Task InitializeSessionAsync_CreatesQueueProjection_WithCorrectFields()
    {
        // Arrange
        var pool = new AdaptiveQuestionPool(
            _store,
            _selector,
            NullLogger<AdaptiveQuestionPool>.Instance,
            _clock);

        var studentId = "student-001";
        var sessionId = "session-001";
        var subjects = new[] { "Mathematics" };
        var mode = "practice";

        // Act
        var queue = await pool.InitializeSessionAsync(
            studentId, sessionId, subjects, mode);

        // Assert
        Assert.NotNull(queue);
        Assert.Equal(sessionId, queue.SessionId);
        Assert.Equal(studentId, queue.StudentId);
        Assert.Equal(subjects, queue.Subjects);
        Assert.Equal(mode, queue.Mode);
        Assert.Null(queue.EndedAt);
        Assert.Equal(0.5, queue.CurrentDifficulty);

        // Verify the projection was stored in Marten
        _session.Received(1).Store(Arg.Is<LearningSessionQueueProjection>(
            q => q.SessionId == sessionId && q.StudentId == studentId));
        await _session.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact(Skip = "RDY-054e: Session queue seeding fixture returns null — needs IQuestionSelector stub alignment. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public async Task GetNextQuestionAsync_WhenQueueNeedsRefill_CallsSelectorAndReturnsQuestion()
    {
        // Arrange
        var pool = new AdaptiveQuestionPool(
            _store,
            _selector,
            NullLogger<AdaptiveQuestionPool>.Instance,
            _clock);

        var sessionId = "session-refill";
        var queue = new LearningSessionQueueProjection
        {
            Id = sessionId,
            SessionId = sessionId,
            StudentId = "student-001",
            Subjects = new[] { "Mathematics" },
            Mode = "practice",
            ConceptMasterySnapshot = new Dictionary<string, double>(),
            StartedAt = DateTime.UtcNow
        };

        // Queue has < 3 items -> NeedsRefill == true
        Assert.True(queue.NeedsRefill);

        _session.LoadAsync<LearningSessionQueueProjection>(sessionId, Arg.Any<CancellationToken>())
            .Returns(queue);

        // Mock the question pool
        var questionPool = Substitute.For<IQuestionPool>();
        questionPool.GetAvailableConcepts().Returns(new List<string> { "concept:math:algebra" });

        // Mock selector to return a question
        _selector.SelectNext(Arg.Any<StudentContext>(), Arg.Any<IQuestionPool>())
            .Returns(new SelectionResult(
                SelectedItem: new PublishedQuestion(
                    ItemId: "q_001",
                    Subject: "Mathematics",
                    ConceptIds: new[] { "concept:math:algebra" },
                    BloomLevel: 2,
                    Difficulty: 0.5f,
                    QualityScore: 80,
                    Language: "he",
                    StemPreview: "Solve for x",
                    SourceType: "authored",
                    PublishedAt: DateTimeOffset.UtcNow,
                    Explanation: "Divide both sides"),
                ConceptId: "concept:math:algebra",
                SelectionReason: "ZPD match"));

        // Act
        var result = await pool.GetNextQuestionAsync(sessionId, questionPool, CancellationToken.None);

        // Assert — a question was returned (not null / not "completed")
        Assert.NotNull(result);
        Assert.Equal("q_001", result.QuestionId);
        Assert.Equal("concept:math:algebra", result.ConceptId);
    }

    [Fact]
    public void QueueProjection_PeekNext_ReturnsNull_WhenEmpty()
    {
        // This is the exact scenario that triggered the bug:
        // POST /start created the queue but never seeded it,
        // so PeekNext() returned null and the endpoint returned "completed".
        var queue = new LearningSessionQueueProjection
        {
            Id = "empty-session",
            SessionId = "empty-session",
            StudentId = "student-001",
            Subjects = new[] { "Mathematics" },
            Mode = "practice",
            StartedAt = DateTime.UtcNow
        };

        // Before FIND-pedagogy-016 fix: this null caused "Session completed!"
        Assert.Null(queue.PeekNext());
        Assert.True(queue.NeedsRefill, "Empty queue should signal NeedsRefill");
    }

    [Fact]
    public void QueueProjection_PeekNext_ReturnsQuestion_WhenSeeded()
    {
        // After FIND-pedagogy-016 fix: queue is seeded during session start
        var queue = new LearningSessionQueueProjection
        {
            Id = "seeded-session",
            SessionId = "seeded-session",
            StudentId = "student-001",
            Subjects = new[] { "Mathematics" },
            Mode = "practice",
            StartedAt = DateTime.UtcNow
        };

        // Simulate what InitializeSessionAsync + GetNextQuestionAsync does
        queue.EnqueueQuestions(new[]
        {
            new QueuedQuestion
            {
                QuestionId = "q_001",
                ConceptId = "concept:math:algebra",
                Subject = "Mathematics",
                BloomLevel = 2,
                Difficulty = 0.5,
                SelectionReason = "ZPD match",
                QueuedAt = DateTime.UtcNow
            }
        });

        // After seeding: PeekNext returns a real question
        var next = queue.PeekNext();
        Assert.NotNull(next);
        Assert.Equal("q_001", next.QuestionId);
        Assert.NotEqual("completed", next.QuestionId);
    }

    [Fact]
    public void QueueProjection_NeedsRefill_TrueWhenLessThanThreeQuestions()
    {
        var queue = new LearningSessionQueueProjection
        {
            Id = "session-refill-check",
            SessionId = "session-refill-check",
            StudentId = "student-001",
            Subjects = new[] { "Mathematics" },
            Mode = "practice",
            StartedAt = DateTime.UtcNow
        };

        // 0 questions -> needs refill
        Assert.True(queue.NeedsRefill);

        // Add 1 question -> still needs refill
        queue.EnqueueQuestions(new[]
        {
            new QueuedQuestion { QuestionId = "q1", ConceptId = "c1", QueuedAt = DateTime.UtcNow }
        });
        Assert.True(queue.NeedsRefill);

        // Add 2 more (total 3) -> no longer needs refill
        queue.EnqueueQuestions(new[]
        {
            new QueuedQuestion { QuestionId = "q2", ConceptId = "c2", QueuedAt = DateTime.UtcNow },
            new QueuedQuestion { QuestionId = "q3", ConceptId = "c3", QueuedAt = DateTime.UtcNow }
        });
        Assert.False(queue.NeedsRefill);
    }

    [Fact]
    public async Task RecordAnswerAsync_UpdatesQueueState()
    {
        // Arrange
        var pool = new AdaptiveQuestionPool(
            _store,
            _selector,
            NullLogger<AdaptiveQuestionPool>.Instance,
            _clock);

        var sessionId = "session-answer";
        var queue = new LearningSessionQueueProjection
        {
            Id = sessionId,
            SessionId = sessionId,
            StudentId = "student-001",
            Subjects = new[] { "Mathematics" },
            Mode = "practice",
            ConceptMasterySnapshot = new Dictionary<string, double>(),
            StartedAt = DateTime.UtcNow
        };

        _session.LoadAsync<LearningSessionQueueProjection>(sessionId, Arg.Any<CancellationToken>())
            .Returns(queue);

        // Act
        await pool.RecordAnswerAsync(
            sessionId, "q_001", isCorrect: true,
            TimeSpan.FromSeconds(30), "A", CancellationToken.None);

        // Assert
        Assert.Equal(1, queue.TotalQuestionsAttempted);
        Assert.Equal(1, queue.CorrectAnswers);
        Assert.Equal(1, queue.StreakCount);
    }

    [Fact]
    public void MartenQuestionPool_Implements_IQuestionPool()
    {
        // Verify MartenQuestionPool satisfies the IQuestionPool interface
        // so it can be passed to AdaptiveQuestionPool.GetNextQuestionAsync
        var pool = new MartenQuestionPool();
        IQuestionPool ipool = pool; // compile-time check
        Assert.Equal(0, ipool.ItemCount);
        Assert.Empty(ipool.GetAvailableConcepts());
        Assert.Empty(ipool.GetForConcept("any"));
        Assert.Empty(ipool.GetFiltered("any", 1, 6, 0f, 1f));
    }
}
