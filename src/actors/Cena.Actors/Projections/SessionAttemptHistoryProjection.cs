// =============================================================================
// Cena Platform -- Session Attempt History Projection (FIND-arch-023)
// Inline projection that builds per-session attempt history for fast reads.
// Replaces event-stream queries in SessionEndpoints GetSessionDetail/Replay.
// =============================================================================

using Cena.Actors.Events;
using Marten.Events;
using Marten.Events.Projections;

namespace Cena.Actors.Projections;

/// <summary>
/// Document storing pre-computed session attempt history.
/// Keyed by sessionId for O(1) lookups in SessionEndpoints.
/// </summary>
public class SessionAttemptHistoryDocument
{
    public string Id { get; set; } = "";
    public string SessionId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public List<SessionAttemptItem> Attempts { get; set; } = new();
    public int TotalAttempts => Attempts.Count;
    public int CorrectAttempts => Attempts.Count(a => a.IsCorrect);
    public double Accuracy => TotalAttempts > 0 ? (double)CorrectAttempts / TotalAttempts : 0;
    public Dictionary<string, double> MasteryDeltas { get; set; } = new();
    public DateTimeOffset LastUpdatedAt { get; set; }
}

public class SessionAttemptItem
{
    public string AttemptId { get; set; } = "";
    public string QuestionId { get; set; } = "";
    public string ConceptId { get; set; } = "";
    public string QuestionType { get; set; } = "";
    public bool IsCorrect { get; set; }
    public int ResponseTimeMs { get; set; }
    public int HintCountUsed { get; set; }
    public bool WasSkipped { get; set; }
    public double PriorMastery { get; set; }
    public double PosteriorMastery { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}

public class SessionAttemptHistoryProjection : MultiStreamProjection<SessionAttemptHistoryDocument, string>
{
    public SessionAttemptHistoryProjection()
    {
        Identity<ConceptAttempted_V1>(e => e.SessionId);
        Identity<ConceptAttempted_V2>(e => e.SessionId);
        ProjectionLifecycle = ProjectionLifecycle.Inline;
    }

    public void Apply(ConceptAttempted_V1 evt, SessionAttemptHistoryDocument doc)
    {
        if (string.IsNullOrEmpty(doc.Id))
        {
            doc.Id = evt.SessionId;
            doc.SessionId = evt.SessionId;
            doc.StudentId = evt.StudentId;
        }

        doc.Attempts.Add(new SessionAttemptItem
        {
            AttemptId = $"{evt.SessionId}-{evt.Timestamp:O}",
            QuestionId = evt.QuestionId,
            ConceptId = evt.ConceptId,
            QuestionType = evt.QuestionType,
            IsCorrect = evt.IsCorrect,
            ResponseTimeMs = evt.ResponseTimeMs,
            HintCountUsed = evt.HintCountUsed,
            WasSkipped = evt.WasSkipped,
            PriorMastery = evt.PriorMastery,
            PosteriorMastery = evt.PosteriorMastery,
            Timestamp = evt.Timestamp
        });

        doc.LastUpdatedAt = evt.Timestamp;
        RecomputeMasteryDeltas(doc);
    }

    public void Apply(ConceptAttempted_V2 evt, SessionAttemptHistoryDocument doc)
    {
        if (string.IsNullOrEmpty(doc.Id))
        {
            doc.Id = evt.SessionId;
            doc.SessionId = evt.SessionId;
            doc.StudentId = evt.StudentId;
        }

        doc.Attempts.Add(new SessionAttemptItem
        {
            AttemptId = $"{evt.SessionId}-{evt.Timestamp:O}",
            QuestionId = evt.QuestionId,
            ConceptId = evt.ConceptId,
            QuestionType = evt.QuestionType,
            IsCorrect = evt.IsCorrect,
            ResponseTimeMs = evt.ResponseTimeMs,
            HintCountUsed = evt.HintCountUsed,
            WasSkipped = evt.WasSkipped,
            PriorMastery = evt.PriorMastery,
            PosteriorMastery = evt.PosteriorMastery,
            Timestamp = evt.Timestamp
        });

        doc.LastUpdatedAt = evt.Timestamp;
        RecomputeMasteryDeltas(doc);
    }

    private static void RecomputeMasteryDeltas(SessionAttemptHistoryDocument doc)
    {
        doc.MasteryDeltas = doc.Attempts
            .GroupBy(a => a.ConceptId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var ordered = g.OrderBy(a => a.Timestamp).ToList();
                    return ordered.Last().PosteriorMastery - ordered.First().PriorMastery;
                });
    }
}
