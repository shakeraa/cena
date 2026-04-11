// =============================================================================
// Cena Platform — Adaptive Question Pool Service (STB-01c)
// Integrates QuestionSelector with LearningSessionQueueProjection
// Provides intelligent question selection based on student performance
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Projections;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Serving;

/// <summary>
/// Service for managing adaptive question pools during learning sessions.
/// </summary>
public interface IAdaptiveQuestionPool
{
    /// <summary>
    /// Initialize a new session queue with adaptive question selection.
    /// </summary>
    Task<LearningSessionQueueProjection> InitializeSessionAsync(
        string studentId,
        string sessionId,
        string[] subjects,
        string mode,
        CancellationToken ct = default);

    /// <summary>
    /// Get the next question for a session, refilling queue if needed.
    /// </summary>
    Task<QueuedQuestion?> GetNextQuestionAsync(
        string sessionId,
        IQuestionPool pool,
        CancellationToken ct = default);

    /// <summary>
    /// Record an answer and update adaptive state.
    /// </summary>
    Task RecordAnswerAsync(
        string sessionId,
        string questionId,
        bool isCorrect,
        TimeSpan timeSpent,
        string? selectedOption,
        CancellationToken ct = default);

    /// <summary>
    /// Get session queue state.
    /// </summary>
    Task<LearningSessionQueueProjection?> GetSessionQueueAsync(
        string sessionId,
        CancellationToken ct = default);

    /// <summary>
    /// End a session and archive the queue.
    /// </summary>
    Task EndSessionAsync(string sessionId, CancellationToken ct = default);
}

/// <summary>
/// Implementation using QuestionSelector for adaptive selection.
/// </summary>
public class AdaptiveQuestionPool : IAdaptiveQuestionPool
{
    private readonly IDocumentStore _store;
    private readonly IQuestionSelector _selector;
    private readonly ILogger<AdaptiveQuestionPool> _logger;

    public AdaptiveQuestionPool(
        IDocumentStore store,
        IQuestionSelector selector,
        ILogger<AdaptiveQuestionPool> logger)
    {
        _store = store;
        _selector = selector;
        _logger = logger;
    }

    public async Task<LearningSessionQueueProjection> InitializeSessionAsync(
        string studentId,
        string sessionId,
        string[] subjects,
        string mode,
        CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();

        // Load student profile for concept mastery
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentId);
        var conceptMastery = profile?.ConceptMastery ?? new Dictionary<string, ConceptMasteryState>();

