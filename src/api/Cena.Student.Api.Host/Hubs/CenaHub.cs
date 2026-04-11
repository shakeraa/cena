// =============================================================================
// Cena Platform -- CenaHub (SES-001.1)
// SignalR hub bridging student clients to the actor system via NATS.
// Commands are published to NATS; events are pushed back by NatsSignalRBridge.
// =============================================================================

using System.Collections.Concurrent;
using System.Text.Json;
using Cena.Actors.Bus;
using Cena.Api.Contracts.Hub;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NATS.Client.Core;

namespace Cena.Api.Host.Hubs;

[Authorize]
public sealed class CenaHub : Hub<ICenaClient>
{
    private readonly INatsConnection _nats;
    private readonly SignalRGroupManager _groupManager;
    private readonly ILogger<CenaHub> _logger;

    // Rate limiting: track command timestamps per connection
    private static readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _commandTimestamps = new();
    private const int MaxCommandsPerSecond = 10;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public CenaHub(
        INatsConnection nats,
        SignalRGroupManager groupManager,
        ILogger<CenaHub> logger)
    {
        _nats = nats;
        _groupManager = groupManager;
        _logger = logger;
    }

    // ── Connection lifecycle ───────────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        var studentId = GetStudentId();
        if (string.IsNullOrEmpty(studentId))
        {
            _logger.LogWarning("Connection {ConnectionId} has no student_id claim — aborting", Context.ConnectionId);
            Context.Abort();
            return;
        }

        // Enforce max 1 connection per student: disconnect stale connection
        var existingConnectionId = _groupManager.GetConnectionId(studentId);
        if (existingConnectionId != null && existingConnectionId != Context.ConnectionId)
        {
            _logger.LogInformation(
                "Student {StudentId} already connected on {OldConnection} — disconnecting stale",
                studentId, existingConnectionId);
            // The stale connection will be cleaned up when its OnDisconnectedAsync fires
        }

        _groupManager.AddConnection(studentId, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, studentId);

        _logger.LogInformation(
            "Student {StudentId} connected via SignalR (connection {ConnectionId})",
            studentId, Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var studentId = GetStudentId();
        if (!string.IsNullOrEmpty(studentId))
        {
            _groupManager.RemoveConnection(studentId, Context.ConnectionId);
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, studentId);

            _logger.LogInformation(
                "Student {StudentId} disconnected (connection {ConnectionId}, reason: {Reason})",
                studentId, Context.ConnectionId, exception?.Message ?? "clean");
        }

