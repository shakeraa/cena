// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Message Throttler
// Layer: Domain Service | Runtime: .NET 9
// Per-role rate limiting for messaging. Uses in-memory tracking.
// Production should use Redis counters (see MSG-005.4).
// ═══════════════════════════════════════════════════════════════════════

using System.Collections.Concurrent;

namespace Cena.Actors.Messaging;

public interface IMessageThrottler
{
    ThrottleResult Check(string userId, MessageRole role);
    void RecordSend(string userId, MessageRole role);
    void Reset(string userId);
}

public sealed class MessageThrottler : IMessageThrottler
{
    // Per-role limits
    private static readonly Dictionary<MessageRole, (int Daily, int Hourly)> Limits = new()
    {
        [MessageRole.Teacher] = (Daily: 100, Hourly: 30),
        [MessageRole.Parent] = (Daily: 10, Hourly: 5),
        [MessageRole.Student] = (Daily: 0, Hourly: 0),
        [MessageRole.System] = (Daily: int.MaxValue, Hourly: int.MaxValue),
    };

    private readonly ConcurrentDictionary<string, UserSendState> _state = new();

    public ThrottleResult Check(string userId, MessageRole role)
    {
        if (role == MessageRole.Student)
            return new ThrottleResult(false, RetryAfterSeconds: 0);

        if (role == MessageRole.System)
            return new ThrottleResult(true);

        var limits = Limits[role];
        var state = _state.GetOrAdd(userId, _ => new UserSendState());

        state.PruneExpired();

        if (state.HourlySends >= limits.Hourly)
        {
            var oldest = state.HourlyOldestSendUtc;
            int retryAfter = oldest.HasValue
                ? Math.Max(1, (int)(oldest.Value.AddHours(1) - DateTimeOffset.UtcNow).TotalSeconds)
                : 3600;
            return new ThrottleResult(false, retryAfter);
        }

        if (state.DailySends >= limits.Daily)
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

        var state = _state.GetOrAdd(userId, _ => new UserSendState());
        state.Record(DateTimeOffset.UtcNow);
    }

    public void Reset(string userId)
    {
        _state.TryRemove(userId, out _);
    }

    private sealed class UserSendState
    {
        private readonly List<DateTimeOffset> _sends = new();
        private readonly object _lock = new();

        public int DailySends
        {
            get
            {
                lock (_lock)
                {
                    var today = DateTimeOffset.UtcNow.Date;
                    return _sends.Count(s => s.Date == today);
                }
            }
        }

        public int HourlySends
        {
            get
            {
                lock (_lock)
                {
                    var oneHourAgo = DateTimeOffset.UtcNow.AddHours(-1);
                    return _sends.Count(s => s >= oneHourAgo);
                }
            }
        }

        public DateTimeOffset? HourlyOldestSendUtc
        {
            get
            {
                lock (_lock)
                {
                    var oneHourAgo = DateTimeOffset.UtcNow.AddHours(-1);
                    return _sends.Where(s => s >= oneHourAgo).OrderBy(s => s).FirstOrDefault();
                }
            }
        }

        public void Record(DateTimeOffset timestamp)
        {
            lock (_lock) { _sends.Add(timestamp); }
        }

        public void PruneExpired()
        {
            lock (_lock)
            {
                var yesterday = DateTimeOffset.UtcNow.AddDays(-1);
                _sends.RemoveAll(s => s < yesterday);
            }
        }
    }
}
