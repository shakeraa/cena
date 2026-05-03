// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Message Throttler (Redis-Backed)
// Layer: Domain Service | Runtime: .NET 9
// Per-role rate limiting using Redis INCR with automatic TTL expiry.
// Horizontally scalable — state shared across all app instances.
// ═══════════════════════════════════════════════════════════════════════

using StackExchange.Redis;

namespace Cena.Actors.Messaging;

public interface IMessageThrottler
{
    ThrottleResult Check(string userId, MessageRole role);
    void RecordSend(string userId, MessageRole role);
    void Reset(string userId);
}

public sealed class MessageThrottler : IMessageThrottler
{
    private readonly IConnectionMultiplexer _redis;

    // Per-role limits
    private static readonly Dictionary<MessageRole, (int Daily, int Hourly)> Limits = new()
    {
        [MessageRole.Teacher] = (Daily: 100, Hourly: 30),
        [MessageRole.Parent] = (Daily: 10, Hourly: 5),
        [MessageRole.Student] = (Daily: 0, Hourly: 0),
        [MessageRole.System] = (Daily: int.MaxValue, Hourly: int.MaxValue),
    };

    private const string KeyPrefix = "cena:throttle";

    public MessageThrottler(IConnectionMultiplexer redis)
    {
        _redis = redis;
    }

    public ThrottleResult Check(string userId, MessageRole role)
    {
        if (role == MessageRole.Student)
            return new ThrottleResult(false, RetryAfterSeconds: 0);

        if (role == MessageRole.System)
            return new ThrottleResult(true);

        var limits = Limits[role];
        var db = _redis.GetDatabase();

        // Check hourly limit first (smaller window, more likely to trip)
        var hourlyKey = $"{KeyPrefix}:{userId}:hourly";
        var hourlyCount = (int)(db.StringGet(hourlyKey));

        if (hourlyCount >= limits.Hourly)
        {
            var ttl = db.KeyTimeToLive(hourlyKey);
            int retryAfter = ttl.HasValue ? Math.Max(1, (int)ttl.Value.TotalSeconds) : 3600;
            return new ThrottleResult(false, retryAfter);
        }

        // Check daily limit
        var dailyKey = $"{KeyPrefix}:{userId}:daily";
        var dailyCount = (int)(db.StringGet(dailyKey));

        if (dailyCount >= limits.Daily)
        {
            var midnightUtc = DateTimeOffset.UtcNow.Date.AddDays(1);
            int retryAfter = Math.Max(1, (int)(midnightUtc - DateTimeOffset.UtcNow).TotalSeconds);
            return new ThrottleResult(false, retryAfter);
        }

        return new ThrottleResult(true);
    }

    public void RecordSend(string userId, MessageRole role)
    {
        if (role == MessageRole.Student) return;

        var db = _redis.GetDatabase();

        // INCR hourly counter with 1-hour TTL
        var hourlyKey = $"{KeyPrefix}:{userId}:hourly";
        db.StringIncrement(hourlyKey);
        // Set TTL only if key is new (no existing TTL)
        if (db.KeyTimeToLive(hourlyKey) is null or { TotalSeconds: < 0 })
            db.KeyExpire(hourlyKey, TimeSpan.FromHours(1));

        // INCR daily counter with TTL until midnight UTC
        var dailyKey = $"{KeyPrefix}:{userId}:daily";
        db.StringIncrement(dailyKey);
        if (db.KeyTimeToLive(dailyKey) is null or { TotalSeconds: < 0 })
        {
            var midnightUtc = DateTimeOffset.UtcNow.Date.AddDays(1);
            var ttl = midnightUtc - DateTimeOffset.UtcNow;
            db.KeyExpire(dailyKey, ttl);
        }
    }

    public void Reset(string userId)
    {
        var db = _redis.GetDatabase();
        db.KeyDelete($"{KeyPrefix}:{userId}:hourly");
        db.KeyDelete($"{KeyPrefix}:{userId}:daily");
    }
}
