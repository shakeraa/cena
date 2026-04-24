// =============================================================================
// Cena Platform -- Live Monitor Service
// ADM-026: NATS subscriber that fans out SSE events to connected teacher clients
// =============================================================================

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using Cena.Actors.Bus;
using Cena.Actors.Events;
using Cena.Actors.Tutoring;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Tenancy;
using Marten;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Cena.Admin.Api;

// ── Channel key: one channel per connected SSE client ──

public interface ILiveMonitorService
{
    /// <summary>
    /// Returns an async sequence of SSE events for the calling teacher.
    /// The sequence ends when the <paramref name="cancel"/> token fires or 30 min elapses.
    /// </summary>
    IAsyncEnumerable<LiveSessionEvent> StreamAsync(
        ClaimsPrincipal caller,
        string? filterStudentId,
        string? lastEventId,
        CancellationToken cancel);

    /// <summary>Snapshot of all currently-active sessions visible to the caller.</summary>
    Task<ActiveSessionsResponse> GetActiveSessionsAsync(ClaimsPrincipal caller);
}

// ── Background subscriber: single NATS connection, fans out to N SSE writers ──

public sealed class LiveMonitorService : BackgroundService, ILiveMonitorService
{
    // Per-student in-memory cache: last observed snapshot
    private readonly ConcurrentDictionary<string, ActiveSessionSnapshot> _activeSessions = new();

    // Per-client channel: (clientId -> channel)
    private readonly ConcurrentDictionary<string, System.Threading.Channels.Channel<LiveSessionEvent>> _clients = new();

    // Monotonic counter used as SSE "id:"
    private long _eventCounter;

    private readonly INatsConnection _nats;
    private readonly IDocumentStore _store;
    private readonly ILogger<LiveMonitorService> _logger;

    // SSE connection timeout: 30 minutes
    private static readonly TimeSpan SseTimeout = TimeSpan.FromMinutes(30);

    public LiveMonitorService(
        INatsConnection nats,
        IDocumentStore store,
        ILogger<LiveMonitorService> logger)
    {
        _nats = nats;
        _store = store;
        _logger = logger;
    }

    // ── BackgroundService: subscribe to all per-student events ──

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LiveMonitorService starting — subscribing to {Subject}", NatsSubjects.AllPerStudentEvents);

        try
        {
            // Pre-load active sessions into cache
            await RefreshActiveSessionCacheAsync(stoppingToken);

            await foreach (var msg in _nats.SubscribeAsync<string>(NatsSubjects.AllPerStudentEvents, cancellationToken: stoppingToken))
            {
                if (msg.Data is null) continue;

                try
                {
                    ProcessNatsMessage(msg.Subject, msg.Data);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process NATS message on {Subject}", msg.Subject);
                }
            }
        }
        catch (OperationCanceledException) { }

