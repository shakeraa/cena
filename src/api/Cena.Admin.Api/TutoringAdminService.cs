// =============================================================================
// Cena Platform -- Tutoring Admin Service
// ADM-017: Queries for tutoring session dashboard, budget status, analytics
// =============================================================================

using System.Security.Claims;
using System.Text.Json;
using Cena.Actors.Events;
using Cena.Actors.Tutoring;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Admin.Api;

public interface ITutoringAdminService
{
    Task<TutoringSessionListResponse> GetSessionsAsync(string? studentId, string? status, int page, int pageSize, ClaimsPrincipal user);
    Task<TutoringSessionDetailDto?> GetSessionDetailAsync(string sessionId, ClaimsPrincipal user);
    Task<TutoringBudgetStatusResponse> GetBudgetStatusAsync(string? classId, ClaimsPrincipal user);
    Task<TutoringAnalyticsDto> GetAnalyticsAsync(ClaimsPrincipal user);
}

public sealed class TutoringAdminService : ITutoringAdminService
{
    private const int DailyOutputTokenLimit = 25_000;

    private readonly IDocumentStore _store;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<TutoringAdminService> _logger;

    public TutoringAdminService(
        IDocumentStore store,
        IConnectionMultiplexer redis,
        ILogger<TutoringAdminService> logger)
    {
        _store = store;
        _redis = redis;
        _logger = logger;
    }

    // ── Sessions List ──

    public async Task<TutoringSessionListResponse> GetSessionsAsync(
        string? studentId, string? status, int page, int pageSize, ClaimsPrincipal user)
    {
        // REV-014: Determine school scope for this caller
        var schoolId = TenantScope.GetSchoolFilter(user);

        await using var session = _store.QuerySession();

        // REV-014: When scoped, restrict to students in the caller's school
        HashSet<string>? scopedStudentIds = null;
        if (schoolId is not null)
        {
            var scopedIds = await session.Query<StudentProfileSnapshot>()
                .Where(s => s.SchoolId == schoolId)
                .Select(s => s.StudentId)
                .ToListAsync();
            scopedStudentIds = new HashSet<string>(scopedIds);
        }

        // Query TutoringSessionDocument directly (shared Marten store with Actors)
        var query = session.Query<TutoringSessionDocument>().AsQueryable();

        if (!string.IsNullOrWhiteSpace(studentId))
            query = query.Where(d => d.StudentId == studentId);

        // Materialise so we can derive status in memory
        var allDocs = await query.OrderByDescending(d => d.StartedAt).ToListAsync();

        // REV-014: Apply school filter if scoped
        if (scopedStudentIds is not null)
            allDocs = allDocs.Where(d => scopedStudentIds.Contains(d.StudentId)).ToList();

        // Derive status from document state
        var mapped = allDocs.Select(d => MapToSummary(d)).ToList();

        if (!string.IsNullOrWhiteSpace(status))
            mapped = mapped.Where(s => s.Status == status).ToList();

        var totalCount = mapped.Count;
        var paged = mapped.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        return new TutoringSessionListResponse(paged, totalCount, page, pageSize);
    }

    // ── Session Detail ──

    public async Task<TutoringSessionDetailDto?> GetSessionDetailAsync(string sessionId, ClaimsPrincipal user)
    {
        // REV-014: Determine school scope for this caller
        var schoolId = TenantScope.GetSchoolFilter(user);

        await using var session = _store.QuerySession();

        var doc = await session.Query<TutoringSessionDocument>()
            .FirstOrDefaultAsync(d => d.Id == sessionId || d.SessionId == sessionId);

        if (doc is null)
            return null;

        // REV-014: Verify the session's student belongs to the caller's school
        if (schoolId is not null)
        {
            var snapshot = await session.Query<StudentProfileSnapshot>()
                .Where(s => s.StudentId == doc.StudentId)
                .FirstOrDefaultAsync();
            if (snapshot?.SchoolId != schoolId)
                return null; // Not in caller's school — treat as not found
        }

        // Build conversation turns from the document's persisted turns list
        var turns = doc.Turns.Select(t => new ConversationTurnDto(
            Role: t.Role,
            MessagePreview: t.Content.Length > 200 ? t.Content[..200] : t.Content,
            Timestamp: t.Timestamp,
            RagSourceCount: 0 // RAG source count not stored on ConversationTurn
        )).ToList();

        // Count RAG sources from TutoringMessageSent_V1 events for this session
        var messageEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "tutoring_message_sent_v1")
            .ToListAsync();

        var sessionMessages = messageEvents
            .Where(e => MatchesSessionId(e, doc.Id))
            .ToList();

        var ragSourcesUsed = sessionMessages.Sum(e => ExtractInt(e, "sourceCount"));

        // Update RAG counts on individual turns where available
        for (int i = 0; i < turns.Count && i < sessionMessages.Count; i++)
        {
            var src = ExtractInt(sessionMessages[i], "sourceCount");
            if (src > 0)
                turns[i] = turns[i] with { RagSourceCount = src };
        }