        // Convert ConceptMasteryState to double (PKnown)
        var masterySnapshot = conceptMastery.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.PKnown);

        // Create queue projection
        var queue = new LearningSessionQueueProjection
        {
            Id = sessionId,
            SessionId = sessionId,
            StudentId = studentId,
            Subjects = subjects,
            Mode = mode,
            ConceptMasterySnapshot = masterySnapshot,
            CurrentDifficulty = 0.5,
            StartedAt = DateTime.UtcNow
        };

        session.Store(queue);
        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Initialized adaptive session queue for student {StudentId}, session {SessionId}",
            studentId, sessionId);

        return queue;
    }

    public async Task<QueuedQuestion?> GetNextQuestionAsync(
        string sessionId,
        IQuestionPool pool,
        CancellationToken ct)
    {
        await using var session = _store.LightweightSession();

        var queue = await session.LoadAsync<LearningSessionQueueProjection>(sessionId);
        if (queue == null || queue.EndedAt != null)
        {
            _logger.LogWarning("Session queue not found or ended: {SessionId}", sessionId);
            return null;
        }

        // Refill queue if needed
        if (queue.NeedsRefill)
        {
            await RefillQueueAsync(queue, pool, session);
        }

        // Get next question
        var next = queue.DequeueNext();
        if (next != null)
        {
            session.Store(queue);
            await session.SaveChangesAsync(ct);
        }

        return next;
    }

    public async Task RecordAnswerAsync(
        string sessionId,
        string questionId,
        bool isCorrect,
        TimeSpan timeSpent,
        string? selectedOption,
        CancellationToken ct)
    {
        await using var session = _store.LightweightSession();

        var queue = await session.LoadAsync<LearningSessionQueueProjection>(sessionId);
        if (queue == null)
        {
            _logger.LogWarning("Session queue not found: {SessionId}", sessionId);
            return;
        }

        queue.RecordAnswer(questionId, isCorrect, timeSpent, selectedOption);
        session.Store(queue);

        // Also append event to student stream for analytics
        var evt = new QuestionAnsweredInSession_V1(
            StudentId: queue.StudentId,
            SessionId: sessionId,
            QuestionId: questionId,
            IsCorrect: isCorrect,
            TimeSpentSeconds: (int)timeSpent.TotalSeconds,
            SelectedOption: selectedOption,
            AnsweredAt: DateTimeOffset.UtcNow);

        session.Events.Append(queue.StudentId, evt);
        await session.SaveChangesAsync(ct);
    }

    public async Task<LearningSessionQueueProjection?> GetSessionQueueAsync(
        string sessionId,
        CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        return await session.LoadAsync<LearningSessionQueueProjection>(sessionId);
    }

    public async Task EndSessionAsync(string sessionId, CancellationToken ct)
    {
        await using var session = _store.LightweightSession();

        var queue = await session.LoadAsync<LearningSessionQueueProjection>(sessionId);
        if (queue == null) return;

        queue.EndedAt = DateTime.UtcNow;
        session.Store(queue);

        _logger.LogInformation(
            "Ended session queue {SessionId}. Accuracy: {Accuracy:P}, Questions: {Count}",
            sessionId, queue.GetAccuracy(), queue.TotalQuestionsAttempted);

        await session.SaveChangesAsync(ct);
    }

    private async Task RefillQueueAsync(
        LearningSessionQueueProjection queue,
        IQuestionPool pool,
        IDocumentSession session)
    {
        // Build student context for selector
        var studentContext = new StudentContext(
            StudentId: queue.StudentId,
            PreferredLanguage: "he", // Default to Hebrew for Cena
            DepthUnit: 1,
            ConceptMastery: queue.ConceptMasterySnapshot,
            LastPracticed: queue.AnsweredQuestions.ToDictionary(
                h => h.QuestionId,
                h => (DateTimeOffset)h.AnsweredAt),
            ItemsSeenThisSession: queue.SeenQuestionIds,
            ItemsSeenLast7Days: new HashSet<string>(), // Could load from history
            CurrentFocus: MapStreakToFocus(queue.StreakCount),
            Goal: MapModeToGoal(queue.Mode));

        // Select up to 5 questions
        var selected = new List<QueuedQuestion>();
        for (int i = 0; i < 5; i++)
        {
            var result = _selector.SelectNext(studentContext, pool);
            if (result == null) break;

            selected.Add(new QueuedQuestion
            {
                QuestionId = result.SelectedItem.ItemId,
                ConceptId = result.ConceptId,
                Subject = result.SelectedItem.Subject,
                BloomLevel = result.SelectedItem.BloomLevel,
                Difficulty = result.SelectedItem.Difficulty,
                SelectionReason = result.SelectionReason,
                QueuedAt = DateTime.UtcNow
            });

            // Add to seen items so selector picks different ones
            studentContext.ItemsSeenThisSession.Add(result.SelectedItem.ItemId);
        }

        if (selected.Count > 0)
        {
            queue.EnqueueQuestions(selected);
            _logger.LogDebug(
                "Refilled queue for session {SessionId} with {Count} questions",
                queue.SessionId, selected.Count);
        }
    }

    private static FocusState MapStreakToFocus(int streak)
    {
        return streak switch
        {
            >= 5 => FocusState.Strong,
            >= 3 => FocusState.Stable,
            >= 1 => FocusState.Declining,
            0 => FocusState.Degrading,
            _ => FocusState.Critical
        };
    }

    private static SessionGoal MapModeToGoal(string mode)
    {
        return mode.ToLowerInvariant() switch
        {
            "practice" => SessionGoal.Practice,
            "review" => SessionGoal.Review,
            "challenge" => SessionGoal.Challenge,
            "diagnostic" => SessionGoal.Diagnostic,
            "exam" => SessionGoal.ExamPrep,
            _ => SessionGoal.Practice
        };
    }
}

/// <summary>
/// Event emitted when a question is answered in a session.
/// </summary>
public record QuestionAnsweredInSession_V1(
    string StudentId,
    string SessionId,
    string QuestionId,
    bool IsCorrect,
    int TimeSpentSeconds,
    string? SelectedOption,
    DateTimeOffset AnsweredAt
);
