// =============================================================================
// Cena Platform -- Tutoring Session Document (SAI-08)
// Marten document for archiving tutoring conversations.
// =============================================================================

namespace Cena.Actors.Tutoring;

/// <summary>
/// Persisted record of a tutoring conversation, stored in Marten/PostgreSQL.
/// </summary>
public sealed class TutoringSessionDocument
{
    public string Id { get; init; } = $"tutor-{Guid.NewGuid():N}";
    public string StudentId { get; init; } = "";
    public string SessionId { get; init; } = "";
    public string ConceptId { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Methodology { get; init; } = "";
    public List<ConversationTurn> Turns { get; init; } = new();
    public int TotalTurns { get; init; }
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
}
