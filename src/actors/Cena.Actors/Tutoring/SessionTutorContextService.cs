// =============================================================================
// Cena Platform — Session Tutor Context Service (prr-204)
//
// Redis + Marten implementation of ISessionTutorContextService. Resilience
// mirrors DailyTutorTimeBudget: Redis failures degrade gracefully to the
// live Marten path. The service stays well under 500 LOC by delegating
// the per-concern lookups to purpose-built helpers.
//
// Privacy / scope guarantees:
//   - Cache key:   "cena:tutor-ctx:{sessionId}"
//   - Cache TTL:   session-scoped (default 6h — longer than any realistic
//                  session, shorter than the 30-day ADR-0003 hard ceiling)
//   - Never reads from or writes to any student-profile store when assembling
//     the misconception tag; only the session stream + session projections.
//
// The NoTutorContextPersistenceTest architecture ratchet asserts at test-time
// that this file does not reference StudentState, StudentProfile, or any
// long-lived student-scoped store.
// =============================================================================

using System.Diagnostics.Metrics;
using System.Text.Json;
using Cena.Actors.Accommodations;
using Cena.Actors.Events;
using Cena.Actors.Projections;
using Cena.Actors.RateLimit;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Actors.Tutoring;

/// <summary>
/// Redis-cached, Marten-backed implementation of
/// <see cref="ISessionTutorContextService"/>. See class-level doc for
/// scope + resilience rationale.
/// </summary>
public sealed class SessionTutorContextService : ISessionTutorContextService
{
    internal const string CacheKeyPrefix = "cena:tutor-ctx";

    // Default TTL: longer than any realistic session but much shorter than
    // the ADR-0003 hard ceiling (30 days). Operators can shorten via
    // Cena:TutorContext:SessionTtlMinutes.
    private static readonly TimeSpan DefaultSessionTtl = TimeSpan.FromHours(6);

    private readonly IConnectionMultiplexer _redis;
    private readonly IDocumentStore _store;
    private readonly IAccommodationProfileService _accommodations;
    private readonly IDailyTutorTimeBudget _dailyBudget;
    private readonly ILogger<SessionTutorContextService> _logger;
    private readonly TimeSpan _sessionTtl;

    private readonly Counter<long> _cacheHits;
    private readonly Counter<long> _cacheMisses;
    private readonly Counter<long> _cacheErrors;
    private readonly Counter<long> _liveBuilds;

    // Ignoring-safe JSON options: camelCase to match the wire format of the
    // API DTO without pulling a separate contracts assembly into the cache
    // layer.
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public SessionTutorContextService(
        IConnectionMultiplexer redis,
        IDocumentStore store,
        IAccommodationProfileService accommodations,
        IDailyTutorTimeBudget dailyBudget,
        IConfiguration configuration,
        ILogger<SessionTutorContextService> logger,
        IMeterFactory meterFactory)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _accommodations = accommodations ?? throw new ArgumentNullException(nameof(accommodations));
        _dailyBudget = dailyBudget ?? throw new ArgumentNullException(nameof(dailyBudget));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var ttlMinutes = configuration.GetValue<int>(
            "Cena:TutorContext:SessionTtlMinutes", (int)DefaultSessionTtl.TotalMinutes);
        _sessionTtl = TimeSpan.FromMinutes(Math.Max(1, ttlMinutes));

        var meter = meterFactory.Create("Cena.Actors.SessionTutorContext", "1.0.0");
        _cacheHits = meter.CreateCounter<long>(
            "cena_tutor_context_cache_hits_total",
            description: "SessionTutorContext Redis cache hits (prr-204)");
        _cacheMisses = meter.CreateCounter<long>(
            "cena_tutor_context_cache_misses_total",
            description: "SessionTutorContext Redis cache misses (prr-204)");
        _cacheErrors = meter.CreateCounter<long>(
            "cena_tutor_context_cache_errors_total",
            description: "SessionTutorContext Redis errors, labeled by op=get|set|del (prr-204)");
        _liveBuilds = meter.CreateCounter<long>(
            "cena_tutor_context_live_builds_total",
            description: "SessionTutorContext Marten rebuilds (cache miss or Redis outage) (prr-204)");
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public async Task<SessionTutorContext?> GetAsync(
        string sessionId,
        string studentId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);

