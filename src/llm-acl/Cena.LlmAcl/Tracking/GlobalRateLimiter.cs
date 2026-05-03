using System.Collections.Concurrent;

namespace Cena.LlmAcl.Tracking;

public class GlobalRateLimiter
{
    private readonly ConcurrentDictionary<string, SlidingWindow> _windows = new();

    public bool TryAcquire(string modelTier, int estimatedTokens)
    {
        var window = _windows.GetOrAdd(modelTier, tier => new SlidingWindow(
            tier switch
            {
                "haiku" => 500_000,
                "sonnet" => 100_000,
                "opus" => 50_000,
                _ => 100_000
            },
            TimeSpan.FromMinutes(1)));

        return window.TryConsume(estimatedTokens);
    }

    public (int Used, int Limit) GetUsage(string modelTier)
    {
        if (_windows.TryGetValue(modelTier, out var window))
            return (window.CurrentUsage, window.Limit);
        return (0, 0);
    }
}

internal class SlidingWindow
{
    private readonly int _limit;
    private readonly TimeSpan _windowSize;
    private readonly ConcurrentQueue<(DateTimeOffset Timestamp, int Tokens)> _entries = new();
    private int _currentTotal;

    public int Limit => _limit;
    public int CurrentUsage => _currentTotal;

    public SlidingWindow(int limit, TimeSpan windowSize)
    {
        _limit = limit;
        _windowSize = windowSize;
    }

    public bool TryConsume(int tokens)
    {
        Prune();
        if (_currentTotal + tokens > _limit)
            return false;

        _entries.Enqueue((DateTimeOffset.UtcNow, tokens));
        Interlocked.Add(ref _currentTotal, tokens);
        return true;
    }

    private void Prune()
    {
        var cutoff = DateTimeOffset.UtcNow - _windowSize;
        while (_entries.TryPeek(out var entry) && entry.Timestamp < cutoff)
        {
            if (_entries.TryDequeue(out var removed))
                Interlocked.Add(ref _currentTotal, -removed.Tokens);
        }
    }
}
