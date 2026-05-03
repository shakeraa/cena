// =============================================================================
// Cena Platform — Money value-type tests (EPIC-PRR-I, ADR-0057 §4)
// =============================================================================

using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class MoneyTests
{
    [Fact]
    public void FromShekels_multiplies_by_100()
    {
        var m = Money.FromShekels(249L);
        Assert.Equal(24_900L, m.Amount);
        Assert.Equal(Currency.Ils, m.Currency);
    }

    [Fact]
    public void Add_rejects_currency_mismatch()
    {
        var ils = Money.FromAgorot(100);
        var fake = new Money(100, (Currency)99);
        Assert.Throws<InvalidOperationException>(() => ils.Add(fake));
    }

    [Fact]
    public void Multiply_scales_correctly()
    {
        var m = Money.FromAgorot(7_900L);
        var annual = m.Multiply(10L);
        Assert.Equal(79_000L, annual.Amount);
    }

    [Fact]
    public void Zero_is_ils()
    {
        Assert.Equal(0L, Money.ZeroIls.Amount);
        Assert.Equal(Currency.Ils, Money.ZeroIls.Currency);
    }
}
