// =============================================================================
// Cena Platform — Parametric Slot DSL (prr-200, ADR-0002, ADR-0032)
//
// A parametric template carries a finite set of named slots. Each slot declares
// the legal value space the compiler draws from. Strategy 1 (no-LLM) requires
// the slot space to be enumerable deterministically so that (template, seed)
// uniquely identifies a variant.
//
// Non-negotiables:
//   * No randomness outside the seeded RNG handed in at compile time.
//   * No cross-slot coupling inside the slot itself — coupling is expressed
//     at the template level via SlotConstraints (see ParametricTemplate).
//   * Slot value generators return rational numbers by default; the renderer
//     format-specifies as integer / rational / decimal per accept_shapes.
// =============================================================================

using System.Numerics;

namespace Cena.Actors.QuestionBank.Templates;

/// <summary>
/// Shape of a slot's value space. Pure enumerable sets only.
/// </summary>
public enum ParametricSlotKind
{
    /// <summary>Integer in [min, max], inclusive, with optional exclude set.</summary>
    Integer,

    /// <summary>
    /// Rational p/q where p ∈ [numMin, numMax], q ∈ [denMin, denMax], q ≠ 0.
    /// Useful for coefficients that must be non-integer (fractions).
    /// </summary>
    Rational,

    /// <summary>
    /// Categorical pick from an explicit string list (e.g. variable names
    /// "x", "y", "t"). Order is preserved; the compiler picks index % count.
    /// </summary>
    Choice
}

/// <summary>
/// Immutable declaration of one named slot in a template.
///
/// Equality semantics are value-based so two compilers on the same template
/// hash to the same fingerprint — this is part of the determinism contract.
/// </summary>
public sealed record ParametricSlot
{
    public required string Name { get; init; }
    public required ParametricSlotKind Kind { get; init; }

    // ── Integer slot config ──
    public int IntegerMin { get; init; }
    public int IntegerMax { get; init; }
    public IReadOnlyList<int> IntegerExclude { get; init; } = Array.Empty<int>();

    // ── Rational slot config ──
    public int NumeratorMin { get; init; }
    public int NumeratorMax { get; init; }
    public int DenominatorMin { get; init; } = 1;
    public int DenominatorMax { get; init; } = 1;
    public bool ReduceRational { get; init; } = true;

