// =============================================================================
// Cena Platform — Daily Tutor Time Budget (prr-012 + prr-048)
//
// Enforces a per-student daily tutor-minute cap. Backed by a Redis counter
// keyed on UTC day, incremented by elapsed seconds per tutor turn.
//
// prr-012 baseline (2026-04-14): default 30-min hard stop, fixed 25-min
// warning — single global value, no tenant override.
//
// prr-048 extension (2026-04-20 pre-release review, finops + ethics lens):
//   - Soft-limit nudge is now percentage-based (default 80% of the cap),
//     not a fixed-minute threshold, so per-institute overrides produce the
//     expected proportional UX ("80% of a 20-minute cap is 16 minutes").
//   - Per-institute config override lets the operator tighten (not relax)
//     the cap in `Cena:Tutor:InstituteOverrides:<instituteId>:DailyTimeMinutes`.
//     Null institute or no override → falls back to the platform default.
//   - Spec-named metrics add `institute_id` + `cap_type` labels:
//         cena_student_daily_minute_cap_hit_total{institute_id, cap_type}
//         cena_student_daily_minute_cap_nudge_total{institute_id}
//     The pre-existing `cena_tutor_daily_hard_stop_total` and
//     `cena_tutor_daily_warning_emitted_total` counters are retained for
//     dashboard continuity.
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
//   counter crosses the cap, we hard-stop and tell the student to rest.
//
// Non-negotiables:
//   - ADR-0001 tenancy: {studentId} is globally unique; {instituteId} is an
//     optional metric/config label only, never used for auth. The upstream
//     endpoint has already authenticated the caller.
//   - ADR-0003 session scope: this counter is operational (25h TTL),
//     NOT on the student profile, NOT used for ML training. A counter of
//     tutor-seconds is finops telemetry, not pedagogical data.
//   - No dark-pattern copy: the "take a break" and nudge strings are
//     ship-gate compliant — no streak, no loss-aversion, no FOMO.
//   - Honest-not-complimentary: the nudge quotes the actual minutes used
//     and remaining. Euphemism would hide the cap.
// =============================================================================

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Actors.RateLimit;

