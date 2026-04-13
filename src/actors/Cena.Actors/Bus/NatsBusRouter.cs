// =============================================================================
// Cena Platform -- NATS Bus Router (Hosted Service)
// Subscribes to NATS command subjects and routes messages to Proto.Actor cluster.
// Publishes actor events back to NATS for downstream consumers.
// =============================================================================

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Cena.Actors.Infrastructure;
using Cena.Actors.Sessions;
using Cena.Actors.Students;
using Cena.Infrastructure.Tracing;
using Marten;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using Proto;
using Proto.Cluster;
using StackExchange.Redis;

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
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<NatsBusRouter> _logger;
    private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // INF-018: Backpressure controls
    private const int MaxConcurrentActivations = 50;
    private const int MaxRetryAttempts = 3;
    private static readonly TimeSpan ActorRequestTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan[] RetryDelays = [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8)
    ];

    private readonly SemaphoreSlim _activationGate = new(MaxConcurrentActivations, MaxConcurrentActivations);

    private long _commandsRouted;
    private long _eventsPublished;
    private long _sessionsStarted;
    private long _errorsCount;
    private long _retriesAttempted;
    private long _deadLettered;
    private long _accountBlocked; // LCM-001: commands rejected by Redis status gate
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
        IConnectionMultiplexer redis,
        ILogger<NatsBusRouter> logger)
    {
        _nats = nats;
        _actorSystem = actorSystem;
        _documentStore = documentStore;
        _redis = redis;
        _logger = logger;
    }

    public long CommandsRouted => Interlocked.Read(ref _commandsRouted);
    public long EventsPublished => Interlocked.Read(ref _eventsPublished);
    public long SessionsStarted => Interlocked.Read(ref _sessionsStarted);
    public long ErrorsCount => Interlocked.Read(ref _errorsCount);
    public long RetriesAttempted => Interlocked.Read(ref _retriesAttempted);
    public long DeadLettered => Interlocked.Read(ref _deadLettered);
    public long AccountBlocked => Interlocked.Read(ref _accountBlocked);
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
            SubscribeAndRoute<BusResumeSession>(NatsSubjects.SessionResume, HandleResumeSession, stoppingToken),
            SubscribeAndRoute<BusConceptAttempt>(NatsSubjects.ConceptAttempt, HandleConceptAttempt, stoppingToken),
            SubscribeAndRoute<BusMethodologySwitch>(NatsSubjects.MethodologySwitch, HandleMethodologySwitch, stoppingToken),
            SubscribeAndRoute<BusAddAnnotation>(NatsSubjects.Annotation, HandleAnnotation, stoppingToken),
            SubscribeAndReply<BusGetSessionSnapshot, SessionSnapshotResponse>(NatsSubjects.SessionSnapshotRequest, HandleGetSessionSnapshot, stoppingToken),
            SubscribeAccountStatusChanges(stoppingToken),
            LogStats(stoppingToken)
        };

        _logger.LogInformation("NatsBusRouter ready — listening on 6 command subjects + account status + session snapshot request/reply");

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
                        // INF-020: Extract W3C trace context from NATS headers so the
                        // processing span becomes a child of the publisher's trace.
                        using var traceActivity = NatsTracePropagation.ExtractTraceContext(
                            msg.Headers, $"NatsBusRouter.Receive {subject}");
                        traceActivity?.SetTag("messaging.system", "nats");
                        traceActivity?.SetTag("messaging.destination", subject);

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

    /// <summary>
    /// PWA-BE-001: Request/reply subscription for session snapshot queries.
    /// Reads the request, routes to the actor, and publishes the response to the reply subject.
    /// </summary>
    private async Task SubscribeAndReply<TRequest, TResponse>(
        string subject,
        Func<BusEnvelope<TRequest>, CancellationToken, Task<TResponse>> handler,
        CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("NatsBusRouter subscribing to request/reply {Subject}...", subject);

            await foreach (var msg in _nats.SubscribeAsync<byte[]>(subject, cancellationToken: ct))
            {
                try
                {
                    if (msg.Data is null || msg.Data.Length == 0)
                    {
                        _logger.LogWarning("Empty message on {Subject}", subject);
                        continue;
                    }

                    var envelope = JsonSerializer.Deserialize<BusEnvelope<TRequest>>(msg.Data, _jsonOpts);
                    if (envelope?.Payload is null)
                    {
                        _logger.LogWarning("Deserialization returned null for message on {Subject}", subject);
                        continue;
                    }

                    var response = await handler(envelope, ct);

                    if (!string.IsNullOrEmpty(msg.ReplyTo))
                    {
                        var responseEnvelope = BusEnvelope<TResponse>.Create(msg.ReplyTo, response, "actor-host");
                        var responseBytes = JsonSerializer.SerializeToUtf8Bytes(responseEnvelope, _jsonOpts);
                        await _nats.PublishAsync(msg.ReplyTo, responseBytes);
                    }
                    else
                    {
                        _logger.LogWarning("Request on {Subject} has no ReplyTo subject", subject);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize message on {Subject}", subject);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling request/reply on {Subject}", subject);
                    // Client will treat timeout as session_expired / session_not_found
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

    // ── LCM-001: Account Status Gate ──

    /// <summary>
    /// Checks Redis for account status before routing a command.
    /// Returns true if the student is blocked (suspended, locked, frozen, pending_delete).
    /// Redis miss = assume active (fail-open at gate; actor is the backstop).
    /// </summary>
    private async Task<bool> IsAccountBlocked(string studentId)
    {
        try
        {
            var db = _redis.GetDatabase();
            var status = await db.StringGetAsync($"account_status:{studentId}");
            if (status.IsNullOrEmpty) return false;

            var statusStr = status.ToString();
            return statusStr is "suspended" or "locked" or "frozen" or "pending_delete";
        }
        catch (Exception ex)
        {
            // Redis failure = fail-open at gate layer (actor is the backstop)
            _logger.LogWarning(ex, "Redis status gate check failed for {StudentId}, allowing through", studentId);
            return false;
        }
    }

    /// <summary>
    /// Subscribes to account status change events and forwards to the affected StudentActor.
    /// This ensures warm actors receive status updates even though the Redis gate catches new commands.
    /// </summary>
    private async Task SubscribeAccountStatusChanges(CancellationToken ct)
    {
        try
        {
            await foreach (var msg in _nats.SubscribeAsync<byte[]>(NatsSubjects.AccountStatusChanged, cancellationToken: ct))
            {
                try
                {
                    var rawData = msg.Data;
                    if (rawData is null || rawData.Length == 0) continue;

                    var envelope = JsonSerializer.Deserialize<BusEnvelope<BusAccountStatusChanged>>(rawData, _jsonOpts);
                    if (envelope?.Payload is not { } payload) continue;

                    // SEC-004: Validate before forwarding to actor
                    if (!BusMessageValidator.ValidateEnvelope(envelope).LogIfRejected(_logger, NatsSubjects.AccountStatusChanged, envelope.MessageId))
                        continue;
                    if (!BusMessageValidator.Validate(payload).LogIfRejected(_logger, NatsSubjects.AccountStatusChanged, envelope.MessageId))
                        continue;

                    if (!Enum.TryParse<AccountStatus>(payload.NewStatus, true, out var status))
                    {
                        _logger.LogWarning("Unknown account status: {Status}", payload.NewStatus);
                        continue;
                    }

                    // Forward to the StudentActor so it can update in-memory state
                    var actorMsg = new AccountStatusChanged(
                        payload.StudentId, status, payload.Reason, payload.ChangedBy, payload.ChangedAt);

                    try
                    {
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        cts.CancelAfter(TimeSpan.FromSeconds(10));
                        await _actorSystem.Cluster()
                            .RequestAsync<ActorResult>(payload.StudentId, "student", actorMsg, cts.Token);
                    }
                    catch (Exception ex)
                    {
                        // Non-fatal: actor may not be active (which is fine — Redis gate handles it)
                        _logger.LogDebug(ex, "Could not deliver AccountStatusChanged to actor {StudentId}", payload.StudentId);
                    }

                    _logger.LogInformation("Account status changed: {StudentId} → {Status} by {ChangedBy}",
                        payload.StudentId, payload.NewStatus, payload.ChangedBy);
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize account status change event");
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    // ── Command Handlers — route to Proto.Actor virtual actors ──

    /// <summary>
    /// Calculates an adaptive timeout based on current error rate.
    /// When errors are high, cold-start contention is likely — extend timeouts
    /// to avoid churn (Fortnite RES-009 pattern).
    /// </summary>
    private TimeSpan GetAdaptiveTimeout()
    {
        var errors = Interlocked.Read(ref _errorsCount);
        var routed = Interlocked.Read(ref _commandsRouted);
        var total = errors + routed;
        if (total < 10) return ActorRequestTimeout;

        var errorRate = (double)errors / total;
        var health = errorRate switch
        {
            < 0.05 => SystemHealthLevel.Healthy,
            < 0.15 => SystemHealthLevel.Degraded,
            < 0.30 => SystemHealthLevel.Critical,
            _      => SystemHealthLevel.Emergency
        };
        return AdaptiveTimeout.Calculate(ActorRequestTimeout, health);
    }

    private async Task HandleStartSession(BusEnvelope<BusStartSession> env, CancellationToken ct)
    {
        var p = env.Payload;

        // SEC-004: Validate envelope and payload before processing
        if (!BusMessageValidator.ValidateEnvelope(env).LogIfRejected(_logger, NatsSubjects.SessionStart, env.MessageId))
        { RecordError("validation", NatsSubjects.SessionStart, "Invalid envelope", null); return; }
        if (!BusMessageValidator.Validate(p).LogIfRejected(_logger, NatsSubjects.SessionStart, env.MessageId))
        { RecordError("validation", NatsSubjects.SessionStart, "Invalid payload", p.StudentId); return; }

        // LCM-001: Redis status gate — reject commands for blocked accounts
        if (await IsAccountBlocked(p.StudentId))
        {
            Interlocked.Increment(ref _accountBlocked);
            RecordError("account_blocked", NatsSubjects.SessionStart, "Account is not active", p.StudentId);
            return;
        }

        var cmd = new StartSession(
            p.StudentId, p.SubjectId, p.ConceptId,
            p.DeviceType, p.AppVersion, p.ClientTimestamp, IsOffline: false,
            SchoolId: p.SchoolId ?? env.SchoolId); // REV-014: prefer payload field, fall back to envelope

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(GetAdaptiveTimeout());
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

        // SEC-004: Validate before routing
        if (!BusMessageValidator.ValidateEnvelope(env).LogIfRejected(_logger, NatsSubjects.SessionEnd, env.MessageId))
        { RecordError("validation", NatsSubjects.SessionEnd, "Invalid envelope", null); return; }
        if (!BusMessageValidator.Validate(p).LogIfRejected(_logger, NatsSubjects.SessionEnd, env.MessageId))
        { RecordError("validation", NatsSubjects.SessionEnd, "Invalid payload", p.StudentId); return; }

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

    private async Task HandleResumeSession(BusEnvelope<BusResumeSession> env, CancellationToken ct)
    {
        var p = env.Payload;

        // SEC-004: Validate before routing
        if (!BusMessageValidator.ValidateEnvelope(env).LogIfRejected(_logger, NatsSubjects.SessionResume, env.MessageId))
        { RecordError("validation", NatsSubjects.SessionResume, "Invalid envelope", null); return; }
        if (!BusMessageValidator.Validate(p).LogIfRejected(_logger, NatsSubjects.SessionResume, env.MessageId))
        { RecordError("validation", NatsSubjects.SessionResume, "Invalid payload", p.StudentId); return; }

        var cmd = new ResumeSession(p.StudentId, p.SessionId);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(GetAdaptiveTimeout());
        var result = await _actorSystem.Cluster()
            .RequestAsync<ActorResult<ResumeSessionResponse>>(p.StudentId, "student", cmd, cts.Token);

        if (result?.Success == true)
        {
            _actorStats.AddOrUpdate(p.StudentId,
                new ActorLiveStats { StudentId = p.StudentId, SessionId = p.SessionId, MessagesProcessed = 1 },
                (_, s) => { s.SessionId = p.SessionId; s.MessagesProcessed++; s.LastActivity = DateTimeOffset.UtcNow; s.Status = "active"; return s; });
            _logger.LogInformation("ResumeSession succeeded for {StudentId}, session {SessionId}", p.StudentId, p.SessionId);
        }
        else
        {
            _logger.LogWarning("ResumeSession failed for {StudentId}/{SessionId}: {Error}", p.StudentId, p.SessionId, result?.ErrorMessage ?? "null response");
        }
    }

    private async Task HandleConceptAttempt(BusEnvelope<BusConceptAttempt> env, CancellationToken ct)
    {
        var p = env.Payload;

        // SEC-004: Validate before routing
        if (!BusMessageValidator.ValidateEnvelope(env).LogIfRejected(_logger, NatsSubjects.ConceptAttempt, env.MessageId))
        { RecordError("validation", NatsSubjects.ConceptAttempt, "Invalid envelope", null); return; }
        if (!BusMessageValidator.Validate(p).LogIfRejected(_logger, NatsSubjects.ConceptAttempt, env.MessageId))
        { RecordError("validation", NatsSubjects.ConceptAttempt, "Invalid payload", p.StudentId); return; }

        // LCM-001: Redis status gate
        if (await IsAccountBlocked(p.StudentId))
        {
            Interlocked.Increment(ref _accountBlocked);
            RecordError("account_blocked", NatsSubjects.ConceptAttempt, "Account is not active", p.StudentId);
            return;
        }

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

        // SEC-004: Validate before routing
        if (!BusMessageValidator.ValidateEnvelope(env).LogIfRejected(_logger, NatsSubjects.Annotation, env.MessageId))
        { RecordError("validation", NatsSubjects.Annotation, "Invalid envelope", null); return; }
        if (!BusMessageValidator.Validate(p).LogIfRejected(_logger, NatsSubjects.Annotation, env.MessageId))
        { RecordError("validation", NatsSubjects.Annotation, "Invalid payload", p.StudentId); return; }

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

        // SEC-004: Validate before routing
        if (!BusMessageValidator.ValidateEnvelope(env).LogIfRejected(_logger, NatsSubjects.MethodologySwitch, env.MessageId))
        { RecordError("validation", NatsSubjects.MethodologySwitch, "Invalid envelope", null); return; }
        if (!BusMessageValidator.Validate(p).LogIfRejected(_logger, NatsSubjects.MethodologySwitch, env.MessageId))
        { RecordError("validation", NatsSubjects.MethodologySwitch, "Invalid payload", p.StudentId); return; }

        var cmd = new SwitchMethodology(p.StudentId, p.StudentId, p.ToMethodology);

        using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts2.CancelAfter(ActorRequestTimeout);
        await _actorSystem.Cluster()
            .RequestAsync<ActorResult>(p.StudentId, "student", cmd, cts2.Token);

        await PublishEventAsync(NatsSubjects.EventMethodologySwitched,
            new { p.StudentId, p.SessionId, p.FromMethodology, p.ToMethodology, p.Reason,
                  Timestamp = DateTimeOffset.UtcNow });
    }

    // ── Session Snapshot Request/Reply (PWA-BE-001) ──

    private async Task<SessionSnapshotResponse> HandleGetSessionSnapshot(BusEnvelope<BusGetSessionSnapshot> env, CancellationToken ct)
    {
        var p = env.Payload;

        if (!BusMessageValidator.ValidateEnvelope(env).LogIfRejected(_logger, NatsSubjects.SessionSnapshotRequest, env.MessageId))
        { return new SessionSnapshotResponse(p.SessionId, 0, null, new(), "full", new(), DateTimeOffset.UtcNow, 0, Error: "session_not_found"); }
        if (!BusMessageValidator.Validate(p).LogIfRejected(_logger, NatsSubjects.SessionSnapshotRequest, env.MessageId))
        { return new SessionSnapshotResponse(p.SessionId, 0, null, new(), "full", new(), DateTimeOffset.UtcNow, 0, Error: "session_not_found"); }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(GetAdaptiveTimeout());

        try
        {
            var result = await _actorSystem.Cluster()
                .RequestAsync<SessionSnapshotResponse>(p.StudentId, "student", new GetSessionSnapshot(p.StudentId, p.SessionId), cts.Token);

            return result;
        }
        catch (OperationCanceledException)
        {
            return new SessionSnapshotResponse(p.SessionId, 0, null, new(), "full", new(), DateTimeOffset.UtcNow, 0, Error: "session_expired");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get session snapshot for {StudentId}/{SessionId}", p.StudentId, p.SessionId);
            return new SessionSnapshotResponse(p.SessionId, 0, null, new(), "full", new(), DateTimeOffset.UtcNow, 0, Error: "session_not_found");
        }
    }

    // ── Event Publishing ──

    public async Task PublishEventAsync<T>(string subject, T payload)
    {
        var envelope = BusEnvelope<T>.Create(subject, payload, "actor-host");
        var json = JsonSerializer.Serialize(envelope, _jsonOpts);

        // INF-020: Propagate W3C trace context on outgoing events
        var headers = NatsTracePropagation.InjectTraceContext();

        await _nats.PublishAsync(subject, json, headers: headers);
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
                var gateSlots = _activationGate.CurrentCount;
                var backpressureEngaged = gateSlots == 0;
                _logger.LogInformation(
                    "NatsBusRouter stats: {Commands} routed, {Events} events, {Errors} errors, {Retries} retries, {DeadLettered} dead-lettered, {Gate}/{Max} gate slots free, backpressure={Backpressure}",
                    _commandsRouted, _eventsPublished, _errorsCount,
                    _retriesAttempted, _deadLettered,
                    gateSlots, MaxConcurrentActivations, backpressureEngaged);
            }
        }
    }
}
