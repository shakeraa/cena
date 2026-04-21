// =============================================================================
// Cena Platform — DiagnosticBlockItem (prr-228)
//
// A single item served inside a per-target diagnostic block. The block is the
// per-ExamTarget replacement for the legacy single-pool diagnostic: 6-8 items
// per target, easy-first adaptive stop, skip always visible, provenance
// stamped per ADR-0043.
//
// Shape notes:
//   - Every item carries a <see cref="ProvenanceKind"/> — items whose
//     provenance is <see cref="ProvenanceKind.MinistryBagrut"/> MUST NEVER
//     be constructed as a block item (the factory throws). Ministry text is
//     curator-internal per ADR-0043 / CLAUDE.md non-negotiable "Bagrut
//     reference-only".
//   - `SkillCode` is the catalog-global skill (prr-222) — the item drives
//     the skill-keyed mastery projection
//     `(StudentId, ExamTargetCode, SkillCode)`.
//   - `DifficultyIrt` is the IRT theta (b) used by the easy-first adaptive
//     stop in <see cref="DiagnosticBlockSelector"/>.
//   - `ItemId` is the QuestionDocument id. Stable across item re-draws.
// =============================================================================

using Cena.Actors.Content;
using Cena.Actors.Mastery;

namespace Cena.Actors.Diagnosis.PerTarget;

/// <summary>
/// An item served inside a per-target diagnostic block.
/// </summary>
/// <param name="ItemId">QuestionDocument id.</param>
/// <param name="SkillCode">Skill this item calibrates (prr-222).</param>
/// <param name="DifficultyIrt">IRT b-parameter (theta). Negative ⇒ easy.</param>
/// <param name="Band">"easy" | "medium" | "hard" for UX / adaptive stop.</param>
/// <param name="Provenance">Origin classification per ADR-0043. Never
/// <see cref="ProvenanceKind.MinistryBagrut"/> for student delivery.</param>
public sealed record DiagnosticBlockItem(
    string ItemId,
    SkillCode SkillCode,
    double DifficultyIrt,
    string Band,
    Provenance Provenance)
{
    /// <summary>
    /// Factory enforcing the ADR-0043 invariant: Ministry items are
    /// curator-internal reference material and MUST NOT flow to students.
    /// Matches the pattern in <see cref="Deliverable{T}.From"/>.
    /// </summary>
    public static DiagnosticBlockItem Create(
        string itemId,
        SkillCode skillCode,
        double difficultyIrt,
        string band,
        Provenance provenance)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            throw new ArgumentException("itemId must be non-empty.", nameof(itemId));
        }

        if (string.IsNullOrWhiteSpace(band))
        {
            throw new ArgumentException("band must be non-empty.", nameof(band));
        }

        if (provenance.Kind == ProvenanceKind.MinistryBagrut)
        {
            throw new InvalidOperationException(
                "MinistryBagrut-provenanced items are curator-internal per ADR-0043 "
                + "and may not be served to students in a diagnostic block. "
                + "Route the item through BagrutRecreationAggregate (ADR-0032) "
                + "and serve the AiRecreated recreation instead. Source="
                + provenance.Source);
        }

        return new DiagnosticBlockItem(itemId, skillCode, difficultyIrt, band, provenance);
    }
}