        // Fast path: Redis cache.
        var cached = await TryReadCacheAsync(sessionId, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            // Defensive ownership check — if the cache has a snapshot keyed by
            // sessionId but the student claim disagrees, fall through to the
            // live Marten path so the endpoint's subsequent ownership guard
            // fails authoritatively instead of leaking a cached snapshot.
            if (!string.Equals(cached.StudentId, studentId, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "[SIEM] Tutor context ownership mismatch for session {SessionId}; " +
                    "cached student {CachedStudent} vs requested {RequestedStudent} — falling back to live build",
                    sessionId, cached.StudentId, studentId);
            }
            else
            {
                _cacheHits.Add(1);
                return cached;
            }
        }

        _cacheMisses.Add(1);

        // Live build from Marten.
        var fresh = await BuildLiveAsync(sessionId, studentId, ct).ConfigureAwait(false);
        if (fresh is null) return null;

        // Write-through so the next request is a cache hit.
        await TryWriteCacheAsync(sessionId, fresh, ct).ConfigureAwait(false);
        return fresh;
    }

    public async Task PreSeedAsync(
        string sessionId,
        string studentId,
        string? instituteId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(studentId);

        var snapshot = await BuildLiveAsync(sessionId, studentId, ct).ConfigureAwait(false);
        if (snapshot is null)
        {
            // The session doc hasn't been written yet (race with the session-
            // start handler). Re-query once the projection is applied; the
            // first GetAsync will rebuild. Log-only — pre-seed is advisory.
            _logger.LogDebug(
                "Tutor context pre-seed skipped: session {SessionId} not yet in Marten", sessionId);
            return;
        }

        // If the caller supplied an authoritative institute id, ensure the
        // snapshot carries it. This is the one place where the endpoint's
        // tenant claim is the source of truth — the queue projection does
        // not store the institute id directly today.
        if (!string.Equals(snapshot.InstituteId, instituteId, StringComparison.Ordinal))
        {
            snapshot = snapshot with { InstituteId = instituteId };
        }

        await TryWriteCacheAsync(sessionId, snapshot, ct).ConfigureAwait(false);
    }

    public async Task InvalidateAsync(
        string sessionId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        try
        {
            var db = _redis.GetDatabase();
            await db.KeyDeleteAsync(BuildKey(sessionId)).WaitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _cacheErrors.Add(1, new KeyValuePair<string, object?>("op", "del"));
            _logger.LogWarning(ex,
                "Tutor context cache DEL failed for session {SessionId}; " +
                "TTL will clean up eventually", sessionId);
        }
    }

    // -------------------------------------------------------------------------
    // Live-build path — folds Marten projections into a context snapshot.
    // -------------------------------------------------------------------------

    internal async Task<SessionTutorContext?> BuildLiveAsync(
        string sessionId,
        string studentId,
        CancellationToken ct)
    {
        _liveBuilds.Add(1);

        await using var q = _store.QuerySession();

        var queue = await q.LoadAsync<LearningSessionQueueProjection>(sessionId, ct)
            .ConfigureAwait(false);
        if (queue is null) return null;

        // Defensive ownership guard: the endpoint already checks this, but a
        // service that depends only on the session id should still refuse to
        // return a foreign student's snapshot if the caller ever gets the
        // wiring wrong. Returning null is mapped to 404 upstream — indistinguishable
        // from "session not found" so we do not leak existence.
        if (!string.Equals(queue.StudentId, studentId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "[SIEM] Tutor context denied: session {SessionId} owner {Owner} != requested {Requested}",
                sessionId, queue.StudentId, studentId);
            return null;
        }

        var history = await q.LoadAsync<SessionAttemptHistoryDocument>(sessionId, ct)
            .ConfigureAwait(false);

        var active = await q.LoadAsync<ActiveSessionSnapshot>(studentId, ct)
            .ConfigureAwait(false);
        var startedAt = active?.StartedAt ?? queue.StartedAt;
        if (startedAt == default) startedAt = DateTime.UtcNow;

        // Accommodations fail-open — a lookup failure degrades to the
        // "no accommodations" baseline so the tutor context stays
        // available during an accommodations-service outage.
        SessionTutorAccommodationFlags accom;
        try
        {
            var profile = await _accommodations.GetCurrentAsync(studentId, ct)
                .ConfigureAwait(false);
            accom = new SessionTutorAccommodationFlags(
                LdAnxiousFriendly: profile.IsEnabled(AccommodationDimension.LdAnxiousFriendly),
                ExtendedTimeMultiplier: profile.ExtendedTimeMultiplier,
                DistractionReducedLayout: profile.DistractionReducedLayoutEnabled,
                TtsForProblemStatements: profile.TtsForProblemStatementsEnabled);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "Tutor context: accommodations lookup failed for {StudentId}; using defaults",
                studentId);
            accom = SessionTutorAccommodationFlags.None;
        }

        // DailyTutorTimeBudget is Redis-backed but independently fail-open:
        // a Redis outage returns an "allowed with full budget" result, which
        // is the right behaviour here (budget exhaustion is enforced by the
        // tutor turn endpoint, not by the context API).
        var budget = await _dailyBudget.CheckAsync(studentId, queue.SchoolIdOrNull(), ct)
            .ConfigureAwait(false);

        var currentRung = queue.CurrentQuestionId is { } curQ
            ? queue.LadderRungByQuestion.GetValueOrDefault(curQ, 0)
            : 0;
        var attemptPhase = ResolveAttemptPhase(queue, history);
        var lastMisconception = ExtractLastMisconceptionTag(history);
        var bucket = ResolveBktBucket(queue);

        var nowUtc = DateTimeOffset.UtcNow;
        var elapsedMinutes = Math.Max(
            0,
            (int)Math.Floor((nowUtc - new DateTimeOffset(startedAt, TimeSpan.Zero)).TotalMinutes));

        return new SessionTutorContext(
            SessionId: sessionId,
            StudentId: studentId,
            InstituteId: null, // pre-seed overrides when the endpoint supplies it
            CurrentQuestionId: queue.CurrentQuestionId,
            AnsweredCount: queue.TotalQuestionsAttempted,
            CorrectCount: queue.CorrectAnswers,
            CurrentRung: currentRung,
            LastMisconceptionTag: lastMisconception,
            AttemptPhase: attemptPhase,
            ElapsedMinutes: elapsedMinutes,
            DailyMinutesRemaining: Math.Max(0, budget.RemainingSeconds / 60),
            BktMasteryBucket: bucket,
            AccommodationFlags: accom,
            BuiltAtUtc: nowUtc);
    }

    // -------------------------------------------------------------------------
    // Redis I/O with fail-open semantics
    // -------------------------------------------------------------------------

    private async Task<SessionTutorContext?> TryReadCacheAsync(string sessionId, CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            var value = await db.StringGetAsync(BuildKey(sessionId))
                .WaitAsync(ct).ConfigureAwait(false);
            if (value.IsNullOrEmpty) return null;
            return JsonSerializer.Deserialize<SessionTutorContext>(value.ToString(), Json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _cacheErrors.Add(1, new KeyValuePair<string, object?>("op", "get"));
            _logger.LogWarning(ex,
                "Tutor context cache GET failed for session {SessionId}; " +
                "falling back to live build", sessionId);
            return null;
        }
    }

    private async Task TryWriteCacheAsync(string sessionId, SessionTutorContext snapshot, CancellationToken ct)
    {
        try
        {
            var db = _redis.GetDatabase();
            var payload = JsonSerializer.Serialize(snapshot, Json);
            await db.StringSetAsync(BuildKey(sessionId), payload, _sessionTtl)
                .WaitAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _cacheErrors.Add(1, new KeyValuePair<string, object?>("op", "set"));
            _logger.LogWarning(ex,
                "Tutor context cache SET failed for session {SessionId}; " +
                "next request will rebuild", sessionId);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    internal static string BuildKey(string sessionId) => $"{CacheKeyPrefix}:{sessionId}";

    internal static SessionTutorContextAttemptPhase ResolveAttemptPhase(
        LearningSessionQueueProjection queue,
        SessionAttemptHistoryDocument? history)
    {
        // No current question → the student is between questions; default to
        // FirstTry because the drawer will render "pick a question" copy.
        if (string.IsNullOrEmpty(queue.CurrentQuestionId))
            return SessionTutorContextAttemptPhase.FirstTry;

        if (history is null || history.Attempts.Count == 0)
            return SessionTutorContextAttemptPhase.FirstTry;

        var attemptsForQuestion = history.Attempts
            .Where(a => string.Equals(a.QuestionId, queue.CurrentQuestionId, StringComparison.Ordinal))
            .ToList();

        if (attemptsForQuestion.Count == 0)
            return SessionTutorContextAttemptPhase.FirstTry;

        // If the student has ever submitted a correct answer to the current
        // question, we are in PostSolution (reflection) phase.
        if (attemptsForQuestion.Any(a => a.IsCorrect))
            return SessionTutorContextAttemptPhase.PostSolution;

        // One or more wrong attempts without a correct one → retry phase.
        return SessionTutorContextAttemptPhase.Retry;
    }

    /// <summary>
    /// Extracts the most recent misconception tag from the session attempt
    /// history. Session-scoped — never consults a student-scoped store.
    /// Today the attempt-history document does not carry an explicit buggy-
    /// rule id, so the fallback uses the concept id of the most recent wrong
    /// attempt as a weak tag. This stays ADR-0003 compliant because it's
    /// still session-scoped; when
    /// <c>MisconceptionDetected_V1</c> lands in the session stream the
    /// extractor will read that event instead.
    /// </summary>
    internal static string? ExtractLastMisconceptionTag(SessionAttemptHistoryDocument? history)
    {
        if (history is null || history.Attempts.Count == 0) return null;

        var lastWrong = history.Attempts
            .Where(a => !a.IsCorrect && !a.WasSkipped)
            .OrderByDescending(a => a.Timestamp)
            .FirstOrDefault();

        if (lastWrong is null) return null;

        // Coarse concept-id tag is sufficient for the Sidekick pre-seed. The
        // ADR-0003 boundary is "session-scoped" — the tag returned here never
        // reaches a profile store (NoTutorContextPersistenceTest enforces).
        return string.IsNullOrWhiteSpace(lastWrong.ConceptId) ? null : lastWrong.ConceptId;
    }

    internal static string ResolveBktBucket(LearningSessionQueueProjection queue)
    {
        if (queue.ConceptMasterySnapshot.Count == 0) return "unknown";
        var avg = queue.ConceptMasterySnapshot.Values.Average();
        if (avg < 0.33) return "low";
        if (avg < 0.66) return "mid";
        return "high";
    }
}

// =============================================================================
// Internal extension — the queue projection does not carry a SchoolId today.
// This helper makes the null-handling explicit at the call-site and keeps the
// service body readable. If/when queue gains a SchoolId property, swap the
// implementation here without touching the service body.
// =============================================================================
internal static class LearningSessionQueueProjectionExtensions
{
    public static string? SchoolIdOrNull(this LearningSessionQueueProjection _) => null;
}
