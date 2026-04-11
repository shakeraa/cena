// =============================================================================
// Cena Platform — Learning Session Queue Projection (STB-01c)
// Manages the adaptive question queue for an active learning session.
// Tracks question history, current queue, and adaptive selection state.
// =============================================================================

namespace Cena.Actors.Projections;

/// <summary>
/// Marten inline projection for learning session question queue.
/// One document per active session. Provides O(1) question selection.
/// </summary>
public class LearningSessionQueueProjection
{
    // Marten requires Id property - same as SessionId for easy lookup
    public string Id { get; set; } = "";
    
    public string SessionId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string[] Subjects { get; set; } = Array.Empty<string>();
    public string Mode { get; set; } = "practice";
    
    // Queue state
    public Queue<QueuedQuestion> QuestionQueue { get; set; } = new();
    public List<QuestionHistory> AnsweredQuestions { get; set; } = new();
    public HashSet<string> SeenQuestionIds { get; set; } = new();
    
    // Adaptive state
    public Dictionary<string, double> ConceptMasterySnapshot { get; set; } = new();
    public string? CurrentQuestionId { get; set; }
    public DateTime? CurrentQuestionShownAt { get; set; }
    
    // Session metrics
    public int TotalQuestionsAttempted { get; set; }
    public int CorrectAnswers { get; set; }
    public int StreakCount { get; set; }
    public double CurrentDifficulty { get; set; } = 0.5;
    
    // Timestamps
    public DateTime StartedAt { get; set; }
    public DateTime? LastQuestionAt { get; set; }
    public DateTime? EndedAt { get; set; }

    /// <summary>
    /// FIND-pedagogy-006 — Per-question hint counter. Keyed by QuestionId,
    /// stores how many progressive hints the student has already consumed
    /// for THAT question. Used by the REST /hint endpoint to enforce the
    /// MaxHints budget published by <c>ScaffoldingService.GetScaffoldingMetadata</c>.
    /// Serialized as part of the Marten document; new sessions default to
    /// an empty map so older session snapshots remain forward-compatible.
    /// </summary>
    public Dictionary<string, int> HintsUsedByQuestion { get; set; } = new();

    /// <summary>
    /// Check if queue needs refill (empty or low)
    /// </summary>
    public bool NeedsRefill => QuestionQueue.Count < 3 && EndedAt == null;

    /// <summary>
    /// Get next question from queue without removing
    /// </summary>
    public QueuedQuestion? PeekNext()
    {
        return QuestionQueue.Count > 0 ? QuestionQueue.Peek() : null;
    }

    /// <summary>
    /// Dequeue and return next question, updating current state
    /// </summary>
    public QueuedQuestion? DequeueNext()
    {
        if (QuestionQueue.Count == 0) return null;
        
        var question = QuestionQueue.Dequeue();
        CurrentQuestionId = question.QuestionId;
        CurrentQuestionShownAt = DateTime.UtcNow;
        LastQuestionAt = CurrentQuestionShownAt;
        
        return question;
    }

    /// <summary>
    /// Add questions to the queue
    /// </summary>
    public void EnqueueQuestions(IEnumerable<QueuedQuestion> questions)
    {
        foreach (var q in questions)
        {
            if (!SeenQuestionIds.Contains(q.QuestionId))
            {
                QuestionQueue.Enqueue(q);
                SeenQuestionIds.Add(q.QuestionId);
            }
        }
    }

    /// <summary>
    /// Record an answer and update adaptive state
    /// </summary>
    public void RecordAnswer(string questionId, bool isCorrect, TimeSpan timeSpent, string? selectedOption)
    {
        var history = new QuestionHistory
        {
            QuestionId = questionId,
            AnsweredAt = DateTime.UtcNow,
            IsCorrect = isCorrect,
            TimeSpentSeconds = (int)timeSpent.TotalSeconds,
            SelectedOption = selectedOption
        };
        
        AnsweredQuestions.Add(history);
        TotalQuestionsAttempted++;
        
        if (isCorrect)
        {
            CorrectAnswers++;
            StreakCount++;
            // Gradually increase difficulty
            CurrentDifficulty = Math.Min(1.0, CurrentDifficulty + 0.05);
        }
        else
        {
            StreakCount = 0;
            // Decrease difficulty on wrong answer
            CurrentDifficulty = Math.Max(0.1, CurrentDifficulty - 0.1);
        }
        
        CurrentQuestionId = null;
        CurrentQuestionShownAt = null;
    }

    /// <summary>
    /// Get accuracy percentage
    /// </summary>
    public double GetAccuracy()
    {
        if (TotalQuestionsAttempted == 0) return 0;
        return (double)CorrectAnswers / TotalQuestionsAttempted;
    }

    /// <summary>
    /// Get session duration
    /// </summary>
    public TimeSpan GetDuration()
    {
        var end = EndedAt ?? DateTime.UtcNow;
        return end - StartedAt;
    }
}

/// <summary>
/// A question queued for the session
/// </summary>
public class QueuedQuestion
{
    public string QuestionId { get; set; } = "";
    public string ConceptId { get; set; } = "";
    public string Subject { get; set; } = "";
    public int BloomLevel { get; set; }
    public double Difficulty { get; set; }
    public string SelectionReason { get; set; } = "";
    public DateTime QueuedAt { get; set; }
}

/// <summary>
/// History of an answered question
/// </summary>
public class QuestionHistory
{
    public string QuestionId { get; set; } = "";
    public DateTime AnsweredAt { get; set; }
    public bool IsCorrect { get; set; }
    public int TimeSpentSeconds { get; set; }
    public string? SelectedOption { get; set; }
}
