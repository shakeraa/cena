// =============================================================================
// Cena Platform — SandboxPaymentGateway tests (EPIC-PRR-I PRR-301 dev/test)
// =============================================================================

using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class SandboxPaymentGatewayTests
{
    private readonly SandboxPaymentGateway _sut = new();

    [Fact]
    public async Task Default_intent_succeeds_with_stable_transaction_id()
    {
        var intent = Intent("ik-001");
        var r = await _sut.AuthorizeAsync(intent, CancellationToken.None);
        Assert.True(r.Succeeded);
        Assert.Equal("sandbox:ik-001", r.TransactionId);
    }

    [Fact]
    public async Task Replay_same_key_yields_same_result()
    {
        var i1 = Intent("ik-replay");
        var r1 = await _sut.AuthorizeAsync(i1, CancellationToken.None);
        var r2 = await _sut.AuthorizeAsync(i1, CancellationToken.None);
        Assert.Equal(r1.TransactionId, r2.TransactionId);
        Assert.True(r1.Succeeded && r2.Succeeded);
    }

    [Fact]
    public async Task Fail_prefix_returns_deterministic_failure()
    {
        var i = Intent("fail-budget-exhausted");
        var r = await _sut.AuthorizeAsync(i, CancellationToken.None);
        Assert.False(r.Succeeded);
        Assert.NotNull(r.FailureReason);
        Assert.Null(r.TransactionId);
    }

    [Fact]
    public async Task Missing_idempotency_key_throws()
    {
        var bad = new PaymentIntent("enc::p", Money.FromAgorot(100), PaymentIntentKind.Activation, IdempotencyKey: "");
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.AuthorizeAsync(bad, CancellationToken.None));
    }

    [Fact]
    public async Task Observed_intents_are_recorded()
    {
        await _sut.AuthorizeAsync(Intent("ik-A"), CancellationToken.None);
        await _sut.AuthorizeAsync(Intent("ik-B"), CancellationToken.None);
        Assert.Equal(2, _sut.ObservedIntents.Count);
    }

    private static PaymentIntent Intent(string key) => new(
        ParentSubjectIdEncrypted: "enc::parent",
        GrossAmount: Money.FromAgorot(24_900),
        Kind: PaymentIntentKind.Activation,
        IdempotencyKey: key);
}
