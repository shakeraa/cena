// =============================================================================
// Cena Platform — RedisSessionStoreMetricsService unit tests (prr-020)
//
// Light-touch tests. The service does real Redis calls for its happy path
// (covered by the staging smoke test); here we exercise the pure helpers
// so delta/reseed logic has coverage independent of StackExchange.Redis
// availability.
// =============================================================================

using System.Collections.Generic;
using System.Linq;
using Cena.Infrastructure.Observability;

namespace Cena.Infrastructure.Tests.Observability;

public sealed class RedisSessionStoreMetricsServiceTests
{
    [Fact]
    public void ReadLong_FindsValueAcrossSections()
    {
        var info = new IGrouping<string, KeyValuePair<string, string>>[]
        {
            new FakeGrouping("memory", new[]
            {
                new KeyValuePair<string, string>("used_memory", "42"),
                new KeyValuePair<string, string>("maxmemory", "100"),
            }),
        };

        Assert.Equal(42, RedisSessionStoreMetricsService.ReadLong(info, "used_memory"));
        Assert.Equal(100, RedisSessionStoreMetricsService.ReadLong(info, "maxmemory"));
    }

    [Fact]
    public void ReadLong_ReturnsZero_WhenKeyMissing()
    {
        var info = new IGrouping<string, KeyValuePair<string, string>>[]
        {
            new FakeGrouping("memory", new[]
            {
                new KeyValuePair<string, string>("used_memory", "42"),
            }),
        };

        Assert.Equal(0, RedisSessionStoreMetricsService.ReadLong(info, "evicted_keys"));
    }

    [Fact]
    public void ReadLong_ReturnsZero_WhenValueNonNumeric()
    {
        var info = new IGrouping<string, KeyValuePair<string, string>>[]
        {
            new FakeGrouping("memory", new[]
            {
                new KeyValuePair<string, string>("used_memory", "garbage"),
            }),
        };

        Assert.Equal(0, RedisSessionStoreMetricsService.ReadLong(info, "used_memory"));
    }

    private sealed class FakeGrouping : IGrouping<string, KeyValuePair<string, string>>
    {
        private readonly IReadOnlyList<KeyValuePair<string, string>> _items;
        public FakeGrouping(string key, IReadOnlyList<KeyValuePair<string, string>> items)
        {
            Key = key;
            _items = items;
        }
        public string Key { get; }
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