    // ── Choice slot config ──
    public IReadOnlyList<string> Choices { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Upper bound on the enumerable cardinality of this slot. Used by the
    /// compiler to detect InsufficientSlotSpace before it burns seeds.
    /// For Rational we over-count (p×q with duplicates); the real post-reduce
    /// cardinality is computed by the deduper after compilation.
    /// </summary>
    public long CardinalityUpperBound() => Kind switch
    {
        ParametricSlotKind.Integer   => Math.Max(0L, (long)IntegerMax - IntegerMin + 1L - IntegerExclude.Count),
        ParametricSlotKind.Rational  => Math.Max(0L, ((long)NumeratorMax - NumeratorMin + 1L)
                                                    * ((long)DenominatorMax - DenominatorMin + 1L)),
        ParametricSlotKind.Choice    => Choices.Count,
        _                            => 0L
    };

    /// <summary>
    /// Draw one value from this slot using the seeded RNG. Returned as a
    /// <see cref="ParametricSlotValue"/> tagged with the originating slot
    /// kind so the renderer can format-specify appropriately.
    /// </summary>
    /// <remarks>
    /// MUST be a pure function of the RNG state. Do NOT call Random.Shared,
    /// Guid.NewGuid, or DateTime.UtcNow here.
    /// </remarks>
    public ParametricSlotValue Draw(Random rng)
    {
        switch (Kind)
        {
            case ParametricSlotKind.Integer:
            {
                if (IntegerMax < IntegerMin)
                    throw new ArgumentException(
                        $"Slot '{Name}' integer range invalid: max {IntegerMax} < min {IntegerMin}");

                // Rejection-sample the exclude set. Bounded iterations so we
                // never loop forever on an empty residual space.
                var span = IntegerMax - IntegerMin + 1;
                if (span <= 0) throw new ArgumentException($"Slot '{Name}' empty integer range");
                var maxAttempts = span * 4; // generous upper bound
                for (var i = 0; i < maxAttempts; i++)
                {
                    var candidate = IntegerMin + rng.Next(span);
                    if (!IntegerExclude.Contains(candidate))
                        return ParametricSlotValue.Integer(Name, candidate);
                }
                throw new InvalidOperationException(
                    $"Slot '{Name}' exclude set eliminates the entire range [{IntegerMin},{IntegerMax}]");
            }

            case ParametricSlotKind.Rational:
            {
                if (NumeratorMax < NumeratorMin || DenominatorMax < DenominatorMin)
                    throw new ArgumentException($"Slot '{Name}' rational range invalid");

                var num = NumeratorMin + rng.Next(NumeratorMax - NumeratorMin + 1);
                int den;
                var denAttempts = (DenominatorMax - DenominatorMin + 1) * 4;
                for (var i = 0; ; i++)
                {
                    if (i >= denAttempts)
                        throw new InvalidOperationException(
                            $"Slot '{Name}' denominator range excludes 0 but yields no legal draw");
                    den = DenominatorMin + rng.Next(DenominatorMax - DenominatorMin + 1);
                    if (den != 0) break;
                }

                if (ReduceRational)
                {
                    var g = (int)BigInteger.GreatestCommonDivisor(Math.Abs(num), Math.Abs(den));
                    if (g > 1) { num /= g; den /= g; }
                    if (den < 0) { num = -num; den = -den; }
                }

                return ParametricSlotValue.Rational(Name, num, den);
            }

            case ParametricSlotKind.Choice:
            {
                if (Choices.Count == 0)
                    throw new ArgumentException($"Slot '{Name}' choice list empty");
                var idx = rng.Next(Choices.Count);
                return ParametricSlotValue.Choice(Name, Choices[idx]);
            }

            default:
                throw new ArgumentOutOfRangeException($"Unknown slot kind {Kind}");
        }
    }

    /// <summary>
    /// Structural validity check. Throws with a descriptive message if the
    /// slot is malformed. Called by the compiler before drawing any values.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("Slot name required");

        switch (Kind)
        {
            case ParametricSlotKind.Integer:
                if (IntegerMax < IntegerMin)
                    throw new ArgumentException($"Slot '{Name}' integer range invalid");
                if (CardinalityUpperBound() <= 0)
                    throw new ArgumentException($"Slot '{Name}' integer range yields no legal values");
                break;
            case ParametricSlotKind.Rational:
                if (NumeratorMax < NumeratorMin || DenominatorMax < DenominatorMin)
                    throw new ArgumentException($"Slot '{Name}' rational range invalid");
                if (DenominatorMin == 0 && DenominatorMax == 0)
                    throw new ArgumentException($"Slot '{Name}' denominator range is exactly {{0}}");
                break;
            case ParametricSlotKind.Choice:
                if (Choices.Count == 0)
                    throw new ArgumentException($"Slot '{Name}' choice list empty");
                break;
        }
    }
}

/// <summary>
/// A drawn value for a named slot. Immutable. The <see cref="Kind"/> field is
/// authoritative for rendering — a rational slot whose p/q reduces to an
/// integer still renders as an integer through <see cref="ToIntegerOrNull"/>.
/// </summary>
public readonly record struct ParametricSlotValue(
    string Name,
    ParametricSlotKind Kind,
    long Numerator,
    long Denominator,
    string? ChoiceValue)
{
    public static ParametricSlotValue Integer(string name, int value) =>
        new(name, ParametricSlotKind.Integer, value, 1, null);

    public static ParametricSlotValue Rational(string name, int num, int den) =>
        new(name, ParametricSlotKind.Rational, num, den, null);

    public static ParametricSlotValue Choice(string name, string value) =>
        new(name, ParametricSlotKind.Choice, 0, 1, value);

    /// <summary>
    /// Canonical string used when substituted into a template. Integers render
    /// plainly, rationals as `(p/q)` parenthesised so operator precedence is
    /// preserved when embedded in a larger expression.
    /// </summary>
    public string ToExpressionString() => Kind switch
    {
        ParametricSlotKind.Integer  => Numerator.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ParametricSlotKind.Rational when Denominator == 1 =>
            Numerator.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ParametricSlotKind.Rational => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"({Numerator}/{Denominator})"),
        ParametricSlotKind.Choice   => ChoiceValue ?? "",
        _                           => ""
    };

    /// <summary>True if this value represents an integer (numerator/1, reduced).</summary>
    public bool IsIntegral() =>
        Kind == ParametricSlotKind.Integer
        || (Kind == ParametricSlotKind.Rational && Denominator != 0 && Numerator % Denominator == 0);

    /// <summary>Returns the integer value or null if this slot is not integral.</summary>
    public long? ToIntegerOrNull() =>
        IsIntegral()
            ? (Kind == ParametricSlotKind.Integer ? Numerator : Numerator / Denominator)
            : null;
}
