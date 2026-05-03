// =============================================================================
// Cena Platform — Concept Extraction Events (ADR-0062 Phase 0)
//
// Event-sourced record of a question's full concept set: primary +
// supporting. Distinct from QuestionTaxonomyMapped_V1 (which carries a
// single taxonomy-node mapping for the kanban / coverage flow); these
// events drive the actual mastery semantics:
//
//   * `Primary` is the SkillCode BKT keys ConceptAttempted_V3 off.
//   * `Supporting` is the set of additional SkillCodes the question
//     also exercises. Phase 2 turns these into MasterySignalEmitted_V1
//     positive-only nudges, but only for leaves with ≥10 published
//     items (the precondition gate from the 002 multi-persona research).
//
// Two events:
//
//   `QuestionConceptsExtracted_V1` — the extractor's output (rules
//      and/or LLM-tier per ADR-0062 §1). Always emitted, even when the
//      curator later overrides; the override is its own event so the
//      audit trail shows what the extractor said vs. what shipped.
//
//   `QuestionConceptsConfirmed_V1` — emitted on curator confirm /
//      override. Carries the final concept set that
//      QuestionListProjection writes onto QuestionReadModel.Concepts
//      (and QuestionState.ConceptIds rebuilds on replay). For the first
//      200 items (calibration corpus) this event is REQUIRED before
//      publish — see QuestionBankService.PublishAsync + the
//      IConceptCurationCalibrationCounter gate. After 200 the
//      extractor's set auto-confirms with the curator UI surfacing
//      one-click override.
//
// Both events are stream-keyed on QuestionId — the same stream the
// existing question lifecycle events use — so AggregateStreamAsync
// rebuilds a coherent QuestionState including concepts.
//
// Stable id: SkillCode (canonical "math.calculus.derivative-rules"
// form). Unmappable LLM output is rejected by BagrutTaxonomyCatalog
// upstream; events never carry free-form skill strings.
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Events;

/// <summary>
/// Concept role on a question. <see cref="Primary"/> drives BKT updates;
/// <see cref="Supporting"/> drives the Phase 2 MasterySignalEmitted_V1
/// nudge channel (gated on ≥10 items per leaf).
/// </summary>
public enum ConceptRole
{
    Primary    = 1,
    Supporting = 2,
}

/// <summary>
/// One concept attached to a question with its role + extraction
/// metadata. Rationale is the LLM's "show your work" string; curators
/// read it when deciding whether to confirm or override.
/// </summary>
public sealed record QuestionConcept(
    SkillCode SkillCode,
    ConceptRole Role,
    double Confidence,        // 0..1 — extractor's self-reported confidence
    string Rationale,         // LLM's explanation; empty for rules-tier hits
    string Tier);             // "rules" | "llm" | "hybrid" | "curator"

/// <summary>
/// Emitted by the extractor (rules + optional LLM tier) when a question
/// gets its concept profile. Always carries the full set, primary first;
/// callers must not mutate the list in place.
/// </summary>
public sealed record QuestionConceptsExtracted_V1(
    string QuestionId,
    IReadOnlyList<QuestionConcept> Concepts,
    string ExtractionStrategy,    // e.g. "rules_v1+llm_haiku4_5_v1" — versioned for audit
    string ExtractedBy,           // tool name / extractor id, NOT a user
    DateTimeOffset Timestamp);

/// <summary>
/// Emitted on curator confirmation. Carries the FINAL concept set that
/// the projection will mirror onto QuestionDocument. May equal the
/// extractor's set verbatim (one-click confirm) or differ (curator
/// override / edit). The CuratorAction field captures which path so
/// the calibration corpus can measure extractor precision against
/// curator decisions per ADR-0062 §Phasing.
/// </summary>
public sealed record QuestionConceptsConfirmed_V1(
    string QuestionId,
    IReadOnlyList<QuestionConcept> Concepts,
    CuratorAction Action,
    string ConfirmedBy,           // user id (curator)
    DateTimeOffset Timestamp);

/// <summary>
/// What the curator did at confirm time. Drives precision telemetry
/// for the calibration corpus + audit trail.
/// </summary>
public enum CuratorAction
{
    /// <summary>One-click confirm of extractor output unchanged.</summary>
    AcceptedAsExtracted = 1,
    /// <summary>Curator changed the primary concept.</summary>
    PrimaryEdited       = 2,
    /// <summary>Curator added or removed supporting concepts.</summary>
    SupportingEdited    = 3,
    /// <summary>Curator rejected the extractor entirely and re-tagged.</summary>
    FullyOverridden     = 4,
}
