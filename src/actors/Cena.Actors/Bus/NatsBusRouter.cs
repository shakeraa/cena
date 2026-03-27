// =============================================================================
// Cena Platform -- NATS Bus Router (Hosted Service)
// Subscribes to NATS command subjects and routes messages to Proto.Actor cluster.
// Publishes actor events back to NATS for downstream consumers.
// =============================================================================

using System.Text.Json;
using Cena.Actors.Students;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using Proto;
using Proto.Cluster;

namespace Cena.Actors.Bus;

/// <summary>
/// Background service that bridges NATS ↔ Proto.Actor.
/// Subscribes to command subjects, deserializes messages, and sends to virtual actors.
/// Also provides PublishEventAsync for actors to emit events on NATS.
/// </summary>
public sealed class NatsBusRouter : BackgroundService
{
    private readonly INatsConnection _nats;
    private readonly ActorSystem _actorSystem;
    private readonly ILogger<NatsBusRouter> _logger;
    private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private long _commandsRouted;
    private long _eventsPublished;

    public NatsBusRouter(
        INatsConnection nats,
        ActorSystem actorSystem,
        ILogger<NatsBusRouter> logger)
    {
        _nats = nats;
        _actorSystem = actorSystem;
        _logger = logger;
    }

    public long CommandsRouted => Interlocked.Read(ref _commandsRouted);
    public long EventsPublished => Interlocked.Read(ref _eventsPublished);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NatsBusRouter starting — subscribing to command subjects...");

        var tasks = new[]
        {
            SubscribeAndRoute<BusStartSession>(NatsSubjects.SessionStart, HandleStartSession, stoppingToken),
            SubscribeAndRoute<BusEndSession>(NatsSubjects.SessionEnd, HandleEndSession, stoppingToken),
            SubscribeAndRoute<BusConceptAttempt>(NatsSubjects.ConceptAttempt, HandleConceptAttempt, stoppingToken),
            SubscribeAndRoute<BusMethodologySwitch>(NatsSubjects.MethodologySwitch, HandleMethodologySwitch, stoppingToken),
            LogStats(stoppingToken)
        };

        _logger.LogInformation("NatsBusRouter ready — listening on 4 command subjects");

        await Task.WhenAll(tasks);
    }

    private async Task SubscribeAndRoute<T>(
        string subject,
        Func<BusEnvelope<T>, CancellationToken, Task> handler,
        CancellationToken ct)
    {
        try
        {
            await foreach (var msg in _nats.SubscribeAsync<string>(subject, cancellationToken: ct))
            {
                try
                {
                    var envelope = JsonSerializer.Deserialize<BusEnvelope<T>>(msg.Data!, _jsonOpts);
                    if (envelope is not null && envelope.Payload is not null)
                    {
                        await handler(envelope, ct);
                        Interlocked.Increment(ref _commandsRouted);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize message on {Subject}", subject);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error routing message from {Subject}", subject);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    // ── Command Handlers — route to Proto.Actor virtual actors ──

    private async Task HandleStartSession(BusEnvelope<BusStartSession> env, CancellationToken ct)
    {
        var p = env.Payload;
        var cmd = new StartSession(
            p.StudentId, p.SubjectId, p.ConceptId,
            p.DeviceType, p.AppVersion, p.ClientTimestamp, IsOffline: false);

        var response = await _actorSystem.Cluster()
            .RequestAsync<StartSessionResponse>(p.StudentId, "student", cmd, ct);

        if (response != null)
        {
            // Publish session started event back on NATS
            await PublishEventAsync(NatsSubjects.EventSessionStarted,
                new BusSessionStartedEvent(
                    p.StudentId, response.SessionId, p.SubjectId,
                    response.StartingConceptId, response.ActiveMethodology.ToString(),
                    DateTimeOffset.UtcNow));
        }
    }

    private async Task HandleEndSession(BusEnvelope<BusEndSession> env, CancellationToken ct)
    {
        var p = env.Payload;
        var reason = Enum.TryParse<SessionEndReason>(p.Reason, true, out var r)
            ? r : SessionEndReason.Completed;
        var cmd = new EndSession(p.StudentId, p.SessionId, reason);

        await _actorSystem.Cluster()
            .RequestAsync<object>(p.StudentId, "student", cmd, ct);

        await PublishEventAsync(NatsSubjects.EventSessionEnded,
            new BusSessionEndedEvent(
                p.StudentId, p.SessionId, p.Reason,
                0, 0, TimeSpan.Zero, DateTimeOffset.UtcNow));
    }

    private async Task HandleConceptAttempt(BusEnvelope<BusConceptAttempt> env, CancellationToken ct)
    {
        var p = env.Payload;
        var qType = Enum.TryParse<QuestionType>(p.QuestionType, true, out var qt)
            ? qt : QuestionType.MultipleChoice;

        var cmd = new AttemptConcept(
            p.StudentId, p.SessionId, p.ConceptId, p.QuestionId,
            qType, p.Answer, p.ResponseTimeMs, p.HintCountUsed,
            p.WasSkipped, p.BackspaceCount, p.AnswerChangeCount, WasOffline: false);

        await _actorSystem.Cluster()
            .RequestAsync<object>(p.StudentId, "student", cmd, ct);

        // Publish attempt event
        await PublishEventAsync(NatsSubjects.EventConceptAttempted,
            new BusConceptAttemptedEvent(
                p.StudentId, p.SessionId, p.ConceptId,
                p.Answer == "correct", 0, 0, // mastery levels filled by actor internally
                p.ResponseTimeMs, null, DateTimeOffset.UtcNow));
    }

    private async Task HandleMethodologySwitch(BusEnvelope<BusMethodologySwitch> env, CancellationToken ct)
    {
        var p = env.Payload;
        var cmd = new SwitchMethodology(p.StudentId, p.StudentId, p.ToMethodology);

        await _actorSystem.Cluster()
            .RequestAsync<object>(p.StudentId, "student", cmd, ct);

        await PublishEventAsync(NatsSubjects.EventMethodologySwitched,
            new { p.StudentId, p.SessionId, p.FromMethodology, p.ToMethodology, p.Reason,
                  Timestamp = DateTimeOffset.UtcNow });
    }

    // ── Event Publishing ──

    public async Task PublishEventAsync<T>(string subject, T payload)
    {
        var envelope = BusEnvelope<T>.Create(subject, payload, "actor-host");
        var json = JsonSerializer.Serialize(envelope, _jsonOpts);
        await _nats.PublishAsync(subject, json);
        Interlocked.Increment(ref _eventsPublished);
    }

    private async Task LogStats(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (_commandsRouted > 0)
            {
                _logger.LogInformation(
                    "NatsBusRouter stats: {Commands} commands routed, {Events} events published",
                    _commandsRouted, _eventsPublished);
            }
        }
    }
}