        // FIND-data-021: Use real token count from TutorMessageDocument instead of estimating
        var messageIds = sessionMessages.Select(e => e.Id.ToString()).ToList();
        var tutorMessages = await session.Query<TutorMessageDocument>()
            .Where(m => m.SessionId == sessionId && m.TokensUsed.HasValue)
            .ToListAsync();
        var tokensUsed = tutorMessages.Sum(m => m.TokensUsed ?? 0);

        var durationSeconds = doc.EndedAt.HasValue
            ? (int)(doc.EndedAt.Value - doc.StartedAt).TotalSeconds
            : (int)(DateTimeOffset.UtcNow - doc.StartedAt).TotalSeconds;

        var summary = MapToSummary(doc);

        // Budget remaining: query today's token usage for this student
        var budgetRemaining = await GetStudentBudgetRemainingAsync(session, doc.StudentId);

        return new TutoringSessionDetailDto(
            Id: doc.Id,
            StudentId: doc.StudentId,
            StudentName: doc.StudentId, // Name resolution would require user store lookup
            SessionId: doc.SessionId,
            ConceptId: doc.ConceptId,
            Subject: doc.Subject,
            Methodology: doc.Methodology,
            Status: summary.Status,
            TurnCount: doc.TotalTurns,
            DurationSeconds: durationSeconds,
            TokensUsed: tokensUsed,
            StartedAt: doc.StartedAt,
            EndedAt: doc.EndedAt,
            Turns: turns,
            RagSourcesUsed: ragSourcesUsed,
            SafetyEventsCount: 0, // Safety events tracked separately if needed
            BudgetRemaining: budgetRemaining);
    }

    // ── Budget Status ──

    public async Task<TutoringBudgetStatusResponse> GetBudgetStatusAsync(string? classId, ClaimsPrincipal user)
    {
        // REV-014: Determine school scope for this caller
        var schoolId = TenantScope.GetSchoolFilter(user);

        await using var session = _store.QuerySession();

        // REV-014: When scoped, restrict budget view to students in the caller's school
        HashSet<string>? scopedStudentIds = null;
        if (schoolId is not null)
        {
            var scopedIds = await session.Query<StudentProfileSnapshot>()
                .Where(s => s.SchoolId == schoolId)
                .Select(s => s.StudentId)
                .ToListAsync();
            scopedStudentIds = new HashSet<string>(scopedIds);
        }

        var today = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        var tomorrow = today.AddDays(1);

        // Sum token usage from TutoringMessageSent_V1 events today, grouped by student
        var todayMessages = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "tutoring_message_sent_v1")
            .Where(e => e.Timestamp >= today && e.Timestamp < tomorrow)
            .ToListAsync();

        // FIND-data-021: Use real token counts from TutorMessageDocument
        var today = DateTimeOffset.UtcNow.Date;
        var messageDocs = await session.Query<TutorMessageDocument>()
            .Where(m => m.SentAt >= today && m.SentAt < today.AddDays(1) && m.TokensUsed.HasValue)
            .ToListAsync();
        
        var studentTokens = messageDocs
            .GroupBy(m => m.StudentId)
            .Where(g => !string.IsNullOrEmpty(g.Key)
                && (scopedStudentIds is null || scopedStudentIds.Contains(g.Key))) // REV-014
            .Select(g =>
            {
                var tokensUsed = g.Sum(m => m.TokensUsed ?? 0);
                var percentUsed = (double)tokensUsed / DailyOutputTokenLimit * 100;

                return new StudentBudgetDto(
                    StudentId: g.Key,
                    StudentName: g.Key, // Name resolution deferred
                    TokensUsedToday: tokensUsed,
                    DailyLimit: DailyOutputTokenLimit,
                    PercentUsed: Math.Round(percentUsed, 1),
                    IsExhausted: tokensUsed >= DailyOutputTokenLimit);
            })
            .OrderByDescending(s => s.PercentUsed)
            .ToList();

        var totalTokensToday = studentTokens.Sum(s => s.TokensUsedToday);
        var nearLimitCount = studentTokens.Count(s => s.PercentUsed >= 80);

        return new TutoringBudgetStatusResponse(studentTokens, totalTokensToday, nearLimitCount);
    }

    // ── Analytics ──

    public async Task<TutoringAnalyticsDto> GetAnalyticsAsync(ClaimsPrincipal user)
    {
        // REV-014: Determine school scope for this caller
        var schoolId = TenantScope.GetSchoolFilter(user);

        await using var session = _store.QuerySession();

        var now = DateTimeOffset.UtcNow;
        var todayStart = new DateTimeOffset(now.Date, TimeSpan.Zero);
        var weekStart = todayStart.AddDays(-(int)todayStart.DayOfWeek);

        // REV-014: When scoped, restrict analytics to students in the caller's school
        HashSet<string>? scopedStudentIds = null;
        if (schoolId is not null)
        {
            var scopedIds = await session.Query<StudentProfileSnapshot>()
                .Where(s => s.SchoolId == schoolId)
                .Select(s => s.StudentId)
                .ToListAsync();
            scopedStudentIds = new HashSet<string>(scopedIds);
        }

        // Count active sessions (started but not ended) — filtered by school if scoped
        var sessionQuery = session.Query<TutoringSessionDocument>().AsQueryable();
        if (scopedStudentIds is not null)
            sessionQuery = sessionQuery.Where(d => scopedStudentIds.Contains(d.StudentId));

        var activeSessions = await sessionQuery.Where(d => d.EndedAt == null).CountAsync();
        var sessionsToday = await sessionQuery.Where(d => d.StartedAt >= todayStart).CountAsync();
        var sessionsThisWeek = await sessionQuery.Where(d => d.StartedAt >= weekStart).CountAsync();

        // Query TutoringSessionEnded_V1 events for averages and resolution rate
        var endedEvents = await session.Events.QueryAllRawEvents()
            .Where(e => e.EventTypeName == "tutoring_session_ended_v1")
            .ToListAsync();

        var totalEndedCount = endedEvents.Count;
        var avgTurns = totalEndedCount > 0
            ? endedEvents.Average(e => ExtractInt(e, "totalTurns"))
            : 0;

        // Resolution rate: sessions NOT ended by timeout / total
        var nonTimeoutCount = endedEvents.Count(e =>
            ExtractString(e, "endReason") != "inactivity_timeout");
        var resolutionRate = totalEndedCount > 0
            ? (double)nonTimeoutCount / totalEndedCount
            : 0;

        // Average budget usage across students today (scoped to same school as caller)
        var budgetStatus = await GetBudgetStatusAsync(null, user);
        var avgBudgetUsage = budgetStatus.Students.Count > 0
            ? budgetStatus.Students.Average(s => s.PercentUsed)
            : 0;

        return new TutoringAnalyticsDto(
            ActiveSessionCount: activeSessions,
            AvgTurnsPerSession: Math.Round(avgTurns, 1),
            ResolutionRate: Math.Round(resolutionRate, 3),
            AvgBudgetUsagePercent: Math.Round(avgBudgetUsage, 1),
            SessionsToday: sessionsToday,
            SessionsThisWeek: sessionsThisWeek);
    }

    // ── Helpers ──

    private static TutoringSessionSummaryDto MapToSummary(TutoringSessionDocument doc)
    {
        var durationSeconds = doc.EndedAt.HasValue
            ? (int)(doc.EndedAt.Value - doc.StartedAt).TotalSeconds
            : (int)(DateTimeOffset.UtcNow - doc.StartedAt).TotalSeconds;

        // Derive status: active if no EndedAt, otherwise completed
        // Budget exhaustion requires checking the ended event's reason
        var status = doc.EndedAt.HasValue ? "completed" : "active";

        return new TutoringSessionSummaryDto(
            Id: doc.Id,
            StudentId: doc.StudentId,
            StudentName: doc.StudentId, // Name resolution deferred to avoid cross-service calls
            SessionId: doc.SessionId,
            ConceptId: doc.ConceptId,
            Subject: doc.Subject,
            Methodology: doc.Methodology,
            Status: status,
            TurnCount: doc.TotalTurns,
            DurationSeconds: durationSeconds,
            TokensUsed: 0, // Populated on detail view
            StartedAt: doc.StartedAt,
            EndedAt: doc.EndedAt);
    }

    private async Task<int> GetStudentBudgetRemainingAsync(
        Marten.IQuerySession querySession, string studentId)
    {
        var today = DateTimeOffset.UtcNow.Date;

        // FIND-data-021: Use real token counts from TutorMessageDocument
        var tokensUsed = await querySession.Query<TutorMessageDocument>()
            .Where(m => m.StudentId == studentId 
                && m.SentAt >= today 
                && m.SentAt < today.AddDays(1) 
                && m.TokensUsed.HasValue)
            .SumAsync(m => m.TokensUsed ?? 0);

        return Math.Max(0, DailyOutputTokenLimit - tokensUsed);
    }

    private static bool MatchesSessionId(dynamic evt, string tutoringSessionId)
    {
        var extracted = ExtractString(evt, "tutoringSessionId");
        return extracted == tutoringSessionId;
    }

    private static int ExtractInt(dynamic evt, string property)
    {
        try
        {
            object? data = evt.Data;
            if (data is null) return 0;
            var json = JsonDocument.Parse(JsonSerializer.Serialize(data));
            if (json.RootElement.TryGetProperty(property, out var prop) ||
                json.RootElement.TryGetProperty(ToPascalCase(property), out prop))
                return prop.TryGetInt32(out var val) ? val : 0;
        }
        catch { /* best-effort extraction */ }
        return 0;
    }

    private static string ExtractString(dynamic evt, string property)
    {
        try
        {
            object? data = evt.Data;
            if (data is null) return "";
            var json = JsonDocument.Parse(JsonSerializer.Serialize(data));
            if (json.RootElement.TryGetProperty(property, out var prop) ||
                json.RootElement.TryGetProperty(ToPascalCase(property), out prop))
                return prop.GetString() ?? "";
        }
        catch { /* best-effort extraction */ }
        return "";
    }

    private static string ToPascalCase(string camelCase)
    {
        if (string.IsNullOrEmpty(camelCase)) return camelCase;
        return char.ToUpperInvariant(camelCase[0]) + camelCase[1..];
    }
}
