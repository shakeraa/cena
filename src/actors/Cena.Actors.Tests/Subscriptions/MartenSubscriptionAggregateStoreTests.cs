// =============================================================================
// Cena Platform — MartenSubscriptionAggregateStore contract tests
//
// Tests the InMemory store behavior against the store interface contract.
// A separate integration test against real Marten lives under /tests/ when
// the postgres fixture is wired for this bounded context (follow-up).
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class InMemorySubscriptionAggregateStoreContractTests
{
    private readonly ISubscriptionAggregateStore _store = new InMemorySubscriptionAggregateStore();
    private const string ParentId = "p-contract-001";

    [Fact]
    public async Task Load_on_fresh_parent_returns_empty_aggregate()
    {
        var agg = await _store.LoadAsync(ParentId, CancellationToken.None);
        Assert.Equal(SubscriptionStatus.Unsubscribed, agg.State.Status);
    }

    [Fact]
    public async Task Append_and_load_preserves_order()
    {
        var e1 = new SubscriptionActivated_V1(
            "enc::parent", "enc::primary",
            SubscriptionTier.Premium, BillingCycle.Monthly,
            24_900, "enc::txn-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMonths(1));
        var e2 = new SiblingEntitlementLinked_V1(
            "enc::parent", "enc::sib-1", 1, SubscriptionTier.Premium,
            14_900, DateTimeOffset.UtcNow);

        await _store.AppendAsync(ParentId, e1, CancellationToken.None);
        await _store.AppendAsync(ParentId, e2, CancellationToken.None);

        var events = await _store.ReadEventsAsync(ParentId, CancellationToken.None);
        Assert.Equal(2, events.Count);
        Assert.IsType<SubscriptionActivated_V1>(events[0]);
        Assert.IsType<SiblingEntitlementLinked_V1>(events[1]);

        var agg = await _store.LoadAsync(ParentId, CancellationToken.None);
        Assert.Equal(SubscriptionStatus.Active, agg.State.Status);
        Assert.Equal(2, agg.State.LinkedStudents.Count);
    }

    [Fact]
    public async Task Read_events_for_unknown_parent_returns_empty()
    {
        var events = await _store.ReadEventsAsync("unknown-parent-xxx", CancellationToken.None);
        Assert.Empty(events);
    }

    [Fact]
    public async Task Append_null_throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _store.AppendAsync(ParentId, null!, CancellationToken.None));
    }
}
