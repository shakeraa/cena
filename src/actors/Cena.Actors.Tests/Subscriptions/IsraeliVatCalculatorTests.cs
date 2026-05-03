// =============================================================================
// Cena Platform — IsraeliVatCalculator tests (EPIC-PRR-I, ADR-0057 §5)
//
// Boundary and exactness coverage. The gross==net+vat invariant must hold
// for every value, not just round numbers.
// =============================================================================

using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class IsraeliVatCalculatorTests
{
    [Fact]
    public void Zero_gross_decomposes_to_zero_net_zero_vat()
    {
        var d = IsraeliVatCalculator.DecomposeGross(Money.ZeroIls);
        Assert.Equal(0L, d.Net.Amount);
        Assert.Equal(0L, d.Vat.Amount);
    }

    [Theory]
    [InlineData(24_900L)]   // ₪249 Premium
    [InlineData(22_900L)]   // ₪229 Plus
    [InlineData(7_900L)]    // ₪79 Basic
    [InlineData(249_000L)]  // annual Premium
    [InlineData(14_900L)]   // sibling 1-2
    [InlineData(9_900L)]    // sibling 3+
    [InlineData(1L)]        // smallest non-zero
    [InlineData(123_456_789L)]
    public void Gross_equals_net_plus_vat_always(long grossAgorot)
    {
        var d = IsraeliVatCalculator.DecomposeGross(Money.FromAgorot(grossAgorot));
        Assert.Equal(grossAgorot, d.Net.Amount + d.Vat.Amount);
    }

    [Fact]
    public void Premium_249_shekels_decomposes_net_21282_vat_3618_agorot()
    {
        // 24900 / 1.17 = 21282.05... → half-up → 21282 net; 24900-21282 = 3618 vat.
        var d = IsraeliVatCalculator.DecomposeGross(Money.FromAgorot(24_900L));
        Assert.Equal(21_282L, d.Net.Amount);
        Assert.Equal(3_618L, d.Vat.Amount);
    }

    [Fact]
    public void Negative_numerator_throws()
    {
        Assert.Throws<ArgumentException>(() =>
            IsraeliVatCalculator.DecomposeGross(new Money(-1L, Currency.Ils)));
    }

    [Fact]
    public void Non_ils_currency_throws()
    {
        // Make a fake currency by casting; defensive coverage for future expansion.
        var bogus = new Money(100L, (Currency)99);
        Assert.Throws<InvalidOperationException>(() =>
            IsraeliVatCalculator.DecomposeGross(bogus));
    }

    [Fact]
    public void Vat_basis_points_is_1700_at_launch()
    {
        Assert.Equal(1700, IsraeliVatCalculator.VatBasisPoints);
    }
}
