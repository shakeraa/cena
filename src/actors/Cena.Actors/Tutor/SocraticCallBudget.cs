// =============================================================================
// Cena Platform — Socratic LLM Call Budget (prr-012)
//
// Hard-caps Socratic self-explanation LLM calls to 3 per tutoring session to
// prevent unbounded Sonnet spend. Per the finops lens of the 2026-04-20
// pre-release review, default Sonnet routing at 10k students × 5 problems/hr ×
// 3 turns ≈ 150k calls/hr ≈ $480k/month — 16× the $30k global cap. Hard-capping
// per session plus SAI-003 L2 cache reuse (ExplanationCacheService) collapses
// this to projected ~$25k/month.
//
// Contract
// --------
// Before every LLM call in ClaudeTutorLlmService, callers MUST do:
//
//     if (!await _budget.CanMakeLlmCallAsync(sessionId, ct))
//         → fall back to StaticHintLadderFallback (no LLM)
//     else
//         → call LLM, then `await _budget.RecordLlmCallAsync(sessionId, ct)`
//
// The counter lives at `cena:socratic:calls:{sessionId}` with a sliding
// session TTL (24h). Redis failure is fail-safe — we return true (allow the
// call) because the ICostCircuitBreaker tier is an independent backstop on
// actual spend, and blocking tutoring entirely on Redis outage is a worse
// user outcome than a single leaked overage.
// =============================================================================

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Actors.Tutor;

/// <summary>
/// Per-session LLM call budget for Socratic tutoring (prr-012).
/// Caps Socratic LLM calls at 3 per session; beyond that, callers must
/// fall back to the static hint ladder.
/// </summary>
public interface ISocraticCallBudget
{
    /// <summary>
    /// Returns true if the session has budget for another LLM call.
    /// Does NOT reserve the budget — callers must call
    /// <see cref="RecordLlmCallAsync"/> after a successful LLM invocation.
    /// </summary>
    Task<bool> CanMakeLlmCallAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Atomically increments the session's LLM call counter.
    /// Call ONLY after a successful LLM invocation — failed attempts
    /// should not consume budget.
    /// </summary>
    /// <returns>The post-increment count for observability.</returns>
    Task<long> RecordLlmCallAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Returns the current LLM call count for a session (0 if unseen).
    /// </summary>
    Task<long> GetCallCountAsync(string sessionId, CancellationToken ct = default);
}

/// <summary>
/// Redis-backed implementation of the Socratic LLM call budget.
/// Key: <c>cena:socratic:calls:{sessionId}</c>, TTL 24h sliding.
/// </summary>
public sealed class SocraticCallBudget : ISocraticCallBudget
{
    /// <summary>Hard cap per session. Non-configurable — locked by prr-012 DoD.</summary>
    public const int MaxLlmCallsPerSession = 3;

    internal const string KeyPrefix = "cena:socratic:calls";
    private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(24);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<SocraticCallBudget> _logger;
    private readonly Counter<long> _capHitCounter;
    private readonly Counter<long> _allowedCounter;

    public SocraticCallBudget(
        IConnectionMultiplexer redis,
        ILogger<SocraticCallBudget> logger,
        IMeterFactory meterFactory)
    {
        _redis = redis;
        _logger = logger;

        var meter = meterFactory.Create("Cena.Actors.SocraticCallBudget", "1.0.0");
        _capHitCounter = meter.CreateCounter<long>(
            "cena_socratic_cap_hit_total",
            description: "Socratic LLM call denied because session hit the 3-call cap (prr-012)");
        _allowedCounter = meter.CreateCounter<long>(
            "cena_socratic_call_allowed_total",
            description: "Socratic LLM calls under budget (prr-012)");
    }

    public async Task<bool> CanMakeLlmCallAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("sessionId is required", nameof(sessionId));

        try
        {
            var count = await GetCallCountAsync(sessionId, ct);
            if (count >= MaxLlmCallsPerSession)
            {
                _capHitCounter.Add(1);
                _logger.LogWarning(
                    "Socratic LLM cap hit: session={SessionHash} count={Count} cap={Cap} — degrading to static hint ladder (prr-012)",
                    HashSessionId(sessionId), count, MaxLlmCallsPerSession);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            // Fail-safe: allow the call. ICostCircuitBreaker is the independent
            // backstop on actual spend; blocking tutoring entirely on a Redis
            // outage is a worse user outcome than a single leaked overage.
            _logger.LogError(ex,
                "SocraticCallBudget.CanMakeLlmCallAsync failed for session {SessionHash} — failing open",
                HashSessionId(sessionId));
            return true;
        }
    }

    public async Task<long> RecordLlmCallAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("sessionId is required", nameof(sessionId));

        try
        {
            var db = _redis.GetDatabase();
            var key = BuildKey(sessionId);

            // INCR is atomic; EXPIRE refreshes sliding TTL on every call.
            var count = await db.StringIncrementAsync(key);
            await db.KeyExpireAsync(key, SessionTtl);

            _allowedCounter.Add(1);
            _logger.LogDebug(
                "Socratic LLM call recorded: session={SessionHash} count={Count}/{Cap}",
                HashSessionId(sessionId), count, MaxLlmCallsPerSession);

            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SocraticCallBudget.RecordLlmCallAsync failed for session {SessionHash} — budget counter may drift",
                HashSessionId(sessionId));
            return 0;
        }
    }

    public async Task<long> GetCallCountAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("sessionId is required", nameof(sessionId));

        try
        {
            var db = _redis.GetDatabase();
            var key = BuildKey(sessionId);
            var value = await db.StringGetAsync(key);
            return value.IsNullOrEmpty ? 0 : (long)value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SocraticCallBudget.GetCallCountAsync failed for session {SessionHash}",
                HashSessionId(sessionId));
            return 0;
        }
    }

    internal static string BuildKey(string sessionId) => $"{KeyPrefix}:{sessionId}";

    /// <summary>
    /// Stable short hash of the session id for log correlation without
    /// leaking the raw id. ADR-0003-adjacent: keep session-scoped data
    /// out of structured logs in raw form.
    /// </summary>
    private static string HashSessionId(string sessionId)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(sessionId));
        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
    }
}
