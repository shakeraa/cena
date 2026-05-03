// =============================================================================
// Cena Platform — Marten Tutor Message Repository (FIND-arch-004)
// Production persistence implementation for TutorMessageService.
// =============================================================================

using Cena.Actors.Tutoring;
using Cena.Infrastructure.Documents;
using Marten;

namespace Cena.Actors.Tutor;

/// <summary>
/// Marten-backed implementation of <see cref="ITutorMessageRepository"/>.
/// Opens short-lived sessions per operation to minimize contention.
/// </summary>
public sealed class MartenTutorMessageRepository : ITutorMessageRepository
{
    private readonly IDocumentStore _store;

    public MartenTutorMessageRepository(IDocumentStore store)
    {
        _store = store;
    }

    public async Task<TutorThreadDocument?> LoadOwnedThreadAsync(
        string threadId,
        string studentId,
        CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var thread = await session.LoadAsync<TutorThreadDocument>(threadId, ct);
        if (thread is null || thread.StudentId != studentId)
            return null;
        return thread;
    }

    public async Task PersistUserMessageAsync(
        TutorThreadDocument thread,
        TutorMessageDocument userMessage,
        CancellationToken ct)
    {
        await using var session = _store.LightweightSession();
        session.Store(userMessage);
        session.Store(thread);
        await session.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TutorMessage>> LoadRecentHistoryAsync(
        string threadId,
        int maxMessages,
        CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var recent = await session.Query<TutorMessageDocument>()
            .Where(m => m.ThreadId == threadId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(maxMessages)
            .ToListAsync(ct);

        return recent
            .OrderBy(m => m.CreatedAt)
            .Select(m => new TutorMessage(m.Role, m.Content))
            .ToList();
    }

    public async Task PersistAssistantMessageAsync(
        TutorThreadDocument thread,
        TutorMessageDocument assistantMessage,
        TutoringMessageSent_V1 analyticsEvent,
        CancellationToken ct)
    {
        await using var session = _store.LightweightSession();
        session.Store(assistantMessage);
        session.Store(thread);
        session.Events.Append(thread.StudentId, analyticsEvent);
        await session.SaveChangesAsync(ct);
    }
}
