// =============================================================================
// Cena Platform — Outbound SMS rate-limit policy (prr-018).
//
// Three-layer rate limit, enforced in this order (tightest first):
//
//   1. Per-parent-phone-hash cap       default 10 msgs / rolling 24h
//   2. Per-institute cap               default 1,000 msgs / rolling 24h
//   3. Global back-pressure cap        default 10,000 msgs / rolling 24h
//
// All three use Redis sorted-sets with sliding-window GC, mirroring the
// pattern in PushNotificationRateLimiter. Each cap has its own key:
//
//   sms:limit:phone:{phoneHash}       (zadd score=unix-seconds)
//   sms:limit:institute:{instituteId} (zadd score=unix-seconds)
//   sms:limit:global                  (zadd score=unix-seconds)
//
// WHY sliding window vs fixed-day: a parent nudge ladder that fires at 21:59
// then 00:01 would burn two "days" in two minutes under a fixed window. A
// sliding 24h window is harder to game and closer to the ethical intent.
//
// WHY per-phone-HASH not per-parent-id: parents can share a phone number (two
// step-parents, one household). The rate limit is about protecting the RECIPIENT
// device from dunning, not about whatever user-account authored the nudge.
//
// WHY emit cena_sms_rate_limited_total{institute_id,reason}:
//   - Finops and SRE (persona-finops, persona-sre) both flagged the need to
//     alert before we burn a month's SMS budget in a day. The institute_id
//     label lets a dashboard show which tenant is causing cost spike.
//
// Defer-vs-block:
//   - For ALL three layers this policy returns Block (not Defer). A message
//     that got rate-limited is, by definition, one we decided should not
//     leave the building — deferring would just pile up more back-pressure.
//     Defer is for quiet-hours which has a known earliest-send-time.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Actors.Notifications.OutboundSms;

/// <summary>
/// Options bound from <c>Cena:Sms:RateLimit</c>. All defaults are deliberately
/// conservative — ops can relax per-institute via config without code change.
/// </summary>
public sealed class SmsRateLimitOptions
{
    public const string SectionName = "Cena:Sms:RateLimit";

    /// <summary>Per-phone-hash cap per rolling 24h window.</summary>
    public int PerPhonePer24h { get; set; } = 10;

    /// <summary>Per-institute cap per rolling 24h window. Null = no cap.</summary>
    public int? PerInstitutePer24h { get; set; } = 1000;

    /// <summary>Global cap per rolling 24h window. Null = no cap.</summary>
    public int? GlobalPer24h { get; set; } = 10_000;

    /// <summary>
    /// Per-institute override of the phone cap — rare (some institutes run
    /// parent-engagement pilots with tighter caps). Keyed by instituteId.
    /// </summary>
    public Dictionary<string, int> PerInstitutePhoneOverrides { get; set; } = new();
}

