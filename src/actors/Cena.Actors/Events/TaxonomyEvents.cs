// =============================================================================
// Cena Platform — Taxonomy Events (RDY-019a / Phase 3)
//
// Event-sourced record of a question being mapped to a node in
// scripts/bagrut-taxonomy.json. Lives next to the other V1 events so
// Marten schema registration (MartenConfiguration.cs) picks it up in
// the same scan.
//
// Producers:
//   * TaxonomyMigrator tool (batch remap of existing questions)
//   * QuestionBankService.CreateQuestionAsync (new ingestions, after
//     CAS-GATE-SEED-REFACTOR t_d995fe1da366 lands)
//
// Consumers:
//   * QuestionReadModel projection — TaxonomyNode + Track fields
//   * Coverage report builder (RDY-019c)
// =============================================================================

namespace Cena.Actors.Events;

/// <summary>
/// Emitted when a question is assigned to a taxonomy node. The node id is
/// a dotted path rooted at a track — "math_5u.calculus.derivatives" — and
/// must match a leaf defined in scripts/bagrut-taxonomy.json at the time
/// of the event. Historical events keep their original node string even
/// if the taxonomy JSON is later reorganised; consumers should handle
/// un-resolvable nodes gracefully.
/// </summary>
public sealed record QuestionTaxonomyMapped_V1(
    string QuestionId,
    string Track,                  // "math_3u" | "math_4u" | "math_5u"
    string TaxonomyNode,           // dotted path, e.g. "calculus.derivatives.rules"
    string? ConceptId,             // optional bridge to the existing concept graph (e.g. "CAL-003")
    double MappingConfidence,      // 0..1 — migrator heuristic or curator confirmation
    string MappingStrategy,        // "curator" | "concept_id" | "heuristic"
    string MappedBy,               // user id or tool name ("taxonomy-migrator")
    DateTimeOffset Timestamp);
