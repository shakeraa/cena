// =============================================================================
// Cena Platform — Israeli VAT calculator (EPIC-PRR-I, ADR-0057 §5)
//
// Israeli consumer VAT is 17% and retail prices are displayed INCLUSIVE of
// VAT (₪249 = gross, net + vat = ₪249). This calculator is a pure function;
// closed-form integer arithmetic on agorot avoids float drift. See
// IsraeliVatCalculatorTests for boundary-case coverage.
//
// The rounding convention is "net-rounded, vat-derived":
//   net = floor( gross * 100 / 117 ) + half-up carry
//   vat = gross - net
// This guarantees gross == net + vat exactly (no off-by-one in the display).
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Pure Israeli consumer VAT math. VAT rate = 17% at launch. When the rate
/// changes (statutory), update <see cref="VatBasisPoints"/> and bump a test
/// fixture; no downstream callers need to change.
/// </summary>
public static class IsraeliVatCalculator
{
    /// <summary>VAT rate in basis points. 1700 bps = 17.00%.</summary>
    public const int VatBasisPoints = 1700;

    /// <summary>Denominator for gross→net conversion: 100 + (VatBasisPoints/100).</summary>
    private const long GrossToNetDenominator = 10000L + VatBasisPoints;   // 11700
    private const long GrossToNetNumerator = 10000L;                      // 10000

    /// <summary>
    /// Decomposition of a VAT-inclusive gross amount into net + VAT. The
    /// decomposition is exact: <c>Gross == Net + Vat</c> always.
    /// </summary>
    /// <param name="gross">VAT-inclusive amount (ILS agorot).</param>
    /// <returns>Net + VAT components.</returns>
    public static VatDecomposition DecomposeGross(Money gross)
    {
        if (gross.Currency != Currency.Ils)
        {
            throw new InvalidOperationException(
                $"Israeli VAT calculator requires ILS; got {gross.Currency}.");
        }

        long netAgorot = HalfUpDivide(gross.Amount * GrossToNetNumerator, GrossToNetDenominator);
        long vatAgorot = gross.Amount - netAgorot;
        return new VatDecomposition(
            Gross: gross,
            Net: Money.FromAgorot(netAgorot),
            Vat: Money.FromAgorot(vatAgorot));
    }

    /// <summary>
    /// Half-up integer division. Rounds 0.5 to 1 (banker's-free, predictable
    /// for invoices). Positive-only (retail subscription amounts are never
    /// negative).
    /// </summary>
    private static long HalfUpDivide(long numerator, long denominator)
    {
        if (denominator <= 0)
        {
            throw new ArgumentException("Denominator must be positive.", nameof(denominator));
        }
        if (numerator < 0)
        {
            throw new ArgumentException(
                "Retail VAT math is non-negative; got negative numerator.", nameof(numerator));
        }
        return (numerator + denominator / 2) / denominator;
    }
}

/// <summary>
/// Exact VAT decomposition. <c>Gross.Amount == Net.Amount + Vat.Amount</c>.
/// </summary>
public sealed record VatDecomposition(Money Gross, Money Net, Money Vat);
