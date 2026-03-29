// =============================================================================
// Cena Platform -- NATS Bus Router (Hosted Service)
// Subscribes to NATS command subjects and routes messages to Proto.Actor cluster.
// Publishes actor events back to NATS for downstream consumers.
// =============================================================================

using System.Collections.Concurrent;
using System.Text.Json;
using Cena.Actors.Infrastructure;
using Cena.Actors.Students;
using Marten;
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
    private readonly IDocumentStore _documentStore;
    private readonly ILogger<NatsBusRouter> _logger;
    private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private const int MaxConcurrentActivations = 50;
    private const int MaxRetryAttempts = 3;
    private static readonly TimeSpan ActorRequestTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan[] RetryDelays = [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4)
    ];

    private readonly SemaphoreSlim _activationGate = new(MaxConcurrentActivations, MaxConcurrentActivations);

    private long _commandsRouted;
    private long _eventsPublished;
    private long _sessionsStarted;
    private long _errorsCount;
    private long _retriesAttempted;
    private long _deadLettered;
    private readonly ConcurrentDictionary<string, ActorLiveStats> _actorStats = new();
    private readonly ConcurrentDictionary<string, long> _errorsByCategory = new();
    private readonly ConcurrentQueue<ErrorEntry> _recentErrors = new();
    private const int MaxRecentErrors = 250;

    public sealed record ErrorEntry(
        DateTimeOffset Timestamp,
        string Category,
        string Subject,
        string Message,
        string? StudentId);

    public sealed record ActorLiveStats
    {
        public string StudentId { get; init; } = "";
        public string? SessionId { get; set; }
        public long MessagesProcessed { get; set; }
        public long CorrectAttempts { get; set; }
        public long TotalAttempts { get; set; }
        public DateTimeOffset LastActivity { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset ActivatedAt { get; init; } = DateTimeOffset.UtcNow;
        public string Status { get; set; } = "active";
    }

    public NatsBusRouter(
        INatsConnection nats,
        ActorSystem actorSystem,
        IDocumentStore documentStore,
        ILogger<NatsBusRouter> logger)
    {
        _nats = nats;
        _actorSystem = actorSystem;
        _documentStore = documentStore;
        _logger = logger;
    }

    public long CommandsRouted => Interlocked.Read(ref _commandsRouted);
    public long EventsPublished => Interlocked.Read(ref _eventsPublished);
    public long SessionsStarted => Interlocked.Read(ref _sessionsStarted);
    public long ErrorsCount => Interlocked.Read(ref _errorsCount);
    public long RetriesAttempted => Interlocked.Read(ref _retriesAttempted);
    public long DeadLettered => Interlocked.Read(ref _deadLettered);
    public IReadOnlyDictionary<string, ActorLiveStats> ActiveActors => _actorStats;
    public IReadOnlyDictionary<string, long> ErrorsByCategory => _errorsByCategory;
    public IReadOnlyList<ErrorEntry> RecentErrors => _recentErrors
        .Reverse().Take(50).ToList();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for Proto.Actor cluster to be fully started before processing messages
        _logger.LogInformation("NatsBusRouter waiting for cluster to be ready...");
        while (_actorSystem.Cluster()?.MemberList?.GetMembers()?.Count is null or 0)
        {
            if (stoppingToken.IsCancellationRequested) return;
            await Task.Delay(250, stoppingToken);
        }
        _logger.LogInformation("NatsBusRouter starting — subscribing to command subjects...");

        // Ensure NATS connection is fully established before subscribing
        if (_nats is NATS.Client.Core.NatsConnection conn)
        {
            await conn.ConnectAsync();
            _logger.LogInformation("NatsBusRouter NATS connection established");
        }

        var tasks = new[]
        {
            SubscribeAndRoute<BusStartSession>(NatsSubjects.SessionStart, HandleStartSession, stoppingToken),
            SubscribeAndRoute<BusEndSession>(NatsSubjects.SessionEnd, HandleEndSession, stoppingToken),
            SubscribeAndRoute<BusConceptAttempt>(NatsSubjects.ConceptAttempt, HandleConceptAttempt, stoppingToken),
            SubscribeAndRoute<BusMethodologySwitch>(NatsSubjects.MethodologySwitch, HandleMethodologySwitch, stoppingToken),
            SubscribeAndRoute<BusAddAnnotation>(NatsSubjects.Annotation, HandleAnnotation, stoppingToken),
            LogStats(stoppingToken)
        };

        _logger.LogInformation("NatsBusRouter ready — listening on 5 command subjects");

        await Task.WhenAll(tasks);
    }

    private async Task SubscribeAndRoute<T>(
        string subject,
        Func<BusEnvelope<T>, CancellationToken, Task> handler,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("NatsBusRouter subscribing to {Subject}...", subject);

            // INF-018: Backpressure is enforced by the _activationGate semaphore (MaxConcurrentActivations=50).
            // If the gate is full, SubscribeAndRoute blocks on _activationGate.WaitAsync, which naturally
            // applies backpressure to the async enumerable — NATS.Net 2.x pauses reading from the socket
            // when the consumer is slower than the producer. No explicit buffer limit needed.
            await foreach (var msg in _nats.SubscribeAsync<byte[]>(subject, cancellationToken: ct))
            {
                try
                {
                    var rawData = msg.Data;
                    if (rawData is null || rawData.Length == 0)
                    {
                        _logger.LogWarning("Empty message on {Subject}", subject);
                        continue;
                    }
                    var envelope = JsonSerializer.Deserialize<BusEnvelope<T>>(rawData, _jsonOpts);
                    if (envelope is not null && envelope.Payload is not null)
                    {
                        await RouteWithRetry(subject, envelope, rawData, handler, ct);
                    }
                    else
                    {
                        _logger.LogWarning("Deserialization returned null for message on {Subject}: {Preview}",
                            subject, System.Text.Encoding.UTF8.GetString(rawData[..Math.Min(200, rawData.Length)]));
                    }
                }
                catch (JsonException ex)
                {
                    RecordError("deserialization", subject, ex.Message, null);
                    _logger.LogWarning(ex, "Failed to deserialize message on {Subject}", subject);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is not TimeoutException)
                {
                    var category = ex switch
                    {
                        InvalidOperationException => "activation",
                        _ => "unknown"
                    };
                    RecordError(category, subject, ex.Message, null);
                    _logger.LogError(ex, "Error routing message from {Subject}", subject);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task RouteWithRetry<T>(
        string subject,
        BusEnvelope<T> envelope,
        byte[] rawData,
        Func<BusEnvelope<T>, CancellationToken, Task> handler,
        CancellationToken ct)
    {
        await _activationGate.WaitAsync(ct);
        try
        {
            for (var attempt = 0; attempt <= MaxRetryAttempts; attempt++)
            {
                try
                {
                    await handler(envelope, ct);
                    Interlocked.Increment(ref _commandsRouted);
                    return;
                }
                catch (Exception ex) when (IsRetryableTimeout(ex, ct) && attempt < MaxRetryAttempts)
                {
                    Interlocked.Increment(ref _retriesAttempted);
                    _logger.LogWarning(
                        "Timeout on {Subject} (attempt {Attempt}/{Max}), retrying in {Delay}s...",
                        subject, attempt + 1, MaxRetryAttempts, RetryDelays[attempt].TotalSeconds);
                    await Task.Delay(RetryDelays[attempt], ct);
                }
                catch (Exception ex) when (IsRetryableTimeout(ex, ct))
                {
                    // All retries exhausted — send to dead-letter
                    Interlocked.Increment(ref _deadLettered);
                    RecordError("timeout", subject, $"Exhausted {MaxRetryAttempts} retries: {ex.Message}", null);
                    _logger.LogError(
                        "Dead-lettering message on {Subject} after {Max} retries (msgId={MsgId})",
                        subject, MaxRetryAttempts, envelope.MessageId);

                    await PublishDeadLetterAsync(subject, rawData);
                }
            }
        }
        finally
        {
            _activationGate.Release();
        }
    }

    private async Task PublishDeadLetterAsync(string originalSubject, byte[] rawData)
    {
        // 1. Persist to Marten so it appears in the Admin DLQ UI
        try
        {
            await using var session = _documentStore.LightweightSession();
            session.Store(new NatsOutboxDeadLetter
            {
                EventSequence = -1, // Not from Marten event store
                StreamId = originalSubject,
                EventType = originalSubject,
                RetryCount = MaxRetryAttempts,
                DeadLetteredAt = DateTimeOffset.UtcNow,
            });
            await session.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist dead-letter to Marten for {Subject}", originalSubject);
        }

        // 2. Publish to JetStream DEAD_LETTER stream for durable replay
        try
        {
            var dlEnvelope = new
            {
                OriginalSubject = originalSubject,
                FailedAt = DateTimeOffset.UtcNow,
                Retries = MaxRetryAttempts,
                Payload = Convert.ToBase64String(rawData)
            };
            var json = JsonSerializer.Serialize(dlEnvelope, _jsonOpts);
            await _nats.PublishAsync(NatsSubjects.DeadLetter, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish dead-letter to NATS for {Subject}", originalSubject);
        }
    }

    // ── Command Handlers — route to Proto.Actor virtual actors ──

    private async Task HandleStartSession(BusEnvelope<BusStartSession> env, CancellationToken ct)
    {
        var p = env.Payload;
        var cmd = new StartSession(
            p.StudentId, p.SubjectId, p.ConceptId,
            p.DeviceType, p.AppVersion, p.ClientTimestamp, IsOffline: false,
            SchoolId: p.SchoolId ?? env.SchoolId); // REV-014: prefer payload field, fall back to envelope

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ActorRequestTimeout);
        var result = await _actorSystem.Cluster()
            .RequestAsync<ActorResult<StartSessionResponse>>(p.StudentId, "student", cmd, cts.Token);

        if (result?.Success == true && result.Data is { } response)
        {
            Interlocked.Increment(ref _sessionsStarted);

            _actorStats.AddOrUpdate(p.StudentId,
                new ActorLiveStats { StudentId = p.StudentId, SessionId = response.SessionId, MessagesProcessed = 1 },
                (_, s) => { s.SessionId = response.SessionId; s.MessagesProcessed++; s.LastActivity = DateTimeOffset.UtcNow; s.Status = "active"; return s; });

            await PublishEventAsync(NatsSubjects.EventSessionStarted,
                new BusSessionStartedEvent(
                    p.StudentId, response.SessionId, p.SubjectId,
                    response.StartingConceptId, response.ActiveMethodology.ToString(),
                    DateTimeOffset.UtcNow));
        }
        else
        {
            _logger.LogWarning("StartSession failed for {StudentId}: {Error}", p.StudentId, result?.ErrorMessage ?? "null response");
        }
    }

    private async Task HandleEndSession(BusEnvelope<BusEndSession> env, CancellationToken ct)
    {
        var p = env.Payload;
        var reason = Enum.TryParse<SessionEndReason>(p.Reason, true, out var r)
            ? r : SessionEndReason.Completed;
        var cmd = new EndSession(p.StudentId, p.SessionId, reason);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ActorRequestTimeout);
        await _actorSystem.Cluster()
            .RequestAsync<ActorResult>(p.StudentId, "student", cmd, cts.Token);

        // Mark actor as idle when session ends
        if (_actorStats.TryGetValue(p.StudentId, out var stat))
        {
            stat.Status = "idle";
            stat.LastActivity = DateTimeOffset.UtcNow;
        }

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

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(ActorRequestTimeout);
        var result = await _actorSystem.Cluster()
            .RequestAsync<ActorResult<EvaluateAnswerResponse>>(p.StudentId, "student", cmd, cts.Token);

        _actorStats.AddOrUpdate(p.StudentId,
            new ActorLiveStats { StudentId = p.StudentId, SessionId = p.SessionId, MessagesProcessed = 1, TotalAttempts = 1, CorrectAttempts = p.Answer == "correct" ? 1 : 0 },
            (_, s) => { s.MessagesProcessed++; s.TotalAttempts++; if (p.Answer == "correct") s.CorrectAttempts++; s.LastActivity = DateTimeOffset.UtcNow; return s; });

        // Publish attempt event
        await PublishEventAsync(NatsSubjects.EventConceptAttempted,
            new BusConceptAttemptedEvent(
                p.StudentId, p.SessionId, p.ConceptId,
                p.Answer == "correct", 0, 0, // mastery levels filled by actor internally
                p.ResponseTimeMs, null, DateTimeOffset.UtcNow));
    }

    private async Task HandleAnnotation(BusEnvelope<BusAddAnnotation> env, CancellationToken ct)
    {
        var p = env.Payload;
        var kind = Enum.TryParse<AnnotationType>(p.Kind, true, out var at)
            ? at : AnnotationType.Note;
        var cmd = new AddAnnotation(p.StudentId, p.ConceptId, p.SessionId, p.Text, kind);

        using var cts1 = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts1.CancelAfter(ActorRequestTimeout);
        await _actorSystem.Cluster()
            .RequestAsync<ActorResult>(p.StudentId, "student", cmd, cts1.Token);

        if (_actorStats.TryGetValue(p.StudentId, out var stat))
        {
            stat.MessagesProcessed++;
            stat.LastActivity = DateTimeOffset.UtcNow;
        }
    }

    private async Task HandleMethodologySwitch(BusEnvelope<BusMethodologySwitch> env, CancellationToken ct)
    {
        var p = env.Payload;
        var cmd = new SwitchMethodology(p.StudentId, p.StudentId, p.ToMethodology);

        using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts2.CancelAfter(ActorRequestTimeout);
        await _actorSystem.Cluster()
            .RequestAsync<ActorResult>(p.StudentId, "student", cmd, cts2.Token);

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

    /// <summary>
    /// Returns true if the exception is a retryable timeout — either a TimeoutException
    /// or an OperationCanceledException from our per-request CTS (not the global shutdown CT).
    /// </summary>
    private static bool IsRetryableTimeout(Exception ex, CancellationToken globalCt)
    {
        if (ex is TimeoutException) return true;
        // OperationCanceledException from our per-request CancellationTokenSource timeout,
        // NOT from the global shutdown token. Proto.Actor wraps timeouts as OCE.
        if (ex is OperationCanceledException && !globalCt.IsCancellationRequested) return true;
        return false;
    }

    internal void RecordError(string category, string subject, string message, string? studentId)
    {
        Interlocked.Increment(ref _errorsCount);
        _errorsByCategory.AddOrUpdate(category, 1, (_, c) => c + 1);

        _recentErrors.Enqueue(new ErrorEntry(DateTimeOffset.UtcNow, category, subject, message, studentId));
        while (_recentErrors.Count > MaxRecentErrors && _recentErrors.TryDequeue(out _)) { }
    }

    private async Task LogStats(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (_commandsRouted > 0)
            {
                _logger.LogInformation(
                    "NatsBusRouter stats: {Commands} routed, {Events} events, {Errors} errors, {Retries} retries, {DeadLettered} dead-lettered, {Gate}/{Max} gate slots free",
                    _commandsRouted, _eventsPublished, _errorsCount,
                    _retriesAttempted, _deadLettered,
                    _activationGate.CurrentCount, MaxConcurrentActivations);
            }
        }
    }
}