/// <summary>
/// Redis-backed rate-limit policy.
/// </summary>
public sealed class SmsRateLimitPolicy : IOutboundSmsPolicy
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(24);

    private readonly IConnectionMultiplexer _redis;
    private readonly SmsRateLimitOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<SmsRateLimitPolicy> _logger;
    private readonly Counter<long> _rateLimitedCounter;

    public SmsRateLimitPolicy(
        IConnectionMultiplexer redis,
        IConfiguration configuration,
        IClock clock,
        IMeterFactory meterFactory,
        ILogger<SmsRateLimitPolicy> logger)
    {
        ArgumentNullException.ThrowIfNull(redis);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _redis = redis;
        _clock = clock;
        _logger = logger;

        // Load options. We read the section manually instead of via IOptions
        // so a Redis outage doesn't take the DI container down — this policy
        // is registered as a singleton by the gateway.
        _options = new SmsRateLimitOptions();
        configuration.GetSection(SmsRateLimitOptions.SectionName).Bind(_options);

        var meter = meterFactory.Create("Cena.Actors.OutboundSms.RateLimit", "1.0.0");
        _rateLimitedCounter = meter.CreateCounter<long>(
            "cena_sms_rate_limited_total",
            description:
                "Outbound SMS blocked by per-phone, per-institute, or global rate limit (prr-018)");
    }

    public string Name => "rate_limit";

    public async Task<SmsPolicyOutcome> EvaluateAsync(
        OutboundSmsRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var instituteLabel = SmsSanitizerPolicy.NormalizeInstituteLabel(request.InstituteId);

        IDatabase db;
        try
        {
            db = _redis.GetDatabase();
        }
        catch (RedisException ex)
        {
            // Redis outage: fail-OPEN on rate limiting — it is worse to block
            // every parent nudge because Redis is down than to briefly lose the
            // counter enforcement. We log loudly and rely on the per-institute
            // cost circuit breaker (separate component) as the backstop.
            _logger.LogError(ex,
                "[prr-018] SMS rate-limit Redis unavailable — failing open; correlation={Corr}",
                request.CorrelationId);
            return new SmsPolicyOutcome.Allow(request);
        }

        var now = _clock.UtcNow;
        var cutoff = now.Subtract(Window).ToUnixTimeSeconds();

        // Layer 1: per-phone. Strictest cap, evaluated first.
        var phoneCap = ResolvePhoneCap(request.InstituteId);
        var phoneKey = BuildPhoneKey(request.ParentPhoneHash);
        if (await ExceedsCap(db, phoneKey, cutoff, phoneCap, ct))
        {
            return RateLimited(instituteLabel, "per_phone",
                $"Per-phone cap of {phoneCap}/24h reached", request.CorrelationId);
        }

        // Layer 2: per-institute.
        if (_options.PerInstitutePer24h is { } instCap)
        {
            var instKey = BuildInstituteKey(instituteLabel);
            if (await ExceedsCap(db, instKey, cutoff, instCap, ct))
            {
                return RateLimited(instituteLabel, "per_institute",
                    $"Per-institute cap of {instCap}/24h reached", request.CorrelationId);
            }
        }

        // Layer 3: global.
        if (_options.GlobalPer24h is { } globalCap)
        {
            if (await ExceedsCap(db, GlobalKey, cutoff, globalCap, ct))
            {
                return RateLimited(instituteLabel, "global_backpressure",
                    $"Global cap of {globalCap}/24h reached", request.CorrelationId);
            }
        }

        // All caps respected — record the send against each counter. The
        // gateway is the one that actually dispatches to the vendor, so we
        // only record on pass-through. A rejection by a downstream policy
        // (none in the default chain) would over-count; that's acceptable and
        // conservative.
        await RecordAsync(db, phoneKey, now, TimeSpan.FromHours(26), ct);
        await RecordAsync(db, BuildInstituteKey(instituteLabel), now, TimeSpan.FromHours(26), ct);
        await RecordAsync(db, GlobalKey, now, TimeSpan.FromHours(26), ct);

        return new SmsPolicyOutcome.Allow(request);
    }

    private SmsPolicyOutcome RateLimited(
        string instituteLabel, string reason, string human, string correlationId)
    {
        _rateLimitedCounter.Add(1,
            new KeyValuePair<string, object?>("institute_id", instituteLabel),
            new KeyValuePair<string, object?>("reason", reason));
        _logger.LogInformation(
            "[prr-018] SMS rate-limit hit: reason={Reason} correlation={Corr} institute={Institute}",
            reason, correlationId, instituteLabel);
        return new SmsPolicyOutcome.Block(reason, human);
    }

    private int ResolvePhoneCap(string? instituteId)
    {
        if (!string.IsNullOrWhiteSpace(instituteId)
            && _options.PerInstitutePhoneOverrides is { } map
            && map.TryGetValue(instituteId!, out var overridden)
            && overridden > 0)
        {
            return overridden;
        }
        return _options.PerPhonePer24h;
    }

    private static async Task<bool> ExceedsCap(
        IDatabase db, RedisKey key, long cutoff, int cap, CancellationToken ct)
    {
        // Two round-trips: remove expired members, then count. We do NOT
        // use a MULTI/EXEC transaction here because it complicates testing
        // (IBatch/ITransaction mocks are painful in NSubstitute) and because
        // a sub-millisecond race between the two calls is harmless — at worst
        // we let one in-flight request through a freshly-freed slot.
        await db.SortedSetRemoveRangeByScoreAsync(key, double.NegativeInfinity, cutoff);
        var count = await db.SortedSetLengthAsync(key);
        return count >= cap;
    }

    private static async Task RecordAsync(
        IDatabase db, RedisKey key, DateTimeOffset now, TimeSpan ttl, CancellationToken ct)
    {
        // Score = unix seconds, member = unix-seconds string (unique per second
        // is fine; bursts within the same second coalesce which is acceptable
        // under-count — the finops alerting threshold is hours not seconds).
        var score = now.ToUnixTimeSeconds();
        await db.SortedSetAddAsync(key, score.ToString(), score);
        await db.KeyExpireAsync(key, ttl);
    }

    // Key builders — deliberately simple strings so the ops console can run
    // `redis-cli --scan --pattern 'sms:limit:*'` to audit.
    internal const string GlobalKey = "sms:limit:global";
    internal static RedisKey BuildPhoneKey(string phoneHash) =>
        $"sms:limit:phone:{phoneHash}";
    internal static RedisKey BuildInstituteKey(string instituteLabel) =>
        $"sms:limit:institute:{instituteLabel}";
}