        _logger.LogInformation("LiveMonitorService stopped");
    }

    // ── Parse NATS subject: cena.events.student.{studentId}.{eventType} ──

    private void ProcessNatsMessage(string subject, string data)
    {
        // subject format: cena.events.student.<studentId>.<event_type>
        var parts = subject.Split('.');
        if (parts.Length < 5) return;

        var studentId = parts[3];
        var eventType = string.Join('.', parts[4..]);

        // Map NATS event type to SSE event name
        var sseEventName = MapToSseEvent(eventType);
        if (sseEventName is null) return;

        var counter = Interlocked.Increment(ref _eventCounter);
        var now = DateTimeOffset.UtcNow;
        // Wrap with envelope so the SSE data is self-contained (studentId + timestamp + inner payload)
        var envelope = "{\"studentId\":\"" + studentId + "\",\"timestamp\":\"" + now.ToString("O") + "\",\"payload\":" + data + "}";
        var liveEvent = new LiveSessionEvent(
            Id: counter.ToString(),
            Event: sseEventName,
            StudentId: studentId,
            Timestamp: now,
            PayloadJson: envelope);

        // Update in-memory session cache for session.started / question.attempted / session.ended
        UpdateSessionCache(studentId, sseEventName, data);

        // Fan out to all subscribed clients that care about this student
        foreach (var (clientId, channel) in _clients)
        {
            _ = channel.Writer.TryWrite(liveEvent);
        }
    }

    private static string? MapToSseEvent(string natsType) => natsType switch
    {
        NatsSubjects.StudentSessionStarted     => "session.started",
        NatsSubjects.StudentSessionEnded        => "session.ended",
        NatsSubjects.StudentAnswerEvaluated     => "question.attempted",
        NatsSubjects.StudentMasteryUpdated      => "mastery.updated",
        NatsSubjects.StudentStagnationDetected  => "stagnation.detected",
        NatsSubjects.StudentMethodologySwitched => "methodology.switched",
        NatsSubjects.StudentTutoringStarted     => "tutoring.started",
        NatsSubjects.StudentTutorMessage        => "tutoring.message",
        NatsSubjects.StudentTutoringEnded       => "tutoring.ended",
        _ => null
    };

    private void UpdateSessionCache(string studentId, string sseEvent, string payloadJson)
    {
        try
        {
            var doc = JsonDocument.Parse(payloadJson).RootElement;

            if (sseEvent == "session.started")
            {
                var snapshot = new ActiveSessionSnapshot(
                    SessionId:     GetString(doc, "sessionId") ?? Guid.NewGuid().ToString("N"),
                    StudentId:     studentId,
                    StudentName:   GetString(doc, "studentName") ?? studentId,
                    Subject:       GetString(doc, "subject") ?? "-",
                    ConceptId:     GetString(doc, "conceptId") ?? "-",
                    Methodology:   GetString(doc, "methodology") ?? "-",
                    QuestionCount: 0,
                    CorrectCount:  0,
                    FatigueScore:  0.0,
                    DurationSeconds: 0,
                    StartedAt:     DateTimeOffset.UtcNow);

                _activeSessions[studentId] = snapshot;
            }
            else if (sseEvent == "session.ended")
            {
                _activeSessions.TryRemove(studentId, out _);
            }
            else if (sseEvent == "question.attempted" && _activeSessions.TryGetValue(studentId, out var existing))
            {
                var isCorrect = doc.TryGetProperty("isCorrect", out var ic) && ic.GetBoolean();
                var fatigue   = doc.TryGetProperty("fatigueScore", out var fs) ? fs.GetDouble() : existing.FatigueScore;
                var elapsed   = (int)(DateTimeOffset.UtcNow - existing.StartedAt).TotalSeconds;

                _activeSessions[studentId] = existing with
                {
                    QuestionCount = existing.QuestionCount + 1,
                    CorrectCount = existing.CorrectCount + (isCorrect ? 1 : 0),
                    FatigueScore = fatigue,
                    DurationSeconds = elapsed,
                };
            }
        }
        catch
        {
            // Ignore JSON parse errors — stale cache is acceptable
        }
    }

    private static string? GetString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var v) ? v.GetString() : null;

    // ── ILiveMonitorService: SSE streaming ──

    public async IAsyncEnumerable<LiveSessionEvent> StreamAsync(
        ClaimsPrincipal caller,
        string? filterStudentId,
        string? lastEventId,
        [EnumeratorCancellation] CancellationToken cancel)
    {
        var clientId = Guid.NewGuid().ToString("N");
        var channel  = System.Threading.Channels.Channel.CreateBounded<LiveSessionEvent>(
            new System.Threading.Channels.BoundedChannelOptions(500)
            {
                FullMode = System.Threading.Channels.BoundedChannelFullMode.DropOldest,
            });

        _clients[clientId] = channel;

        using var timeoutCts = new CancellationTokenSource(SseTimeout);
        using var linked     = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeoutCts.Token);

        try
        {
            // Determine student filter for this teacher
            var schoolId = TenantScope.GetSchoolFilter(caller);
            HashSet<string>? allowedStudents = null;

            if (!string.IsNullOrWhiteSpace(filterStudentId))
            {
                allowedStudents = new HashSet<string>(StringComparer.Ordinal) { filterStudentId };
            }
            else if (schoolId is not null)
            {
                allowedStudents = await GetStudentIdsForSchoolAsync(schoolId, linked.Token);
            }

            // Emit snapshots for all currently-active sessions as initial "session.snapshot" events
            foreach (var snapshot in _activeSessions.Values)
            {
                if (allowedStudents is not null && !allowedStudents.Contains(snapshot.StudentId))
                    continue;

                var counter = Interlocked.Increment(ref _eventCounter);
                yield return new LiveSessionEvent(
                    Id:           counter.ToString(),
                    Event:        "session.snapshot",
                    StudentId:    snapshot.StudentId,
                    Timestamp:    DateTimeOffset.UtcNow,
                    PayloadJson:  JsonSerializer.Serialize(snapshot));
            }

            // Stream live events
            await foreach (var ev in channel.Reader.ReadAllAsync(linked.Token))
            {
                if (allowedStudents is not null && !allowedStudents.Contains(ev.StudentId))
                    continue;

                yield return ev;
            }
        }
        finally
        {
            _clients.TryRemove(clientId, out _);
            channel.Writer.TryComplete();
        }
    }

    // ── ILiveMonitorService: REST snapshot ──

    public async Task<ActiveSessionsResponse> GetActiveSessionsAsync(ClaimsPrincipal caller)
    {
        var schoolId = TenantScope.GetSchoolFilter(caller);
        HashSet<string>? allowedStudents = null;

        if (schoolId is not null)
            allowedStudents = await GetStudentIdsForSchoolAsync(schoolId, CancellationToken.None);

        var sessions = _activeSessions.Values
            .Where(s => allowedStudents is null || allowedStudents.Contains(s.StudentId))
            .OrderBy(s => s.StartedAt)
            .ToList();

        return new ActiveSessionsResponse(sessions, sessions.Count);
    }

    // ── Helpers ──

    private async Task<HashSet<string>> GetStudentIdsForSchoolAsync(string schoolId, CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var ids = await session.Query<StudentProfileSnapshot>()
            .Where(s => s.SchoolId == schoolId)
            .Select(s => s.StudentId)
            .ToListAsync(ct);

        return new HashSet<string>(ids, StringComparer.Ordinal);
    }

    private async Task RefreshActiveSessionCacheAsync(CancellationToken ct)
    {
        try
        {
            await using var session = _store.QuerySession();

            // Load sessions that started today and have no EndedAt
            var cutoff = DateTimeOffset.UtcNow.AddHours(-8);
            var activeDocs = await session.Query<TutoringSessionDocument>()
                .Where(d => d.StartedAt > cutoff && d.EndedAt == null)
                .ToListAsync(ct);

            // Also load student names
            var studentIds = activeDocs.Select(d => d.StudentId).Distinct().ToList();
            var profiles   = await session.Query<StudentProfileSnapshot>()
                .Where(s => studentIds.Contains(s.StudentId))
                .ToListAsync(ct);

            var nameMap = profiles.ToDictionary(p => p.StudentId, p => p.StudentId);

            foreach (var doc in activeDocs)
            {
                var elapsed = (int)(DateTimeOffset.UtcNow - doc.StartedAt).TotalSeconds;
                var snapshot = new ActiveSessionSnapshot(
                    SessionId:      doc.Id,
                    StudentId:      doc.StudentId,
                    StudentName:    nameMap.GetValueOrDefault(doc.StudentId, doc.StudentId),
                    Subject:        doc.Subject ?? "-",
                    ConceptId:      doc.ConceptId ?? "-",
                    Methodology:    doc.Methodology ?? "-",
                    QuestionCount:  doc.Turns.Count(t => t.Role == "student"),
                    CorrectCount:   0, // not tracked at document level
                    FatigueScore:   0.0,
                    DurationSeconds: elapsed,
                    StartedAt:      doc.StartedAt);

                _activeSessions[doc.StudentId] = snapshot;
            }

            _logger.LogInformation("LiveMonitor cache seeded with {Count} active sessions", _activeSessions.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to seed active session cache");
        }
    }
}