/// <summary>
/// Per-student daily tutor time budget (prr-012 + prr-048).
/// </summary>
public interface IDailyTutorTimeBudget
{
    /// <summary>
    /// Returns the current daily budget state for the student. Check before
    /// any tutor turn; if <see cref="DailyTutorTimeCheck.Allowed"/> is false,
    /// route the student to the "take a break" response (see
    /// <see cref="DailyTutorTimeBudget.TakeBreakMessage"/>).
    /// </summary>
    /// <param name="studentId">Globally-unique student identifier.</param>
    /// <param name="instituteId">
    /// Optional tenant identifier (ADR-0001). Used ONLY as a metric label and
    /// for per-institute config override of the cap value. Pass null when
    /// unknown — metrics will tag as "unknown" and the platform default applies.
    /// </param>
    Task<DailyTutorTimeCheck> CheckAsync(
        string studentId,
        string? instituteId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Records the elapsed seconds a tutor turn consumed (LLM + static).
    /// Called from the tutor service at the end of each turn. Also emits
    /// the 80% soft-limit nudge counter at most once per student-day.
    /// </summary>
    /// <param name="studentId">Globally-unique student identifier.</param>
    /// <param name="elapsedSeconds">Wall-clock seconds consumed by this turn.</param>
    /// <param name="instituteId">
    /// Optional tenant identifier (ADR-0001). See <see cref="CheckAsync"/>.
    /// </param>
    Task RecordUsageAsync(
        string studentId,
        int elapsedSeconds,
        string? instituteId = null,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a daily budget check.
/// </summary>
/// <param name="Allowed">True iff the student has remaining daily minutes.</param>
/// <param name="UsedSeconds">Seconds consumed today so far.</param>
/// <param name="RemainingSeconds">Seconds remaining in today's budget.</param>
/// <param name="DailyLimitSeconds">
/// Effective cap in seconds — after any per-institute override has been applied.
/// </param>
/// <param name="NudgeThresholdSeconds">
/// The soft-limit threshold in seconds (default 80% of the cap). Callers may
/// inspect this to render a consistent "approaching your daily limit" message
/// without recomputing the percentage.
/// </param>
public sealed record DailyTutorTimeCheck(
    bool Allowed,
    int UsedSeconds,
    int RemainingSeconds,
    int DailyLimitSeconds,
    int NudgeThresholdSeconds = 0);

/// <summary>
/// Redis-backed implementation. See class docs for rationale.
/// </summary>
public sealed class DailyTutorTimeBudget : IDailyTutorTimeBudget
{
    internal const string UsageKeyPrefix = "cena:tutor:daily";
    internal const string WarningKeyPrefix = "cena:tutor:daily:warning";
    internal const string NudgeKeyPrefix = "cena:tutor:daily:nudge";
    internal const string UnknownInstituteLabel = "unknown";

    /// <summary>
    /// Neutral, ship-gate compliant copy. No streak/loss-aversion wording.
    /// Used when the student has hit the 100% hard limit.
    /// </summary>
    public const string TakeBreakMessage =
        "You've been studying hard today — take a break and come back tomorrow. " +
        "Your tutor will be available again in the morning, and a rested brain learns faster.";

    // prr-048 defaults. Platform-wide; per-institute overrides applied per-call.
    internal const int DefaultDailyTimeMinutes = 30;
    internal const int DefaultNudgePercent = 80;   // 80% of the cap
    internal const int DefaultWarningMinutes = 25; // legacy prr-012 counter

    // 25h ensures the last second of the day is still counted after UTC rollover.
    private static readonly TimeSpan DayKeyTtl = TimeSpan.FromHours(25);

    private readonly int _defaultDailyLimitSeconds;
    private readonly int _defaultWarningThresholdSeconds;
    private readonly int _nudgePercent;
    private readonly IConfiguration _configuration;

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<DailyTutorTimeBudget> _logger;

    // Legacy prr-012 counters — kept for existing dashboards.
    private readonly Counter<long> _hardStopCounter;
    private readonly Counter<long> _warningCounter;

    // prr-048 spec-named counters — labeled by institute_id and cap_type.
    private readonly Counter<long> _capHitCounter;
    private readonly Counter<long> _nudgeCounter;

    public DailyTutorTimeBudget(
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        ILogger<DailyTutorTimeBudget> logger,
        IMeterFactory meterFactory)
    {
        _redis = redis;
        _logger = logger;
        _configuration = configuration;

        var dailyMinutes = configuration.GetValue<int>(
            "Cena:Tutor:DailyTimeMinutes", DefaultDailyTimeMinutes);
        var warningMinutes = configuration.GetValue<int>(
            "Cena:Tutor:DailyWarningMinutes", DefaultWarningMinutes);
        var nudgePercent = configuration.GetValue<int>(
            "Cena:Tutor:DailyNudgePercent", DefaultNudgePercent);

        // Defensive clamp: nudge threshold must be in (0, 100). An invalid
        // config value should not silently become 0% (always nudge) or >=100%
        // (never nudge). Out-of-range → fall back to the documented default.
        if (nudgePercent <= 0 || nudgePercent >= 100)
        {
            _logger.LogWarning(
                "Cena:Tutor:DailyNudgePercent={Nudge} is out of (0,100); falling back to {Default}%",
                nudgePercent, DefaultNudgePercent);
            nudgePercent = DefaultNudgePercent;
        }

        _defaultDailyLimitSeconds = Math.Max(1, dailyMinutes) * 60;
        _defaultWarningThresholdSeconds = Math.Max(1, warningMinutes) * 60;
        _nudgePercent = nudgePercent;

        var meter = meterFactory.Create("Cena.Actors.DailyTutorTimeBudget", "1.0.0");
        _hardStopCounter = meter.CreateCounter<long>(
            "cena_tutor_daily_hard_stop_total",
            description: "Tutor turn denied because student hit the daily time cap (prr-012)");
        _warningCounter = meter.CreateCounter<long>(
            "cena_tutor_daily_warning_emitted_total",
            description: "Tutor daily 25-minute warning emitted (prr-012)");

        _capHitCounter = meter.CreateCounter<long>(
            "cena_student_daily_minute_cap_hit_total",
            description: "Per-student daily minute cap hit, labeled by institute_id and cap_type (prr-048)");
        _nudgeCounter = meter.CreateCounter<long>(
            "cena_student_daily_minute_cap_nudge_total",
            description: "Soft-limit nudge emitted at 80% of the daily minute cap, per institute (prr-048)");
    }

    public async Task<DailyTutorTimeCheck> CheckAsync(
        string studentId,
        string? instituteId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentId))
            throw new ArgumentException("studentId is required", nameof(studentId));

        var effectiveCapSeconds = ResolveEffectiveCapSeconds(instituteId);
        var nudgeThresholdSeconds = ComputeNudgeThreshold(effectiveCapSeconds);
        var instituteLabel = NormalizeInstituteLabel(instituteId);

        try
        {
            var db = _redis.GetDatabase();
            var key = BuildUsageKey(studentId);
            var value = await db.StringGetAsync(key);
            var used = value.IsNullOrEmpty ? 0 : (int)value;
            var remaining = Math.Max(0, effectiveCapSeconds - used);
            var allowed = used < effectiveCapSeconds;

            if (!allowed)
            {
                _hardStopCounter.Add(1);
                _capHitCounter.Add(1,
                    new KeyValuePair<string, object?>("institute_id", instituteLabel),
                    new KeyValuePair<string, object?>("cap_type", "hard"));
                _logger.LogInformation(
                    "Daily tutor cap hit: student={StudentId} institute={InstituteId} used={UsedSec}s limit={LimitSec}s — hard stop (prr-012/prr-048)",
                    studentId, instituteLabel, used, effectiveCapSeconds);
            }

            return new DailyTutorTimeCheck(
                Allowed: allowed,
                UsedSeconds: used,
                RemainingSeconds: remaining,
                DailyLimitSeconds: effectiveCapSeconds,
                NudgeThresholdSeconds: nudgeThresholdSeconds);
        }
        catch (Exception ex)
        {
            // Fail-open: a Redis outage should not lock every student out of
            // tutoring. ICostCircuitBreaker is the independent spend backstop.
            _logger.LogError(ex,
                "DailyTutorTimeBudget.CheckAsync failed for student {StudentId} (institute={InstituteId}) — failing open",
                studentId, instituteLabel);
            return new DailyTutorTimeCheck(
                true, 0, effectiveCapSeconds, effectiveCapSeconds, nudgeThresholdSeconds);
        }
    }

    public async Task RecordUsageAsync(
        string studentId,
        int elapsedSeconds,
        string? instituteId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentId))
            throw new ArgumentException("studentId is required", nameof(studentId));
        if (elapsedSeconds <= 0) return;

