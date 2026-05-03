// =============================================================================
// Cena Platform — InMemoryProcessedWebhookLog tests (EPIC-PRR-I PRR-301)
// =============================================================================

using Cena.Actors.Subscriptions.Stripe;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions.Stripe;

public class InMemoryProcessedWebhookLogTests
{
    private readonly InMemoryProcessedWebhookLog _sut = new();

    [Fact]
    public async Task First_register_returns_true()
    {
        Assert.True(await _sut.TryRegisterAsync("evt_001", CancellationToken.None));
    }

    [Fact]
    public async Task Second_register_of_same_id_returns_false()
    {
        await _sut.TryRegisterAsync("evt_002", CancellationToken.None);
        Assert.False(await _sut.TryRegisterAsync("evt_002", CancellationToken.None));
    }

    [Fact]
    public async Task Distinct_ids_independent()
    {
        Assert.True(await _sut.TryRegisterAsync("evt_A", CancellationToken.None));
        Assert.True(await _sut.TryRegisterAsync("evt_B", CancellationToken.None));
    }

    [Fact]
    public async Task Empty_id_throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.TryRegisterAsync("", CancellationToken.None));
    }
}