        _commandTimestamps.TryRemove(Context.ConnectionId, out _);
        await base.OnDisconnectedAsync(exception);
    }

    // ── Command methods (client → NATS) ────────────────────────────────────

    public async Task StartSession(StartSessionCommand command)
    {
        var studentId = GetStudentIdOrThrow();
        if (!CheckRateLimit()) return;

        var correlationId = Guid.NewGuid().ToString("N");

        var busCmd = new BusStartSession(
            StudentId: studentId,
            SubjectId: command.SubjectId,
            ConceptId: command.ConceptId,
            DeviceType: command.Device.DeviceType,
            AppVersion: command.Device.AppVersion,
            ClientTimestamp: DateTimeOffset.UtcNow,
            SchoolId: GetSchoolId());

        var envelope = BusEnvelope<BusStartSession>.Create(
            NatsSubjects.SessionStart, busCmd, "signalr-hub", GetSchoolId());

        await PublishToNats(NatsSubjects.SessionStart, envelope);
        await Clients.Caller.CommandAck(new CommandAckEvent(correlationId, "StartSession", DateTimeOffset.UtcNow));

        _logger.LogInformation(
            "StartSession command published for student {StudentId}, subject {SubjectId}",
            studentId, command.SubjectId);
    }

    public async Task SubmitAnswer(SubmitAnswerCommand command)
    {
        var studentId = GetStudentIdOrThrow();
        if (!CheckRateLimit()) return;

        var correlationId = Guid.NewGuid().ToString("N");

        var busCmd = new BusConceptAttempt(
            StudentId: studentId,
            SessionId: command.SessionId,
            ConceptId: command.ConceptId,
            QuestionId: command.QuestionId,
            QuestionType: command.QuestionType,
            Answer: command.Answer,
            ResponseTimeMs: command.ResponseTimeMs,
            HintCountUsed: command.HintCountUsed,
            WasSkipped: command.WasSkipped,
            BackspaceCount: command.BackspaceCount,
            AnswerChangeCount: command.AnswerChangeCount);

        var envelope = BusEnvelope<BusConceptAttempt>.Create(
            NatsSubjects.ConceptAttempt, busCmd, "signalr-hub", GetSchoolId());

        await PublishToNats(NatsSubjects.ConceptAttempt, envelope);
        await Clients.Caller.CommandAck(new CommandAckEvent(correlationId, "SubmitAnswer", DateTimeOffset.UtcNow));
    }

    public async Task EndSession(EndSessionCommand command)
    {
        var studentId = GetStudentIdOrThrow();
        if (!CheckRateLimit()) return;

        var correlationId = Guid.NewGuid().ToString("N");

        var busCmd = new BusEndSession(
            StudentId: studentId,
            SessionId: command.SessionId,
            Reason: command.Reason);

        var envelope = BusEnvelope<BusEndSession>.Create(
            NatsSubjects.SessionEnd, busCmd, "signalr-hub", GetSchoolId());

        await PublishToNats(NatsSubjects.SessionEnd, envelope);
        await Clients.Caller.CommandAck(new CommandAckEvent(correlationId, "EndSession", DateTimeOffset.UtcNow));
    }

    public async Task RequestHint(RequestHintCommand command)
    {
        var studentId = GetStudentIdOrThrow();
        if (!CheckRateLimit()) return;

        var correlationId = Guid.NewGuid().ToString("N");

        // Hint requests go as a special annotation with kind "hint_request"
        var busCmd = new BusAddAnnotation(
            StudentId: studentId,
            SessionId: command.SessionId,
            ConceptId: command.ConceptId,
            Text: $"hint_request::{command.QuestionId}",
            Kind: "hint_request");

        var envelope = BusEnvelope<BusAddAnnotation>.Create(
            NatsSubjects.Annotation, busCmd, "signalr-hub", GetSchoolId());

        await PublishToNats(NatsSubjects.Annotation, envelope);
        await Clients.Caller.CommandAck(new CommandAckEvent(correlationId, "RequestHint", DateTimeOffset.UtcNow));
    }

    public async Task SkipQuestion(SkipQuestionCommand command)
    {
        var studentId = GetStudentIdOrThrow();
        if (!CheckRateLimit()) return;

        var correlationId = Guid.NewGuid().ToString("N");

        // A skip is a concept attempt with WasSkipped = true
        var busCmd = new BusConceptAttempt(
            StudentId: studentId,
            SessionId: command.SessionId,
            ConceptId: command.ConceptId,
            QuestionId: command.QuestionId,
            QuestionType: "skip",
            Answer: "",
            ResponseTimeMs: 0,
            HintCountUsed: 0,
            WasSkipped: true,
            BackspaceCount: 0,
            AnswerChangeCount: 0);

        var envelope = BusEnvelope<BusConceptAttempt>.Create(
            NatsSubjects.ConceptAttempt, busCmd, "signalr-hub", GetSchoolId());

        await PublishToNats(NatsSubjects.ConceptAttempt, envelope);
        await Clients.Caller.CommandAck(new CommandAckEvent(correlationId, "SkipQuestion", DateTimeOffset.UtcNow));
    }

    public async Task AddAnnotation(AddAnnotationCommand command)
    {
        var studentId = GetStudentIdOrThrow();
        if (!CheckRateLimit()) return;

        var correlationId = Guid.NewGuid().ToString("N");

        var busCmd = new BusAddAnnotation(
            StudentId: studentId,
            SessionId: command.SessionId,
            ConceptId: command.ConceptId,
            Text: command.Text,
            Kind: command.Kind);

        var envelope = BusEnvelope<BusAddAnnotation>.Create(
            NatsSubjects.Annotation, busCmd, "signalr-hub", GetSchoolId());

        await PublishToNats(NatsSubjects.Annotation, envelope);
        await Clients.Caller.CommandAck(new CommandAckEvent(correlationId, "AddAnnotation", DateTimeOffset.UtcNow));
    }

    public async Task SwitchApproach(SwitchApproachCommand command)
    {
        var studentId = GetStudentIdOrThrow();
        if (!CheckRateLimit()) return;

        var correlationId = Guid.NewGuid().ToString("N");

        var busCmd = new BusMethodologySwitch(
            StudentId: studentId,
            SessionId: command.SessionId,
            FromMethodology: command.FromMethodology,
            ToMethodology: command.ToMethodology,
            Reason: command.Reason);

        var envelope = BusEnvelope<BusMethodologySwitch>.Create(
            NatsSubjects.MethodologySwitch, busCmd, "signalr-hub", GetSchoolId());

        await PublishToNats(NatsSubjects.MethodologySwitch, envelope);
        await Clients.Caller.CommandAck(new CommandAckEvent(correlationId, "SwitchApproach", DateTimeOffset.UtcNow));
    }

    public async Task RequestNextConcept(RequestNextConceptCommand command)
    {
        var studentId = GetStudentIdOrThrow();
        if (!CheckRateLimit()) return;

        var correlationId = Guid.NewGuid().ToString("N");

        // Next-concept requests go as an annotation the actor interprets
        var busCmd = new BusAddAnnotation(
            StudentId: studentId,
            SessionId: command.SessionId,
            ConceptId: "",
            Text: "next_concept_request",
            Kind: "next_concept");

        var envelope = BusEnvelope<BusAddAnnotation>.Create(
            NatsSubjects.Annotation, busCmd, "signalr-hub", GetSchoolId());

        await PublishToNats(NatsSubjects.Annotation, envelope);
        await Clients.Caller.CommandAck(new CommandAckEvent(correlationId, "RequestNextConcept", DateTimeOffset.UtcNow));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private string? GetStudentId()
    {
        return Context.User?.FindFirst("student_id")?.Value
            ?? Context.User?.FindFirst("sub")?.Value;
    }

    private string? GetSchoolId()
    {
        return Context.User?.FindFirst("school_id")?.Value;
    }

    private string GetStudentIdOrThrow()
    {
        return GetStudentId()
            ?? throw new HubException("No student_id claim found in token.");
    }

    private bool CheckRateLimit()
    {
        var connectionId = Context.ConnectionId;
        var timestamps = _commandTimestamps.GetOrAdd(connectionId, _ => new Queue<DateTimeOffset>());
        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddSeconds(-1);

        lock (timestamps)
        {
            // Remove timestamps older than 1 second
            while (timestamps.Count > 0 && timestamps.Peek() < windowStart)
                timestamps.Dequeue();

            if (timestamps.Count >= MaxCommandsPerSecond)
            {
                _logger.LogWarning(
                    "Rate limit exceeded for connection {ConnectionId} ({Count} commands/sec)",
                    connectionId, timestamps.Count);

                Clients.Caller.Error(new HubErrorEvent(
                    Guid.NewGuid().ToString("N"),
                    "RATE_LIMIT_EXCEEDED",
                    $"Maximum {MaxCommandsPerSecond} commands per second exceeded. Please slow down.",
                    now));
                return false;
            }

            timestamps.Enqueue(now);
        }

        return true;
    }

    private async Task PublishToNats<T>(string subject, BusEnvelope<T> envelope)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOpts);
            await _nats.PublishAsync(subject, bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish to NATS subject {Subject}", subject);
            await Clients.Caller.Error(new HubErrorEvent(
                envelope.MessageId,
                "PUBLISH_FAILED",
                "Failed to process command. Please try again.",
                DateTimeOffset.UtcNow));
        }
    }
}