        var effectiveCapSeconds = ResolveEffectiveCapSeconds(instituteId);
        var nudgeThresholdSeconds = ComputeNudgeThreshold(effectiveCapSeconds);
        var instituteLabel = NormalizeInstituteLabel(instituteId);

        try
        {
            var db = _redis.GetDatabase();
            var key = BuildUsageKey(studentId);
            var newUsed = await db.StringIncrementAsync(key, elapsedSeconds);
            await db.KeyExpireAsync(key, DayKeyTtl);

            // Legacy prr-012 fixed 25-min warning — kept for dashboard continuity.
            if (newUsed >= _defaultWarningThresholdSeconds
                && newUsed - elapsedSeconds < _defaultWarningThresholdSeconds)
            {
                var warningKey = BuildWarningKey(studentId);
                var firstCrossing = await db.StringSetAsync(
                    warningKey, "1", DayKeyTtl, When.NotExists);
                if (firstCrossing)
                {
                    _warningCounter.Add(1);
                    _logger.LogInformation(
                        "Daily tutor legacy warning crossed: student={StudentId} used={UsedSec}s warn_at={WarnSec}s",
                        studentId, newUsed, _defaultWarningThresholdSeconds);
                }
            }

            // prr-048 percentage-based soft-limit nudge. Fires at most once
            // per student-day, keyed separately so it doesn't race with the
            // legacy warning. Detect the crossing boundary: previous usage
            // was below the nudge threshold, current usage is at or above it.
            if (newUsed >= nudgeThresholdSeconds
                && newUsed - elapsedSeconds < nudgeThresholdSeconds
                && newUsed < effectiveCapSeconds)
            {
                var nudgeKey = BuildNudgeKey(studentId);
                var firstNudge = await db.StringSetAsync(
                    nudgeKey, "1", DayKeyTtl, When.NotExists);
                if (firstNudge)
                {
                    _nudgeCounter.Add(1,
                        new KeyValuePair<string, object?>("institute_id", instituteLabel));
                    _logger.LogInformation(
                        "Daily tutor soft-limit nudge: student={StudentId} institute={InstituteId} used={UsedSec}s nudge_at={NudgeSec}s cap={CapSec}s (prr-048)",
                        studentId, instituteLabel, newUsed, nudgeThresholdSeconds, effectiveCapSeconds);
                }
            }

            // Hard-limit crossing — record the prr-048 cap-hit counter once
            // per student-day when usage first crosses the cap during record.
            if (newUsed >= effectiveCapSeconds
                && newUsed - elapsedSeconds < effectiveCapSeconds)
            {
                _capHitCounter.Add(1,
                    new KeyValuePair<string, object?>("institute_id", instituteLabel),
                    new KeyValuePair<string, object?>("cap_type", "hard"));
                _logger.LogInformation(
                    "Daily tutor cap reached on record: student={StudentId} institute={InstituteId} used={UsedSec}s cap={CapSec}s (prr-048)",
                    studentId, instituteLabel, newUsed, effectiveCapSeconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DailyTutorTimeBudget.RecordUsageAsync failed for student {StudentId} ({Seconds}s lost, institute={InstituteId})",
                studentId, elapsedSeconds, instituteLabel);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves the effective daily cap in seconds after applying any
    /// per-institute configuration override. Override key:
    ///   Cena:Tutor:InstituteOverrides:{instituteId}:DailyTimeMinutes
    /// Non-positive or missing → platform default.
    /// </summary>
    internal int ResolveEffectiveCapSeconds(string? instituteId)
    {
        if (string.IsNullOrWhiteSpace(instituteId))
            return _defaultDailyLimitSeconds;

        var key = $"Cena:Tutor:InstituteOverrides:{instituteId}:DailyTimeMinutes";
        var overrideMinutes = _configuration.GetValue<int?>(key);
        if (overrideMinutes is > 0)
            return overrideMinutes.Value * 60;

        return _defaultDailyLimitSeconds;
    }

    internal int ComputeNudgeThreshold(int capSeconds) =>
        Math.Max(1, (int)Math.Floor(capSeconds * (_nudgePercent / 100.0)));

    internal static string NormalizeInstituteLabel(string? instituteId) =>
        string.IsNullOrWhiteSpace(instituteId) ? UnknownInstituteLabel : instituteId;

    internal static string BuildUsageKey(string studentId)
        => $"{UsageKeyPrefix}:{studentId}:{DateTime.UtcNow:yyyy-MM-dd}";

    internal static string BuildWarningKey(string studentId)
        => $"{WarningKeyPrefix}:{studentId}:{DateTime.UtcNow:yyyy-MM-dd}";

    internal static string BuildNudgeKey(string studentId)
        => $"{NudgeKeyPrefix}:{studentId}:{DateTime.UtcNow:yyyy-MM-dd}";

    /// <summary>
    /// Renders the honest soft-limit nudge copy for a student at the 80%
    /// threshold. Used by student-facing surfaces that render a banner or
    /// toast. No streak, no loss-aversion, no FOMO — the message states the
    /// actual minutes used and remaining so students can make an informed
    /// decision to stop or push through.
    /// </summary>
    public static string RenderNudge(int usedSeconds, int remainingSeconds)
    {
        var usedMinutes = Math.Max(0, usedSeconds / 60);
        var remainingMinutes = Math.Max(0, remainingSeconds / 60);
        return
            $"You've used about {usedMinutes} minutes of tutor time today and have roughly {remainingMinutes} minutes left. " +
            "That's plenty to finish the step you're on — and a short rest afterwards usually helps the material stick.";
    }
}
