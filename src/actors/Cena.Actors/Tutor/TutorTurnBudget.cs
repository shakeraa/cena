// =============================================================================
// Cena Platform — Tutor Turn Budget (prr-105, from ADR-0002)
//
// Hard-caps the number of LLM tutoring turns per session to a configurable
// ceiling (default 20). Complements the prr-012 three-call Socratic self-
// explanation cap: that cap protects against unbounded per-session Sonnet
// spend; this cap protects against pedagogical drift — past ~20 turns the
// Socratic dialogue loses cognitive-load coherence and the CAS oracle can no
// longer anchor the conversation to verified math (ADR-0002 §Turn budget).
//
// Contract
// --------
// Before every Socratic LLM call (after the prr-012 SocraticCallBudget gate
// passes), callers MUST do:
//
//     if (!await _turnBudget.CanTakeTurnAsync(sessionId, instituteId, ct))
//         → fall back to StaticHintLadderFallback (no LLM)
//     else
//         → call LLM, then `await _turnBudget.RecordTurnAsync(sessionId, ct)`
//
// Per-institute config override:
//   Cena:Tutor:InstituteOverrides:<instituteId>:MaxTurns     (tighten only)
//   Cena:Tutor:DefaultMaxTurns                               (platform default)
//
// The counter lives at `cena:tutor:turns:{sessionId}` with a 24h sliding TTL.
// Redis failure is fail-safe — we return true (allow the turn) because the
// prr-012 SocraticCallBudget + the global cost circuit breaker are
// independent backstops; blocking tutoring entirely on a Redis outage is a
// worse student outcome than a single overage above 20 turns.
//
// Metric:
//   cena_tutor_turn_cap_hit_total{institute_id} — counter, emitted once per
//   cap-hit. Used by the prr-095 runbook to quantify fallback traffic when
//   the vendor degrades.
//
// Non-negotiables:
//   - ADR-0002: the turn cap is the policy, not a convenience — no bypass
//     under A/B tests, growth experiments, or "premium" tiers.
//   - ADR-0001 tenancy: {sessionId} is globally unique; {instituteId} is a
//     config + metric label only, never used for auth. Upstream endpoints
//     authenticate and authorise before reaching this gate.
//   - ADR-0003 session scope: the counter is session-keyed with 24h TTL, is
//     never projected onto a student profile, and never exported.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Actors.Tutor;

/// <summary>
/// Per-session Socratic tutor-turn budget (prr-105).
/// </summary>
public interface ITutorTurnBudget
{
    /// <summary>
    /// Returns true if the session still has budget for another Socratic
    /// tutor turn. Does NOT reserve the budget — callers must call
    /// <see cref="RecordTurnAsync"/> after a successful LLM turn.
    /// </summary>
    /// <param name="sessionId">Globally-unique session identifier.</param>
    /// <param name="instituteId">
    /// Optional tenant identifier (ADR-0001). Used ONLY as a metric label
    /// and for per-institute config override of the cap value. Pass null
    /// when unknown — metrics will tag as "unknown" and the platform
    /// default applies.
    /// </param>
    Task<bool> CanTakeTurnAsync(
        string sessionId,
        string? instituteId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically increments the session's tutor-turn counter. Call ONLY
    /// after a successful LLM turn — failed attempts should not consume
    /// budget.
    /// </summary>
    /// <returns>The post-increment count for observability.</returns>
    Task<long> RecordTurnAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Returns the current tutor-turn count for a session (0 if unseen).
    /// </summary>
    Task<long> GetTurnCountAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Resolves the effective per-session turn cap for the given institute.
    /// Returns the platform default when no override is configured.
    /// </summary>
    int ResolveCapFor(string? instituteId);
}

/// <summary>
/// Redis-backed implementation of the tutor-turn budget (prr-105).
/// </summary>
public sealed class TutorTurnBudget : ITutorTurnBudget
{
    /// <summary>
    /// Platform-default turn cap per session (ADR-0002). Institutes may
    /// override downward in config but cannot relax above this without
    /// an ADR amendment.
    /// </summary>
    public const int DefaultMaxTurns = 20;

    /// <summary>Absolute floor — configuring below this is a config error.</summary>
    internal const int MinConfigurableTurns = 3;

    internal const string KeyPrefix = "cena:tutor:turns";
    private static readonly TimeSpan SessionTtl = TimeSpan.FromHours(24);

