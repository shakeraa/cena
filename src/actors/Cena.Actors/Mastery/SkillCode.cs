// =============================================================================
// Cena Platform — SkillCode VO (prr-222)
//
// Catalog-global identifier for a pedagogical skill / concept.
// Lowercase, dot-separated hierarchy, e.g.:
//   "math.algebra.quadratic-equations"
//   "math.geometry.pythagoras"
//   "verbal.sentence-completion"
//
// SkillCode is independent of ExamTargetCode. Mastery rows are keyed on
// the TUPLE (StudentId, ExamTargetCode, SkillCode) per prr-222 so that a
// student preparing for Bagrut-Math-4yu and Bagrut-Math-5yu has SEPARATE
// mastery posteriors for the same skill at two different exam depths.
// Cross-exam skill sharing (Bagrut-Math ↔ PET-Quant) is handled by
// ExamTargetCode catalog aliasing at the scheduler layer, not by
// collapsing the mastery key.
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Catalog-global skill / concept identifier. Normalised to lowercase,
/// dot-hierarchy, e.g. "math.algebra.quadratic-equations".
/// </summary>
public readonly record struct SkillCode
{
    /// <summary>Canonical raw value.</summary>
    public string Value { get; }

    private SkillCode(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Parse a raw string into a <see cref="SkillCode"/>. Normalises
    /// case and trims whitespace; rejects empty input, leading/trailing
    /// dots, consecutive dots, and any character outside <c>[a-z0-9.-]</c>.
    /// </summary>
    public static SkillCode Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ArgumentException(
                "SkillCode must be non-empty.",
                nameof(raw));
        }

        var normalised = raw.Trim().ToLowerInvariant();
        if (normalised.Length == 0)
        {
            throw new ArgumentException(
                "SkillCode must be non-empty after trim.",
                nameof(raw));
        }

        if (normalised[0] == '.' || normalised[^1] == '.')
        {
            throw new ArgumentException(
                $"SkillCode '{raw}' may not start or end with '.'.",
                nameof(raw));
        }

        var sawDot = false;
        foreach (var ch in normalised)
        {
            if (ch == '.')
            {
                if (sawDot)
                {
                    throw new ArgumentException(
                        $"SkillCode '{raw}' contains consecutive dots.",
                        nameof(raw));
                }
                sawDot = true;
                continue;
            }
            sawDot = false;

            var ok = (ch >= 'a' && ch <= 'z')
                     || (ch >= '0' && ch <= '9')
                     || ch == '-';
            if (!ok)
            {
                throw new ArgumentException(
                    $"SkillCode '{raw}' contains invalid character '{ch}'. "
                    + "Allowed: a-z, 0-9, '-', '.'.",
                    nameof(raw));
            }
        }

        return new SkillCode(normalised);
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
