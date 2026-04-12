// =============================================================================
// Cena Platform -- Messaging Admin Service
// ADM-025: Queries Marten ThreadSummary projections and Redis Streams
// for admin messaging endpoints.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Messaging;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api;

public interface IMessagingAdminService
{
    Task<MessagingThreadListResponse> GetThreadsAsync(
        string? threadType, string? participantId, string? search, int page, int pageSize,
        ClaimsPrincipal user, DateTimeOffset? since = null);
    Task<MessagingThreadDetailDto?> GetThreadDetailAsync(
        string threadId, string? beforeCursor, int limit, ClaimsPrincipal user);
    Task<MessagingContactListResponse> GetContactsAsync(string? search, ClaimsPrincipal user);
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
        string? threadType, string? participantId, string? search, int page, int pageSize,
        ClaimsPrincipal user, DateTimeOffset? since = null)
    {
        var callerSchoolId = TenantScope.GetSchoolFilter(user);
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

        if (since.HasValue)
            query = query.Where(t => t.LastMessageAt > since.Value);

        var allThreads = await query
            .OrderByDescending(t => t.LastMessageAt)
            .ToListAsync();

        // FIND-sec-011: Filter threads by caller's school (check if any participant is in caller's school)
        var filteredThreads = allThreads;
        if (callerSchoolId is not null)
        {
            // Get all students and admins in caller's school
            var schoolStudentIds = await session.Query<StudentProfileSnapshot>()
                .Where(s => s.SchoolId == callerSchoolId)
                .Select(s => s.StudentId)
                .ToListAsync();
            
            var schoolAdminIds = await session.Query<AdminUser>()
                .Where(a => a.School == callerSchoolId && !a.SoftDeleted)
                .Select(a => a.Id)
                .ToListAsync();

            var schoolUserIds = schoolStudentIds.Concat(schoolAdminIds).ToHashSet();

            // Only include threads where at least one participant is in caller's school
            filteredThreads = allThreads
                .Where(t => t.ParticipantIds.Any(p => schoolUserIds.Contains(p)))
                .ToList();
        }

        var totalCount = filteredThreads.Count;
        var threads = filteredThreads
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

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
        string threadId, string? beforeCursor, int limit, ClaimsPrincipal user)
    {
        var callerSchoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        var summary = await session.LoadAsync<ThreadSummary>(threadId);
        if (summary is null)
            return null;

        // FIND-sec-011: Verify caller can access this thread (has participant in caller's school)
        if (callerSchoolId is not null)
        {
            var schoolStudentIds = await session.Query<StudentProfileSnapshot>()
                .Where(s => s.SchoolId == callerSchoolId)
                .Select(s => s.StudentId)
                .ToListAsync();
            
            var schoolAdminIds = await session.Query<AdminUser>()
                .Where(a => a.School == callerSchoolId && !a.SoftDeleted)
                .Select(a => a.Id)
                .ToListAsync();

            var schoolUserIds = schoolStudentIds.Concat(schoolAdminIds).ToHashSet();

            if (!summary.ParticipantIds.Any(p => schoolUserIds.Contains(p)))
            {
                _logger.LogWarning("Cross-tenant thread access: caller from school {CallerSchool} attempted to access thread {ThreadId}",
                    callerSchoolId, threadId);
                return null;
            }
        }

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

    public async Task<MessagingContactListResponse> GetContactsAsync(string? search, ClaimsPrincipal user)
    {
        var callerSchoolId = TenantScope.GetSchoolFilter(user);
        await using var session = _store.QuerySession();

        var query = session.Query<AdminUser>()
            .Where(u => !u.SoftDeleted && u.Status == UserStatus.Active);

        // FIND-sec-011: Filter by caller's school
        if (callerSchoolId is not null)
            query = query.Where(u => u.School == callerSchoolId);

        // Marten translates string.Contains(term) to PostgreSQL ILIKE
        // but doesn't support the StringComparison overload — use plain Contains
        if (!string.IsNullOrEmpty(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(u =>
                u.FullName.ToLower().Contains(term) ||
                u.Email.ToLower().Contains(term));
        }

        var users = await query
            .OrderBy(u => u.FullName)
            .Take(50)
            .ToListAsync();

        var contacts = users.Select(u => new MessagingContactDto(
            u.Id,
            u.FullName,
            u.Role.ToString(),
            u.Email,
            u.AvatarUrl
        )).ToList();

        return new MessagingContactListResponse(contacts);
    }
}
