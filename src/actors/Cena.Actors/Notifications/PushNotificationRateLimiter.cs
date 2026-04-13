// =============================================================================
// Cena Platform — Push Notification Rate Limiter (PWA-BE-002)
// Redis-backed daily/weekly limits per student for Web Push notifications.
// =============================================================================

using StackExchange.Redis;

namespace Cena.Actors.Notifications;

/// <summary>
/// Rate limiter for push notifications enforcing per-student daily and weekly caps.
/// </summary>
public interface IPushNotificationRateLimiter
{
    /// <summary>
    /// Returns true if the student has not exceeded 3 push notifications today
    /// and 10 push notifications this week.
    /// </summary>
    Task<bool> CanSendAsync(string studentId, CancellationToken ct = default);

    /// <summary>
    /// Records a sent push notification against the student's daily and weekly quotas.
    /// </summary>
    Task RecordSentAsync(string studentId, CancellationToken ct = default);
}

/// <summary>
/// Redis-backed implementation using sorted sets with sliding windows.
/// </summary>
public sealed class PushNotificationRateLimiter : IPushNotificationRateLimiter
{
    private readonly IConnectionMultiplexer _redis;
    private const int MaxPerDay = 3;
    private const int MaxPerWeek = 10;

    public PushNotificationRateLimiter(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public async Task<bool> CanSendAsync(string studentId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var now = DateTimeOffset.UtcNow;
        var dayStart = now.AddDays(-1).ToUnixTimeSeconds();
        var weekStart = now.AddDays(-7).ToUnixTimeSeconds();
        var score = now.ToUnixTimeSeconds();

        var dayKey = $"push:limit:day:{studentId}";
        var weekKey = $"push:limit:week:{studentId}";

        // Clean old entries and count remaining in day/week windows atomically via Lua
        var lua = @"
            redis.call('zremrangebyscore', KEYS[1], '-inf', ARGV[1])
            redis.call('zremrangebyscore', KEYS[2], '-inf', ARGV[2])
            local dayCount = redis.call('zcard', KEYS[1])
            local weekCount = redis.call('zcard', KEYS[2])
            return {dayCount, weekCount}
        ";

        var result = (RedisResult[])await db.ScriptEvaluateAsync(lua,
            new RedisKey[] { dayKey, weekKey },
            new RedisValue[] { dayStart, weekStart });

        var dayCount = (int)result[0];
        var weekCount = (int)result[1];

        return dayCount < MaxPerDay && weekCount < MaxPerWeek;
    }

    public async Task RecordSentAsync(string studentId, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var now = DateTimeOffset.UtcNow;
        var score = now.ToUnixTimeSeconds();
        var dayKey = $"push:limit:day:{studentId}";
        var weekKey = $"push:limit:week:{studentId}";

        // Add entry to both windows; let TTL clean them up eventually
        await db.SortedSetAddAsync(dayKey, score.ToString(), score);
        await db.SortedSetAddAsync(weekKey, score.ToString(), score);
        await db.KeyExpireAsync(dayKey, TimeSpan.FromDays(2));
        await db.KeyExpireAsync(weekKey, TimeSpan.FromDays(8));
    }
}
