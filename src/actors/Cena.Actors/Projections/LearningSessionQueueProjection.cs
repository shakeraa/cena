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
    /// prr-203 — Per-question hint-ladder rung state. Keyed by QuestionId,
    /// stores the highest rung (0 = none, 1 = L1 template shown, 2 = L2
    /// Haiku shown, 3 = L3 worked example shown) that the student has
    /// already been served for that question. Used by the hint-ladder
    /// endpoint (POST /api/sessions/{sid}/question/{qid}/hint/next) so
    /// the server — not the client — decides which rung to advance to,
    /// per ADR-0045 §3. A client cannot skip L1 by requesting L2 because
    /// the orchestrator reads and advances from this map. Resets per
    /// session (session-scoped storage) so a new session starts fresh.
    /// Serialized as part of the Marten document; new sessions default
    /// to an empty map so older session snapshots remain forward-
    /// compatible.
    /// </summary>
    public Dictionary<string, int> LadderRungByQuestion { get; set; } = new();

    /// <summary>
    /// PRR-260 — Student-controlled hide-then-reveal attempt mode. Default
    /// "visible" (traditional render). The student may flip to
    /// "hidden_reveal" at session start or mid-session via the settings
    /// drawer; the flip is session-scoped (persona-ethics autonomy
    /// guardrail — student re-opts-in each session rather than having a
    /// cross-session default that might feel prescriptive). See
    /// <see cref="Cena.Actors.Sessions.SessionAttemptMode"/> for the canonical
    /// enum + wire strings and
    /// <see cref="Cena.Actors.Sessions.SessionAttemptModePolicy.ResolveEffective"/>
    /// for the read-side override policy (non-MC / author-force-visible /
    /// diagnostic-block all force Visible regardless of stored mode).
    /// Serialised as a string on Marten docs so a replay of an older
    /// session without this field reads as the default Visible via
    /// <c>SessionAttemptModeWire.TryParse</c> at the endpoint boundary.
    /// </summary>
    public string AttemptMode { get; set; } = Cena.Actors.Sessions.SessionAttemptModeWire.Visible;

    /// <summary>
    /// RDY-057c — Concept ids the student self-reported as anxious in
    /// onboarding (TopicFeelings == Anxious). Captured at session start
    /// from OnboardingSelfAssessmentDocument when present. Empty list =
    /// student skipped the self-assessment OR has no anxious concepts.
    ///
    /// Consumed as a tie-breaker signal in ZPD selection (per
    /// LearningSessionActor.HandleNextQuestion) — never as a primary
    /// decision and never to BLOCK a concept. Affective self-signal
    /// is weaker than observed BKT data; this field nudges selection
    /// when two concepts have similar ZPD scores.
    ///
    /// Privacy: session-scoped per ADR-0003 — copied into the queue
    /// doc at session start, never retained beyond session lifetime,
    /// never joined with cross-session profile fields.
    /// </summary>
    public List<string> AnxiousConceptIds { get; set; } = new();

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
    public QueuedQuestion? DequeueNext(DateTime now)
    {
        if (QuestionQueue.Count == 0) return null;
        
        var question = QuestionQueue.Dequeue();
        CurrentQuestionId = question.QuestionId;
        CurrentQuestionShownAt = now;
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
    public void RecordAnswer(string questionId, bool isCorrect, TimeSpan timeSpent, string? selectedOption, DateTime now)
    {
        var history = new QuestionHistory
        {
            QuestionId = questionId,
            AnsweredAt = now,
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
    public TimeSpan GetDuration(DateTime now)
    {
        var end = EndedAt ?? now;
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
