// =============================================================================
// Cena Platform — KAnonymityEnforcer tests (prr-026)
//
// Guards:
//   - Group size >= k passes silently (no metric).
//   - Group size < k throws InsufficientAnonymityException and increments
//     cena_k_anonymity_suppressed_total{surface, k}.
//   - MeetsFloor short-circuits once k distinct elements are seen (O(k)).
//   - Null group is treated as size 0 and fails the floor.
//   - Non-positive k is rejected (ArgumentOutOfRangeException on Assert*).
//   - Distinct counting respects the optional IEqualityComparer<T>.
//   - Recording suppression separately fires the counter without throwing.
//   - NullKAnonymityEnforcer is stateless and does not throw on any input.
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Infrastructure.Analytics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Infrastructure.Tests.Analytics;

public sealed class KAnonymityEnforcerTests
{
    private sealed class CapturingMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public Meter Create(string name, string? version = null) =>
            new(new MeterOptions(name) { Version = version });
        public void Dispose() { }
    }

    private static (KAnonymityEnforcer enforcer,
                    List<(long value, Dictionary<string, object?> tags)> emitted,
                    MeterListener listener)
        BuildEnforcer()
    {
        var factory = new CapturingMeterFactory();
        var enforcer = new KAnonymityEnforcer(factory, NullLogger<KAnonymityEnforcer>.Instance);
        var emitted = new List<(long, Dictionary<string, object?>)>();

        var listener = new MeterListener
        {
            InstrumentPublished = (inst, lst) =>
            {
                if (inst.Name == KAnonymityEnforcer.CounterName)
                    lst.EnableMeasurementEvents(inst);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var dict = new Dictionary<string, object?>();
            foreach (var t in tags) dict[t.Key] = t.Value;
            emitted.Add((value, dict));
        });
        listener.Start();

        return (enforcer, emitted, listener);
    }

    [Fact]
    public void MeetsFloor_Returns_True_When_Group_Size_At_Or_Above_K()
    {
        var (enforcer, _, listener) = BuildEnforcer();
        using var _ = listener;

        // Exactly k.
        Assert.True(enforcer.MeetsFloor(Enumerable.Range(1, 10), 10));
        // Greater than k.
        Assert.True(enforcer.MeetsFloor(Enumerable.Range(1, 50), 10));
    }

    [Fact]
    public void MeetsFloor_Returns_False_When_Group_Size_Below_K()
    {
        var (enforcer, _, listener) = BuildEnforcer();
        using var _ = listener;

        Assert.False(enforcer.MeetsFloor(Enumerable.Range(1, 9), 10));
        Assert.False(enforcer.MeetsFloor(Array.Empty<int>(), 10));
        Assert.False(enforcer.MeetsFloor<int>(null, 10));
    }

    [Fact]
    public void MeetsFloor_Counts_Distinct_Members()
    {
        var (enforcer, _, listener) = BuildEnforcer();
        using var _ = listener;

        // 10 duplicates of a single student -> distinct count is 1, not 10.
        var duplicated = Enumerable.Repeat("stu-1", 10);
        Assert.False(enforcer.MeetsFloor(duplicated, 2));

        // A real 10-student roster passes.
        var roster = Enumerable.Range(0, 10).Select(i => $"stu-{i}");
        Assert.True(enforcer.MeetsFloor(roster, 10));
    }

    [Fact]
    public void MeetsFloor_Respects_Equality_Comparer()
    {
        var (enforcer, _, listener) = BuildEnforcer();
        using var _ = listener;

        // Case-insensitive comparer should treat "stu-1" and "STU-1" as one.
        var group = new[] { "stu-1", "STU-1", "stu-2", "stu-3" };
        Assert.False(enforcer.MeetsFloor(group, 4, StringComparer.OrdinalIgnoreCase));
        Assert.True(enforcer.MeetsFloor(group, 4, StringComparer.Ordinal));
    }

    [Fact]
    public void AssertMinimumGroupSize_Throws_And_Increments_Counter_When_Below_Floor()
    {
        var (enforcer, emitted, listener) = BuildEnforcer();
        using var _ = listener;

        var ex = Assert.Throws<InsufficientAnonymityException>(() =>
            enforcer.AssertMinimumGroupSize(
                Enumerable.Range(1, 3),
                10,
                "/classrooms/{id}/analytics/aggregate",
                EqualityComparer<int>.Default));

        Assert.Equal("/classrooms/{id}/analytics/aggregate", ex.Surface);
        Assert.Equal(10, ex.K);

        Assert.Single(emitted);
        var (value, tags) = emitted[0];
        Assert.Equal(1, value);
        Assert.Equal("/classrooms/{id}/analytics/aggregate", tags["surface"]);
        Assert.Equal(10, tags["k"]);
    }

    [Fact]
    public void AssertMinimumGroupSize_Silent_When_At_Or_Above_Floor()
    {
        var (enforcer, emitted, listener) = BuildEnforcer();
        using var _ = listener;

        enforcer.AssertMinimumGroupSize(
            Enumerable.Range(1, 10),
            10,
            "/classrooms/{id}/analytics/aggregate");

        Assert.Empty(emitted);
    }

    [Fact]
    public void AssertMinimumGroupSize_Rejects_Non_Positive_K()
    {
        var (enforcer, _, listener) = BuildEnforcer();
        using var _ = listener;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            enforcer.AssertMinimumGroupSize(new[] { 1 }, 0, "/x"));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            enforcer.AssertMinimumGroupSize(new[] { 1 }, -1, "/x"));
    }

    [Fact]
    public void AssertMinimumGroupSize_Rejects_Empty_Surface()
    {
        var (enforcer, _, listener) = BuildEnforcer();
        using var _ = listener;

        Assert.Throws<ArgumentException>(() =>
            enforcer.AssertMinimumGroupSize(new[] { 1 }, 10, ""));
    }

    [Fact]
    public void RecordSuppression_Fires_Counter_Without_Throwing()
    {
        var (enforcer, emitted, listener) = BuildEnforcer();
        using var _ = listener;

        enforcer.RecordSuppression("/classrooms/{id}/analytics/topic", 10);

        Assert.Single(emitted);
        var (value, tags) = emitted[0];
        Assert.Equal(1, value);
        Assert.Equal("/classrooms/{id}/analytics/topic", tags["surface"]);
        Assert.Equal(10, tags["k"]);
    }

    [Fact]
    public void InsufficientAnonymityException_Does_Not_Leak_Group_Size_In_Message()
    {
        // The exception message must NEVER include the actual cohort size
        // — a message like "class had 3 of 10 required" is itself a
        // k-anonymity leak (attacker probing "is size<k?" gets a yes with
        // the exact size).
        var ex = new InsufficientAnonymityException("/surface", 10);
        Assert.DoesNotContain("3", ex.Message);
        Assert.DoesNotContain("size", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("below_anonymity_floor", ex.Message);
    }

    [Fact]
    public void NullKAnonymityEnforcer_Is_NoOp()
    {
        var enforcer = NullKAnonymityEnforcer.Instance;

        // Never throws regardless of input shape.
        enforcer.AssertMinimumGroupSize(Array.Empty<int>(), 10, "/x");
        Assert.True(enforcer.MeetsFloor<int>(null, 10));
        enforcer.RecordSuppression("/x", 10);
    }
}
