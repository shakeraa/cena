// =============================================================================
// Cena Platform — Money value type for retail subscriptions (EPIC-PRR-I)
//
// All retail money is represented in agorot (1/100 of an Israeli shekel) as a
// 64-bit integer. Rationale (ADR-0057 §4):
//   - Float/decimal drift on VAT math is a classical customer-visible-error
//     source; agorot-long arithmetic is closed-form.
//   - Stripe and most gateways expect the smallest currency unit; agorot is
//     native on the wire.
//   - Serializing a primitive `long` avoids JSON-decimal ambiguity.
//
// Equality is structural via the record. Currency is locked to ILS at launch;
// the currency field exists so a multi-currency expansion does not require
// a schema migration (memory "PWA over Flutter" confirms Israel-first).
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Supported retail currencies. Launch: ILS only. Additional currencies
/// require multi-currency pricing policy (not in ADR-0057 scope).
/// </summary>
public enum Currency
{
    /// <summary>Israeli new shekel. Smallest unit = agora (1/100 ILS).</summary>
    Ils = 0,
}

/// <summary>
/// Immutable money value in the smallest currency unit. For ILS: agorot.
/// </summary>
/// <param name="Amount">Integer amount in smallest unit. Always non-negative
/// in retail subscription contexts; sign discipline is on callers.</param>
/// <param name="Currency">ISO-ish enum; ILS at launch.</param>
public sealed record Money(long Amount, Currency Currency)
{
    /// <summary>Zero ILS.</summary>
    public static readonly Money ZeroIls = new(0, Currency.Ils);

    /// <summary>Construct an ILS money from agorot.</summary>
    public static Money FromAgorot(long agorot) => new(agorot, Currency.Ils);

    /// <summary>
    /// Construct ILS money from whole shekels. Utility only — real prices
    /// come from <see cref="TierCatalog"/> constants in agorot.
    /// </summary>
    public static Money FromShekels(long shekels) => new(checked(shekels * 100L), Currency.Ils);

    /// <summary>
    /// Add two money values. Throws if currencies differ, enforcing that no
    /// caller accidentally mixes ILS with a future currency.
    /// </summary>
    public Money Add(Money other)
    {
        if (other.Currency != Currency)
        {
            throw new InvalidOperationException(
                $"Cannot add {Currency} to {other.Currency}; currency mismatch.");
        }
        return new Money(checked(Amount + other.Amount), Currency);
    }

    /// <summary>Subtract two money values. Currency-check identical to Add.</summary>
    public Money Subtract(Money other)
    {
        if (other.Currency != Currency)
        {
            throw new InvalidOperationException(
                $"Cannot subtract {other.Currency} from {Currency}; currency mismatch.");
        }
        return new Money(checked(Amount - other.Amount), Currency);
    }

    /// <summary>Scale by an integer factor (e.g., 12 months of monthly price).</summary>
    public Money Multiply(long factor) => new(checked(Amount * factor), Currency);
}
