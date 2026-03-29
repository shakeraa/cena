// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Session Event NATS Publisher
// Layer: Infrastructure | Runtime: .NET 9
// Publishes session and tutoring events to per-student NATS subjects
// for real-time SignalR bridge (SES-001). Fire-and-forget, no throw.
// Follows the same pattern as MessagingNatsPublisher.
// ═══════════════════════════════════════════════════════════════════════

using System.Diagnostics.Metrics;
using System.Text.Json;
using Cena.Actors.Bus;
using Cena.Actors.Events;
using Cena.Actors.Tutoring;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Cena.Actors.Sessions;

public sealed class SessionNatsPublisher : ISessionEventPublisher
{
    private readonly INatsConnection _nats;
    private readonly ILogger<SessionNatsPublisher> _logger;
    private readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly Meter Meter = new("Cena.Session.Nats", "1.0.0");
    private static readonly Counter<long> Published =
        Meter.CreateCounter<long>("cena_session_events_published_total");
    private static readonly Counter<long> Failures =
        Meter.CreateCounter<long>("cena_session_events_failures_total");

    public SessionNatsPublisher(INatsConnection nats, ILogger<SessionNatsPublisher> logger)
    {
        _nats = nats;
        _logger = logger;
    }

    public Task PublishSessionStartedAsync(string studentId, SessionStarted_V1 evt) =>
        PublishSafe(NatsSubjects.StudentEvent(studentId, "session_started"), evt, evt.SessionId);

    public Task PublishSessionEndedAsync(string studentId, SessionEnded_V1 evt) =>
        PublishSafe(NatsSubjects.StudentEvent(studentId, "session_ended"), evt, evt.SessionId);

    public Task PublishConceptAttemptedAsync(string studentId, ConceptAttempted_V1 evt) =>
        PublishSafe(NatsSubjects.StudentEvent(studentId, "answer_evaluated"), evt, evt.QuestionId);

    public Task PublishMasteryUpdatedAsync(string studentId, ConceptMastered_V1 evt) =>
        PublishSafe(NatsSubjects.StudentEvent(studentId, "mastery_updated"), evt, $"{evt.ConceptId}-{evt.Timestamp.Ticks}");

    public Task PublishHintDeliveredAsync(string studentId, HintRequested_V1 evt) =>
        PublishSafe(NatsSubjects.StudentEvent(studentId, "hint_delivered"), evt, $"{evt.QuestionId}-hint-{evt.HintLevel}");

    public Task PublishXpAwardedAsync(string studentId, XpAwarded_V1 evt) =>
        PublishSafe(NatsSubjects.StudentEvent(studentId, "xp_awarded"), evt, $"{studentId}-xp-{evt.TotalXp}");

    public Task PublishStreakUpdatedAsync(string studentId, StreakUpdated_V1 evt) =>
        PublishSafe(NatsSubjects.StudentEvent(studentId, "streak_updated"), evt, $"{studentId}-streak-{evt.CurrentStreak}-{evt.LastActivityDate.Ticks}");

    public Task PublishStagnationDetectedAsync(string studentId, StagnationDetected_V1 evt) =>
        PublishSafe(NatsSubjects.StudentEvent(studentId, "stagnation_detected"), evt, $"{evt.ConceptId}-stag-{DateTimeOffset.UtcNow.Ticks}");

    public Task PublishMethodologySwitchedAsync(string studentId, MethodologySwitched_V1 evt) =>
        PublishSafe(NatsSubjects.StudentEvent(studentId, "methodology_switched"), evt, $"{studentId}-meth-{evt.Timestamp.Ticks}");

    public Task PublishTutoringStartedAsync(string studentId, TutoringSessionStarted_V1 evt) =>
        PublishSafe(NatsSubjects.StudentEvent(studentId, "tutoring_started"), evt, evt.TutoringSessionId);

    public Task PublishTutoringMessageAsync(string studentId, TutoringMessageSent_V1 evt) =>
        PublishSafe(NatsSubjects.StudentEvent(studentId, "tutor_message"), new
        {
            evt.SessionId,
            evt.TutoringSessionId,
            evt.TurnNumber,
            evt.Role,
            MessagePreview = evt.MessagePreview.Length > 100 ? evt.MessagePreview[..100] : evt.MessagePreview,
            evt.SourceCount,
            evt.Timestamp
        }, $"{evt.TutoringSessionId}-turn-{evt.TurnNumber}");

    public Task PublishTutoringEndedAsync(string studentId, TutoringSessionEnded_V1 evt) =>
        PublishSafe(NatsSubjects.StudentEvent(studentId, "tutoring_ended"), evt, evt.TutoringSessionId);

    private async Task PublishSafe<T>(string subject, T payload, string deduplicationId)
    {
        try
        {
            var data = JsonSerializer.SerializeToUtf8Bytes(payload, _jsonOpts);

            var headers = new NatsHeaders
            {
                ["Nats-Msg-Id"] = deduplicationId,
                ["Cena-Schema-Version"] = "1",
            };

            await _nats.PublishAsync(subject, data, headers: headers);

            Published.Add(1, new KeyValuePair<string, object?>("event_type",
                subject[(subject.LastIndexOf('.') + 1)..]));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Session NATS publish failed: subject={Subject}, dedup={DeduplicationId}",
                subject, deduplicationId);

            Failures.Add(1, new KeyValuePair<string, object?>("event_type",
                subject[(subject.LastIndexOf('.') + 1)..]));
            // Do NOT rethrow — Marten is the source of truth
        }
    }
}
