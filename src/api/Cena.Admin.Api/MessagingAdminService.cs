// =============================================================================
// Cena Platform -- Messaging Admin Service
// ADM-025: Queries Marten ThreadSummary projections and Redis Streams
// for admin messaging endpoints.
// =============================================================================

using Cena.Actors.Messaging;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api;

public interface IMessagingAdminService
{
    Task<MessagingThreadListResponse> GetThreadsAsync(
        string? threadType, string? participantId, string? search, int page, int pageSize);
    Task<MessagingThreadDetailDto?> GetThreadDetailAsync(
        string threadId, string? beforeCursor, int limit);
    Task<MessagingContactListResponse> GetContactsAsync(string? search);
}

public sealed class MessagingAdminService : IMessagingAdminService
{
    private readonly IDocumentStore _store;
    private readonly ILogger<MessagingAdminService> _logger;

    public MessagingAdminService(IDocumentStore store, ILogger<MessagingAdminService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<MessagingThreadListResponse> GetThreadsAsync(
        string? threadType, string? participantId, string? search, int page, int pageSize)
    {
        await using var session = _store.QuerySession();

        IQueryable<ThreadSummary> query = session.Query<ThreadSummary>();

        if (!string.IsNullOrEmpty(threadType))
            query = query.Where(t => t.ThreadType == threadType);

        if (!string.IsNullOrEmpty(participantId))
            query = query.Where(t => t.ParticipantIds.Contains(participantId));

        if (!string.IsNullOrEmpty(search))
            query = query.Where(t =>
                t.LastMessagePreview.Contains(search) ||
                t.ParticipantNames.Any(n => n.Contains(search)));

        var totalCount = await query.CountAsync();

        var threads = await query
            .OrderByDescending(t => t.LastMessageAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var items = threads.Select(t => new MessagingThreadDto(
            t.Id,
            t.ThreadType,
            t.ParticipantIds,
            t.ParticipantNames,
            t.ClassRoomId,
            t.LastMessagePreview,
            t.LastMessageAt,
            t.MessageCount,
            t.CreatedAt
        )).ToList();

        return new MessagingThreadListResponse(items, totalCount, page, pageSize);
    }

    public async Task<MessagingThreadDetailDto?> GetThreadDetailAsync(
        string threadId, string? beforeCursor, int limit)
    {
        await using var session = _store.QuerySession();

        var summary = await session.LoadAsync<ThreadSummary>(threadId);
        if (summary is null)
            return null;

        // Messages are in Redis Streams (MSG-002), but for the admin view we return
        // Marten-stored event data as a fallback. Full Redis integration is in MSG-002.
        // For now, query MessageSent_V1 events from the Marten event store.
        var events = await session.Events.FetchStreamAsync(threadId);
        var messages = events
            .Select(e => e.Data)
            .OfType<Cena.Actors.Events.MessageSent_V1>()
            .OrderByDescending(m => m.SentAt)
            .Take(limit)
            .Select(m => new MessagingMessageDto(
                m.MessageId,
                m.SenderId,
                "", // Name resolved from participants
                m.SenderRole.ToString(),
                m.Content.Text,
                m.Content.ContentType,
                m.Content.ResourceUrl,
                m.Channel.ToString(),
                m.ReplyToMessageId,
                null, null,
                m.SentAt
            ))
            .ToList();

        // Resolve sender names from summary participants
        var nameMap = new Dictionary<string, string>();
        for (var i = 0; i < summary.ParticipantIds.Length; i++)
        {
            if (i < summary.ParticipantNames.Length)
                nameMap[summary.ParticipantIds[i]] = summary.ParticipantNames[i];
        }

        var resolvedMessages = messages.Select(m => m with
        {
            SenderName = nameMap.GetValueOrDefault(m.SenderId, m.SenderId)
        }).ToList();

        return new MessagingThreadDetailDto(
            summary.Id,
            summary.ThreadType,
            summary.ParticipantIds,
            summary.ParticipantNames,
            resolvedMessages,
            summary.MessageCount,
            resolvedMessages.Count >= limit
                ? resolvedMessages.Last().SentAt.ToString("O")
                : null
        );
    }

    public async Task<MessagingContactListResponse> GetContactsAsync(string? search)
    {
        // For now, return admin users from Marten. Full Firebase user list
        // integration requires AdminUserService — reuse its data.
        await using var session = _store.QuerySession();

        // Query ThreadSummary for unique participants as a lightweight contact list
        var summaries = await session.Query<ThreadSummary>()
            .Take(100)
            .ToListAsync();

        var contacts = new Dictionary<string, MessagingContactDto>();
        foreach (var s in summaries)
        {
            for (var i = 0; i < s.ParticipantIds.Length; i++)
            {
                var id = s.ParticipantIds[i];
                if (contacts.ContainsKey(id)) continue;

                var name = i < s.ParticipantNames.Length ? s.ParticipantNames[i] : id;
                if (!string.IsNullOrEmpty(search) &&
                    !name.Contains(search, StringComparison.OrdinalIgnoreCase))
                    continue;

                contacts[id] = new MessagingContactDto(id, name, "Student", null, null);
            }
        }

        return new MessagingContactListResponse(contacts.Values.ToList());
    }
}
