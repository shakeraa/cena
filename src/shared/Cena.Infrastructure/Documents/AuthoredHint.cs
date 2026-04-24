// =============================================================================
// Cena Platform — AuthoredHint + HintVariant (EPIC-PRR-F PRR-262)
//
// Lives in Cena.Infrastructure.Documents (not Cena.Actors) because the
// type is a persistence-layer concern — it's a field on the Marten-
// serialised QuestionDocument. Keeping it next to QuestionDocument.cs
// avoids a Cena.Actors → Cena.Infrastructure cyclic dependency while
// still letting the actors-layer router + leak detector consume the
// types.
//
// Semantics + banner docs: see
// `src/actors/Cena.Actors/Mastery/StemGroundedHints.cs` — the router
// and leak-detector file carries the pedagogy rationale (persona-
// educator + persona-cogsci blocker; no-leak invariant in hidden mode;
// ADR-0050 prerequisite-flag interaction).
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Per-hint variant flag. Drives whether a hint is safe to serve when
/// the student has not yet revealed the MC options (PRR-260 hidden
/// mode / PRR-261 classroom-enforced redaction).
/// </summary>
public enum HintVariant
{
    /// <summary>
    /// Hint references ONLY the question stem / prerequisite concepts.
    /// Does not reference option letters or option content. Safe to serve
    /// in hidden_reveal mode.
    /// </summary>
    StemGrounded = 0,

    /// <summary>
    /// Hint may reference option letters or option content. Only served
    /// when options are already visible (attemptMode == visible OR the
    /// student has clicked "reveal").
    /// </summary>
    Full = 1,
}

/// <summary>
/// One authored hint on a question. Ships with scaffolding-level binding
/// (L1 / L2 / L3 per ADR-0045 hint-ladder convention) + variant metadata
/// so the router can pick the right one for the student's mode.
/// Serialised as part of <see cref="QuestionDocument.AuthoredHints"/>.
/// </summary>
/// <param name="Level">Ladder rung: 1 = template, 2 = method, 3 = worked example.</param>
/// <param name="Variant">StemGrounded = hidden-mode safe; Full = requires revealed options.</param>
/// <param name="Text">Localised hint body. Authors write per-locale copies as separate rows; the router treats one row as one (level × variant × locale) tuple.</param>
/// <param name="Locale">BCP-47-ish locale tag ("he" / "ar" / "en").</param>
public sealed record AuthoredHint(
    int Level,
    HintVariant Variant,
    string Text,
    string Locale);
