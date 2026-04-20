// =============================================================================
// Cena Platform — Daily Tutor Time Budget (prr-012)
//
// Enforces a per-student 30-minute daily tutor cap. Backed by a Redis
// counter keyed on UTC day, incremented by elapsed seconds per tutor turn.
//
// Key: cena:tutor:daily:{studentId}:{yyyy-MM-dd}
// TTL: 25h (rolls over with a small overlap so late-night UTC turns don't
//      lose their last increment).
//
// Why Redis INCR not a token bucket:
//   Students consume time in non-uniform chunks (a 12-second hint vs a
//   4-minute worked example). Token buckets don't model elapsed wall-clock
//   well. A simple monotonic counter of seconds per UTC day is exactly the
//   unit the product team reports on ("daily tutor minutes"). When the
//   counter crosses 30 min, we hard-stop and tell the student to rest.
//
// 25-min warning: the service emits the
// `cena_tutor_daily_warning_emitted_total` counter exactly once per
// student-day (tracked by a second Redis key with the same TTL).
//
// Non-negotiables:
//   - ADR-0001 tenancy: {studentId} is global; the upstream endpoint has
//     already authenticated the caller, so we scope by the server-side
//     studentId and never trust a client-supplied identifier.
//   - No dark-pattern copy: the "take a break" message is neutral and
//     explicitly reassuring ("the tutor will be available again tomorrow").
// =============================================================================

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Actors.RateLimit;

/// <summary>
/// Per-student daily tutor time budget (prr-012).
/// </summary>
public interface IDailyTutorTimeBudget
{
    /// <summary>
    /// Returns true if the student has remaining daily tutor minutes.
    /// Check before any tutor turn; if false, route the student to the
    /// "take a break" response (see <see cref="TakeBreakMessage"/>).
    /// </summary>
    Task<DailyTutorTimeCheck> CheckAsync(string studentId, CancellationToken ct = default);

    /// <summary>
    /// Records the elapsed seconds a tutor turn consumed (LLM + static).
    /// Called from the tutor service at the end of each turn.
    /// </summary>
    Task RecordUsageAsync(string studentId, int elapsedSeconds, CancellationToken ct = default);
}

/// <summary>
/// Result of a daily budget check.
/// </summary>
public sealed record DailyTutorTimeCheck(
    bool Allowed,
    int UsedSeconds,
    int RemainingSeconds,
    int DailyLimitSeconds);

/// <summary>
/// Redis-backed implementation. See class docs for rationale.
/// </summary>
public sealed class DailyTutorTimeBudget : IDailyTutorTimeBudget
{
    internal const string UsageKeyPrefix = "cena:tutor:daily";
    internal const string WarningKeyPrefix = "cena:tutor:daily:warning";

    /// <summary>
    /// Neutral, ship-gate compliant copy. No streak/loss-aversion wording.
    /// </summary>
    public const string TakeBreakMessage =
        "You've been studying hard today — take a break and come back tomorrow. " +
        "Your tutor will be available again in the morning, and a rested brain learns faster.";

    // Seconds
    private readonly int _dailyLimit;
    private readonly int _warningThreshold;

    // 25h ensures the last second of the day is still counted after UTC rollover.
    private static readonly TimeSpan DayKeyTtl = TimeSpan.FromHours(25);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<DailyTutorTimeBudget> _logger;
    private readonly Counter<long> _hardStopCounter;
    private readonly Counter<long> _warningCounter;

    public DailyTutorTimeBudget(
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        ILogger<DailyTutorTimeBudget> logger,
        IMeterFactory meterFactory)
    {
        _redis = redis;
        _logger = logger;

        // Routing-config.yaml maps per_student daily token limit; the
        // time-minutes cap is a separate finops control and lives in
        // app config (Cena:Tutor:DailyTimeMinutes). Default 30.
        var dailyMinutes = configuration.GetValue<int>("Cena:Tutor:DailyTimeMinutes", 30);
        var warningMinutes = configuration.GetValue<int>("Cena:Tutor:DailyWarningMinutes", 25);
        _dailyLimit = dailyMinutes * 60;
        _warningThreshold = warningMinutes * 60;

        var meter = meterFactory.Create("Cena.Actors.DailyTutorTimeBudget", "1.0.0");
        _hardStopCounter = meter.CreateCounter<long>(
            "cena_tutor_daily_hard_stop_total",
            description: "Tutor turn denied because student hit the daily time cap (prr-012)");
        _warningCounter = meter.CreateCounter<long>(
            "cena_tutor_daily_warning_emitted_total",
            description: "Tutor daily 25-minute warning emitted (prr-012)");
    }

    public async Task<DailyTutorTimeCheck> CheckAsync(string studentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentId))
            throw new ArgumentException("studentId is required", nameof(studentId));

        try
        {
            var db = _redis.GetDatabase();
            var key = BuildUsageKey(studentId);
            var value = await db.StringGetAsync(key);
            var used = value.IsNullOrEmpty ? 0 : (int)value;
            var remaining = Math.Max(0, _dailyLimit - used);
            var allowed = used < _dailyLimit;

            if (!allowed)
            {
                _hardStopCounter.Add(1);
                _logger.LogInformation(
                    "Daily tutor cap hit: student={StudentId} used={UsedSec}s limit={LimitSec}s — hard stop (prr-012)",
                    studentId, used, _dailyLimit);
            }

            return new DailyTutorTimeCheck(
                Allowed: allowed,
                UsedSeconds: used,
                RemainingSeconds: remaining,
                DailyLimitSeconds: _dailyLimit);
        }
        catch (Exception ex)
        {
            // Fail-open: a Redis outage should not lock every student out of
            // tutoring. ICostCircuitBreaker is the independent spend backstop.
            _logger.LogError(ex,
                "DailyTutorTimeBudget.CheckAsync failed for student {StudentId} — failing open",
                studentId);
            return new DailyTutorTimeCheck(true, 0, _dailyLimit, _dailyLimit);
        }
    }

    public async Task RecordUsageAsync(string studentId, int elapsedSeconds, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentId))
            throw new ArgumentException("studentId is required", nameof(studentId));
        if (elapsedSeconds <= 0) return;

        try
        {
            var db = _redis.GetDatabase();
            var key = BuildUsageKey(studentId);
            var newUsed = await db.StringIncrementAsync(key, elapsedSeconds);
            await db.KeyExpireAsync(key, DayKeyTtl);

            // Emit the 25-min warning at most once per student-day.
            if (newUsed >= _warningThreshold && newUsed - elapsedSeconds < _warningThreshold)
            {
                var warningKey = BuildWarningKey(studentId);
                // SET NX so only the first crossing wins.
                var firstCrossing = await db.StringSetAsync(
                    warningKey, "1", DayKeyTtl, When.NotExists);
                if (firstCrossing)
                {
                    _warningCounter.Add(1);
                    _logger.LogInformation(
                        "Daily tutor warning threshold crossed: student={StudentId} used={UsedSec}s warn_at={WarnSec}s",
                        studentId, newUsed, _warningThreshold);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DailyTutorTimeBudget.RecordUsageAsync failed for student {StudentId} ({Seconds}s lost)",
                studentId, elapsedSeconds);
        }
    }

    internal static string BuildUsageKey(string studentId)
        => $"{UsageKeyPrefix}:{studentId}:{DateTime.UtcNow:yyyy-MM-dd}";

    internal static string BuildWarningKey(string studentId)
        => $"{WarningKeyPrefix}:{studentId}:{DateTime.UtcNow:yyyy-MM-dd}";
}
