// =============================================================================
// Cena Platform — ExamTargetCode VO (prr-222/223/229, ADR-0050)
//
// Stable identifier for an ExamTarget within a student's plan. Combines a
// catalog-level exam identity (e.g. "bagrut-math-5yu", "sat-math", "pet-quant")
// with a per-student instance discriminator so two active targets for the
// same exam+track+sitting on the same student collide on the (StudentId,
// ExamTargetCode, SkillCode) invariant enforced by SkillKeyedMasteryProjection.
//
// This file ships ahead of the full prr-218 StudentPlan aggregate so that
// prr-222 (skill-keyed mastery), prr-223 (RTBF cascade), and prr-229 (24m
// retention) can land without blocking on the larger aggregate overhaul.
// When prr-218 ships, its richer ExamTarget record adopts this VO
// unchanged — the string form here IS the canonical catalog code that
// ADR-0050 §2 calls "ministry שאלון code" for Bagrut, "collegeboard code"
// for SAT, "nite code" for PET.
// =============================================================================

namespace Cena.Actors.ExamTargets;

/// <summary>
/// Stable, catalog-level identifier for an exam target. Normalised to
/// lowercase, hyphen-separated ASCII. Examples:
///   "bagrut-math-5yu"   (Bagrut Math 5-unit)
///   "bagrut-math-4yu"   (Bagrut Math 4-unit — distinct from 5yu per ADR-0050 #2)
///   "sat-math"          (SAT Math)
///   "pet-quant"         (PET Quantitative — regulator:nite per ADR-0050 #7)
/// </summary>
public readonly record struct ExamTargetCode
{
    /// <summary>Historical default assumed by the V1 → V2 upcaster (prr-222).</summary>
    public const string V1UpcastDefault = "bagrut-math-5yu";

    /// <summary>Canonical raw value. Normalised (see <see cref="Parse"/>).</summary>
    public string Value { get; }

    private ExamTargetCode(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Parse a raw string into an <see cref="ExamTargetCode"/>. Normalises
    /// case and trims whitespace; rejects empty input and any character
    /// outside <c>[a-z0-9-]</c> after normalisation.
    /// </summary>
    public static ExamTargetCode Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new ArgumentException(
                "ExamTargetCode must be non-empty.",
                nameof(raw));
        }

        var normalised = raw.Trim().ToLowerInvariant();
        if (normalised.Length == 0)
        {
            throw new ArgumentException(
                "ExamTargetCode must be non-empty after trim.",
                nameof(raw));
        }

        foreach (var ch in normalised)
        {
            var ok = (ch >= 'a' && ch <= 'z')
                     || (ch >= '0' && ch <= '9')
                     || ch == '-';
            if (!ok)
            {
                throw new ArgumentException(
                    $"ExamTargetCode '{raw}' contains invalid character '{ch}'. "
                    + "Allowed: a-z, 0-9, '-'.",
                    nameof(raw));
            }
        }

        return new ExamTargetCode(normalised);
    }

    /// <summary>
    /// Try-parse variant. Returns <c>false</c> instead of throwing. Useful
    /// on upcaster paths where the historical default is the fallback.
    /// </summary>
    public static bool TryParse(string? raw, out ExamTargetCode code)
    {
        try
        {
            code = Parse(raw ?? "");
            return true;
        }
        catch (ArgumentException)
        {
            code = default;
            return false;
        }
    }

    /// <summary>
    /// The canonical V1-upcast default (<see cref="V1UpcastDefault"/>).
    /// </summary>
    public static ExamTargetCode Default => Parse(V1UpcastDefault);

    /// <inheritdoc />
    public override string ToString() => Value;
}