    private readonly IConnectionMultiplexer _redis;
    private readonly IConfiguration _config;
    private readonly ILogger<TutorTurnBudget> _logger;
    private readonly Counter<long> _capHitCounter;
    private readonly int _platformDefault;

    public TutorTurnBudget(
        IConnectionMultiplexer redis,
        IConfiguration config,
        ILogger<TutorTurnBudget> logger,
        IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(meterFactory);

        _redis = redis;
        _config = config;
        _logger = logger;

        _platformDefault = config.GetValue<int?>("Cena:Tutor:DefaultMaxTurns") ?? DefaultMaxTurns;
        if (_platformDefault < MinConfigurableTurns)
        {
            _logger.LogWarning(
                "Cena:Tutor:DefaultMaxTurns={Configured} is below the floor {Floor} — using floor",
                _platformDefault, MinConfigurableTurns);
            _platformDefault = MinConfigurableTurns;
        }

        var meter = meterFactory.Create("Cena.Actors.TutorTurnBudget", "1.0.0");
        _capHitCounter = meter.CreateCounter<long>(
            "cena_tutor_turn_cap_hit_total",
            description: "Socratic tutor turn denied because session hit the per-institute turn cap (prr-105)");
    }

    public int ResolveCapFor(string? instituteId)
    {
        if (!string.IsNullOrWhiteSpace(instituteId))
        {
            var key = $"Cena:Tutor:InstituteOverrides:{instituteId}:MaxTurns";
            var overrideValue = _config.GetValue<int?>(key);
            if (overrideValue is int v && v >= MinConfigurableTurns)
            {
                // Tighten-only: institutes may lower the cap but never raise
                // it above the platform default (ADR-0002 policy lock).
                return Math.Min(v, _platformDefault);
            }
        }
        return _platformDefault;
    }

    public async Task<bool> CanTakeTurnAsync(
        string sessionId,
        string? instituteId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("sessionId is required", nameof(sessionId));

        var cap = ResolveCapFor(instituteId);

        try
        {
            var count = await GetTurnCountAsync(sessionId, ct);
            if (count >= cap)
            {
                var tags = new TagList
                {
                    { "institute_id", string.IsNullOrWhiteSpace(instituteId) ? "unknown" : instituteId! }
                };
                _capHitCounter.Add(1, tags);
                _logger.LogWarning(
                    "Tutor turn cap hit: session={SessionHash} institute={Institute} count={Count} cap={Cap} — degrading to static hint ladder (prr-105)",
                    HashSessionId(sessionId),
                    instituteId ?? "unknown",
                    count, cap);
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            // Fail-safe: allow the turn. prr-012 SocraticCallBudget + the
            // global ICostCircuitBreaker tier are independent backstops on
            // actual spend, and blocking tutoring entirely on Redis outage
            // is a worse student outcome than a single overage above 20 turns.
            _logger.LogError(ex,
                "TutorTurnBudget.CanTakeTurnAsync failed for session {SessionHash} — failing open",
                HashSessionId(sessionId));
            return true;
        }
    }

    public async Task<long> RecordTurnAsync(string sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("sessionId is required", nameof(sessionId));

        try
        {
            var db = _redis.GetDatabase();
            var key = BuildKey(sessionId);

            var count = await db.StringIncrementAsync(key);
            await db.KeyExpireAsync(key, SessionTtl);
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "TutorTurnBudget.RecordTurnAsync failed for session {SessionHash} — turn counter may drift",
                HashSessionId(sessionId));
            return 0;
        }
    }

    public async Task<long> GetTurnCountAsync(string sessionId, CancellationToken ct = default)
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
                "TutorTurnBudget.GetTurnCountAsync failed for session {SessionHash}",
                HashSessionId(sessionId));
            return 0;
        }
    }

    internal static string BuildKey(string sessionId) => $"{KeyPrefix}:{sessionId}";

    /// <summary>
    /// Stable short hash of the session id for log correlation without
    /// leaking the raw id. ADR-0003-adjacent: keep session-scoped data out
    /// of structured logs in raw form.
    /// </summary>
    private static string HashSessionId(string sessionId)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(sessionId));
        return Convert.ToHexString(hash.AsSpan(0, 4)).ToLowerInvariant();
    }
}
