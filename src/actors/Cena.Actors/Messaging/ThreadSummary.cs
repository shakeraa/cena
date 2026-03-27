// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — ThreadSummary Read Model & Projection
// Layer: Data / Read Model | Runtime: .NET 9 | ORM: Marten v8.x
// Async projection that maintains thread metadata in PostgreSQL.
// Individual messages live in Redis Streams — this only tracks
// thread-level summary data for list/query endpoints.
// ═══════════════════════════════════════════════════════════════════════

using Cena.Actors.Events;
using Marten;
using Marten.Events.Aggregation;

namespace Cena.Actors.Messaging;

/// <summary>
/// Read model for thread list queries. Lightweight — no message content
/// beyond the last message preview. Queryable by ParticipantIds via
/// PostgreSQL GIN index on the array column.
/// </summary>
public sealed class ThreadSummary
{
    public string Id { get; set; } = "";
    public string ThreadType { get; set; } = "";
    public string[] ParticipantIds { get; set; } = Array.Empty<string>();
    public string[] ParticipantNames { get; set; } = Array.Empty<string>();
    public string? ClassRoomId { get; set; }
    public string LastMessagePreview { get; set; } = "";
    public DateTimeOffset LastMessageAt { get; set; }
    public int MessageCount { get; set; }
    public string CreatedById { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Marten single-stream projection that builds ThreadSummary from
/// ThreadCreated_V1 and MessageSent_V1 domain events.
/// </summary>
public sealed class ThreadSummaryProjection : SingleStreamProjection<ThreadSummary, string>
{
    public ThreadSummary Create(ThreadCreated_V1 evt) => new()
    {
        Id = evt.ThreadId,
        ThreadType = evt.ThreadType,
        ParticipantIds = evt.ParticipantIds,
        ParticipantNames = evt.ParticipantNames,
        ClassRoomId = evt.ClassRoomId,
        CreatedById = evt.CreatedById,
        CreatedAt = evt.CreatedAt,
        LastMessageAt = evt.CreatedAt,
        MessageCount = 0,
        LastMessagePreview = "",
    };

    public void Apply(MessageSent_V1 evt, ThreadSummary summary)
    {
        summary.MessageCount++;
        summary.LastMessageAt = evt.SentAt;
        summary.LastMessagePreview = evt.Content.Text.Length > 100
            ? evt.Content.Text[..100]
            : evt.Content.Text;
    }
}
