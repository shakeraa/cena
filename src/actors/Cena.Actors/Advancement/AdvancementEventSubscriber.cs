// =============================================================================
// Cena Platform — Advancement Event Subscriber (RDY-061 Phase 2)
//
// Hosted service running inside the Actor Host. Subscribes to
// ConceptMastered events on NATS and calls StudentAdvancementService
// to cascade chapter transitions. Decoupled from StudentActor so the
// mastery engine's write path isn't widened.
//
// Subjects:
//   cena.events.student.{studentId}.concept_mastered_v2
//
// Idempotency: the service's ApplyConceptMasteryAsync is safe to call
// repeatedly — events are only emitted when state actually changes.
// =============================================================================

using System.Text.Json;
using Cena.Actors.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Cena.Actors.Advancement;

public sealed class AdvancementEventSubscriber : BackgroundService
{
    private readonly INatsConnection _nats;
    private readonly IStudentAdvancementService _advancement;
    private readonly ILogger<AdvancementEventSubscriber> _logger;
    private const string Subject = "cena.events.student.*.concept_mastered_v2";

    public AdvancementEventSubscriber(
        INatsConnection nats,
        IStudentAdvancementService advancement,
        ILogger<AdvancementEventSubscriber> logger)
    {
        _nats = nats;
        _advancement = advancement;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[ADVANCEMENT_SUB] subscribing to {Subject}", Subject);
        try
        {
            await foreach (var msg in _nats.SubscribeAsync<string>(Subject, cancellationToken: stoppingToken))
            {
                try
                {
                    if (string.IsNullOrEmpty(msg.Data)) continue;
                    var payload = JsonSerializer.Deserialize<ConceptMasteryPayload>(msg.Data,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (payload is null || string.IsNullOrEmpty(payload.StudentId)
                        || string.IsNullOrEmpty(payload.ConceptId)) continue;

                    // TrackId may ship via enrollmentId — we map enrollmentId → trackId
                    // via the EnrollmentDocument lookup inside ApplyConceptMasteryAsync
                    // if / when the payload omits trackId. The emitter in recent
                    // versions includes it directly.
                    var trackId = payload.TrackId ?? payload.EnrollmentId ?? string.Empty;
                    if (string.IsNullOrEmpty(trackId)) continue;

                    await _advancement.ApplyConceptMasteryAsync(
                        payload.StudentId, trackId, payload.ConceptId,
                        payload.MasteredAt ?? DateTimeOffset.UtcNow, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "[ADVANCEMENT_SUB] payload handling failed — dropping message");
                }
            }
        }
        catch (OperationCanceledException)
        {
            /* normal shutdown */
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ADVANCEMENT_SUB] subscriber exited unexpectedly");
        }
    }

    private sealed record ConceptMasteryPayload(
        string StudentId,
        string ConceptId,
        string? TrackId,
        string? EnrollmentId,
        DateTimeOffset? MasteredAt);
}
