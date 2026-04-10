// =============================================================================
// Cena Platform -- NATS → SignalR Event Bridge (SES-001.2)
// BackgroundService that subscribes to per-student NATS events and pushes
// them to connected SignalR clients via IHubContext<CenaHub>.
// =============================================================================

using System.Text.Json;
using Cena.Actors.Bus;
using Cena.Api.Contracts.Hub;
using Cena.Infrastructure.Tracing;
using Microsoft.AspNetCore.SignalR;
using NATS.Client.Core;

namespace Cena.Api.Host.Hubs;

/// <summary>
/// Subscribes to <c>cena.events.student.&gt;</c> on NATS and routes events
/// to the correct SignalR group (keyed by studentId) via IHubContext.
/// </summary>
public sealed class NatsSignalRBridge : BackgroundService
{
    private readonly INatsConnection _nats;
    private readonly IHubContext<CenaHub, ICenaClient> _hubContext;
    private readonly SignalRGroupManager _groupManager;
    private readonly ILogger<NatsSignalRBridge> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public NatsSignalRBridge(
        INatsConnection nats,
        IHubContext<CenaHub, ICenaClient> hubContext,
        SignalRGroupManager groupManager,
        ILogger<NatsSignalRBridge> logger)
    {
        _nats = nats;
        _hubContext = hubContext;
        _groupManager = groupManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "NatsSignalRBridge starting — subscribing to {Subject}",
            NatsSubjects.AllPerStudentEvents);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SubscribeAndRoute(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NatsSignalRBridge subscription failed — reconnecting in 2s");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("NatsSignalRBridge stopped");
    }

    private async Task SubscribeAndRoute(CancellationToken ct)
    {
        // Subscribe to all per-student events with a wildcard
        // Subject pattern: cena.events.student.{studentId}.{event_type}
        await foreach (var msg in _nats.SubscribeAsync<string>(
            NatsSubjects.AllPerStudentEvents, cancellationToken: ct))
        {
            try
            {
                // INF-020: Extract W3C trace context from NATS headers so the
                // SignalR push becomes a child span of the publishing actor's trace.
                using var traceActivity = NatsTracePropagation.ExtractTraceContext(
                    msg.Headers, $"NatsSignalRBridge.RouteEvent {msg.Subject}");
                traceActivity?.SetTag("messaging.system", "nats");
                traceActivity?.SetTag("messaging.destination", msg.Subject);

                await RouteEvent(msg.Subject, msg.Data ?? "{}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to route NATS event from subject {Subject}", msg.Subject);
            }
        }
    }

    private async Task RouteEvent(string subject, string json)
    {
        // Parse subject: cena.events.student.{studentId}.{event_type}
        var parts = subject.Split('.');
        if (parts.Length < 5)
        {
            _logger.LogDebug("Ignoring NATS message with unexpected subject format: {Subject}", subject);
            return;
        }

        var studentId = parts[3];
        var eventType = parts[4];

        // Only push to connected students
        if (!_groupManager.IsConnected(studentId))
            return;

        var group = _hubContext.Clients.Group(studentId);

        // Extract payload from BusEnvelope if present, otherwise use raw JSON
        var payloadJson = ExtractPayload(json);

        switch (eventType)
        {
            case NatsSubjects.StudentSessionStarted:
                var sessionStarted = Deserialize<SessionStartedEvent>(payloadJson);
                if (sessionStarted != null) await group.SessionStarted(sessionStarted);
                break;

            case NatsSubjects.StudentSessionEnded:
                var sessionEnded = Deserialize<SessionEndedEvent>(payloadJson);
                if (sessionEnded != null) await group.SessionEnded(sessionEnded);
                break;

            case NatsSubjects.StudentAnswerEvaluated:
                var answerEvaluated = Deserialize<AnswerEvaluatedEvent>(payloadJson);
                if (answerEvaluated != null) await group.AnswerEvaluated(answerEvaluated);
                break;

            case NatsSubjects.StudentMasteryUpdated:
                var masteryUpdated = Deserialize<MasteryUpdatedEvent>(payloadJson);
                if (masteryUpdated != null) await group.MasteryUpdated(masteryUpdated);
                break;

            case NatsSubjects.StudentHintDelivered:
                var hintDelivered = Deserialize<HintDeliveredEvent>(payloadJson);
                if (hintDelivered != null) await group.HintDelivered(hintDelivered);
                break;

            case NatsSubjects.StudentMethodologySwitched:
                var methodologySwitched = Deserialize<MethodologySwitchedEvent>(payloadJson);
                if (methodologySwitched != null) await group.MethodologySwitched(methodologySwitched);
                break;

            case NatsSubjects.StudentStagnationDetected:
                var stagnation = Deserialize<StagnationDetectedEvent>(payloadJson);
                if (stagnation != null) await group.StagnationDetected(stagnation);
                break;

            case NatsSubjects.StudentXpAwarded:
                var xpAwarded = Deserialize<XpAwardedEvent>(payloadJson);
                if (xpAwarded != null) await group.XpAwarded(xpAwarded);
                break;

            case NatsSubjects.StudentStreakUpdated:
                var streakUpdated = Deserialize<StreakUpdatedEvent>(payloadJson);
                if (streakUpdated != null) await group.StreakUpdated(streakUpdated);
                break;

            case NatsSubjects.StudentTutoringStarted:
                var tutoringStarted = Deserialize<TutoringStartedEvent>(payloadJson);
                if (tutoringStarted != null) await group.TutoringStarted(tutoringStarted);
                break;

            case NatsSubjects.StudentTutorMessage:
                var tutorMessage = Deserialize<TutorMessageEvent>(payloadJson);
                if (tutorMessage != null) await group.TutorMessage(tutorMessage);
                break;

            case NatsSubjects.StudentTutoringEnded:
                var tutoringEnded = Deserialize<TutoringEndedEvent>(payloadJson);
                if (tutoringEnded != null) await group.TutoringEnded(tutoringEnded);
                break;

            default:
                _logger.LogDebug("Unknown per-student event type: {EventType}", eventType);
                break;
        }
    }

    /// <summary>
    /// Extracts the "payload" field from a BusEnvelope JSON string.
    /// If the JSON has a "payload" property, returns its raw text; otherwise returns the original JSON.
    /// </summary>
    private static string ExtractPayload(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("payload", out var payload))
                return payload.GetRawText();
            // Also try PascalCase (System.Text.Json default serialization)
            if (doc.RootElement.TryGetProperty("Payload", out var payloadPascal))
                return payloadPascal.GetRawText();
        }
        catch (JsonException)
        {
            // Not valid JSON or no payload field — use as-is
        }

        return json;
    }

    private T? Deserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOpts);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize {Type} from NATS payload", typeof(T).Name);
            return null;
        }
    }
}
