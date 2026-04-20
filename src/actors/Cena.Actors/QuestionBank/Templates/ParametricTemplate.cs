// =============================================================================
// Cena Platform — Parametric Template Record (prr-200, ADR-0002, ADR-0032,
// ADR-0040 methodology, ADR-0043 Bagrut reference-only)
//
// An immutable authoring artifact that declares:
//   * The stem pattern with {slotName} substitutions
//   * Named slots (integer / rational / choice)
//   * The SymPy solution expression in terms of slot names
//   * Accept-shapes — which answer shapes are pedagogically acceptable
//   * Methodology / track / difficulty tags (coverage axes)
//   * Optional distractor misconception mappings (MCQ templates)
//
// Non-negotiables:
//   * No LLM is consulted at any point in the template's lifecycle.
//   * solution_expr is a SymPy expression in slot names; the renderer
//     substitutes values and sends to the CAS router for canonicalisation.
//   * If the template was derived from a Bagrut reference item, the source
//     attribution is carried in BagrutSource (ADR-0043). It never leaks onto
//     a student-facing DTO — enforced by BagrutRecreationOnlyTest.
// =============================================================================

namespace Cena.Actors.QuestionBank.Templates;

/// <summary>
/// Which answer shapes the CAS gate should accept for this template.
/// A slot combo that solves to <c>7/3</c> is dropped when
/// <see cref="AcceptShape.Integer"/> is the only accepted shape.
/// </summary>
[Flags]
public enum AcceptShape
{
    None       = 0,
    Integer    = 1 << 0,
    Rational   = 1 << 1,
    Decimal    = 1 << 2, // terminating decimal only
    Symbolic   = 1 << 3, // e.g. sqrt(2)
    Any        = Integer | Rational | Decimal | Symbolic
}

/// <summary>
/// Pedagogical methodology per ADR-0040. A template authored for one
/// methodology does NOT count as coverage for the other.
/// </summary>
public enum TemplateMethodology
{
    Halabi,
    Rabinovitch
}

/// <summary>
/// Bagrut track (4-unit vs 5-unit). Independent coverage axes — a 4-unit
/// template does not satisfy a 5-unit rung.
/// </summary>
public enum TemplateTrack
{
    FourUnit,
    FiveUnit
}

/// <summary>
/// Difficulty rung per Strategy 3 in the engine doc §4.1.
/// Persisted as a 3-bucket label; the DifficultyElo float on the QuestionDocument
/// is seeded from this bucket at publish time via SeedDifficultyEloFromBucket.
/// </summary>
public enum TemplateDifficulty
{
    Easy,
    Medium,
    Hard
}

/// <summary>
/// Optional Bagrut source attribution when a template was authored *from* a
/// Ministry item. Reference-only per ADR-0043 — carried on the template
/// record, never surfaced on a student-facing DTO. <see cref="ReferenceOnly"/>
/// is an explicit declaration, not a secret flag.
/// </summary>
public sealed record BagrutSourceAttribution(
    string MinistryExamYear,
    string MinistryExamMoed,
    string MinistryQuestionNumber,
    bool   ReferenceOnly);

/// <summary>
/// MCQ distractor derived from a named misconception class in
/// <see cref="Cena.Actors.Services.MisconceptionCatalog"/>. The formula is a
/// SymPy-substitutable expression in slot names — see DoD bullet
/// "Distractor generation" in TASK-PRR-200.
/// </summary>
public sealed record DistractorRule(
    string MisconceptionId,
    string FormulaExpr,
    string? LabelHint);

/// <summary>
/// Optional cross-slot predicate. Evaluated AFTER all slots are drawn; rejects
/// the combo without CAS cost if it trivially would not pedagogically work
/// (e.g. "a == 0" on a coefficient slot is a degenerate case we want to skip
/// even if the IntegerExclude didn't catch it).
/// </summary>
public sealed record SlotConstraint(string Description, string PredicateExpr);

/// <summary>
/// Parametric template — the unit of authoring for Strategy 1.
///
/// Templates are event-sourced at the admin authoring layer (prr-202 owns
/// CRUD; this task introduces the record only). Persistence is out of scope
/// for prr-200; the CLI harness and unit tests pass templates by value.
/// </summary>
public sealed record ParametricTemplate
{
    public required string Id { get; init; }
    public required int Version { get; init; } = 1;

    // ── Classification ──
    public required string Subject { get; init; }
    public required string Topic { get; init; }
    public required TemplateTrack Track { get; init; }
    public required TemplateDifficulty Difficulty { get; init; }
    public required TemplateMethodology Methodology { get; init; }
    public int BloomsLevel { get; init; } = 3;
    public string Language { get; init; } = "en";

    // ── Content ──
    public required string StemTemplate { get; init; }
    public required string SolutionExpr { get; init; }
    public string? VariableName { get; init; }
    public AcceptShape AcceptShapes { get; init; } = AcceptShape.Any;

    // ── Slots & constraints ──
    public required IReadOnlyList<ParametricSlot> Slots { get; init; }
    public IReadOnlyList<SlotConstraint> Constraints { get; init; } = Array.Empty<SlotConstraint>();

    // ── MCQ distractors (optional) ──
    public IReadOnlyList<DistractorRule> DistractorRules { get; init; } = Array.Empty<DistractorRule>();

    // ── Attribution (ADR-0043) ──
    public BagrutSourceAttribution? BagrutSource { get; init; }

    /// <summary>
    /// Structural validity. Called by the compiler before any slot is drawn.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id)) throw new ArgumentException("Template Id required");
        if (Version < 1) throw new ArgumentException("Template Version must be >= 1");
        if (string.IsNullOrWhiteSpace(StemTemplate)) throw new ArgumentException("StemTemplate required");
        if (string.IsNullOrWhiteSpace(SolutionExpr)) throw new ArgumentException("SolutionExpr required");
        if (Slots.Count == 0) throw new ArgumentException("Template must declare at least one slot");
        if (AcceptShapes == AcceptShape.None) throw new ArgumentException("AcceptShapes must not be None");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in Slots)
        {
            s.Validate();
            if (!seen.Add(s.Name))
                throw new ArgumentException($"Duplicate slot name '{s.Name}' in template '{Id}'");
        }

        // Every referenced {slot} in the stem must be declared.
        foreach (var refName in ExtractReferencedSlotNames(StemTemplate))
        {
            if (!seen.Contains(refName))
                throw new ArgumentException(
                    $"StemTemplate references undeclared slot '{{{refName}}}' in template '{Id}'");
        }
    }

    /// <summary>
    /// Extract {slotName} tokens from a stem template. Matches
    /// <c>[A-Za-z_][A-Za-z0-9_]*</c> inside braces. Escaped braces
    /// (<c>{{</c> / <c>}}</c>) are ignored.
    /// </summary>
    public static IEnumerable<string> ExtractReferencedSlotNames(string template)
    {
        if (string.IsNullOrEmpty(template)) yield break;
        for (var i = 0; i < template.Length; i++)
        {
            if (template[i] != '{') continue;
            if (i + 1 < template.Length && template[i + 1] == '{') { i++; continue; }

            var end = template.IndexOf('}', i + 1);
            if (end < 0) yield break;
            var name = template.Substring(i + 1, end - i - 1);
            if (IsValidSlotName(name)) yield return name;
            i = end;
        }
    }

    private static bool IsValidSlotName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!(char.IsLetter(name[0]) || name[0] == '_')) return false;
        for (var i = 1; i < name.Length; i++)
            if (!(char.IsLetterOrDigit(name[i]) || name[i] == '_')) return false;
        return true;
    }
}
