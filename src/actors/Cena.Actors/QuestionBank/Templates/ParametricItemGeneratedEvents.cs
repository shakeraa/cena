// =============================================================================
// Cena Platform — Parametric Item Generated Events (prr-200)
//
// Persistable envelope emitted by the compile pipeline when a variant is
// accepted. Per DoD we store ONLY the deterministic inputs — the rendered
// text can be regenerated at will from (templateId, version, seed) + a
// snapshot of the slot draws. The slot snapshot is redundant given the
// compiler's seed derivation, but we keep it for forensic readability and
// to survive the case where a template is re-authored (Version bumps) while
// old variants are still referenced by sessions.
//
// These events are intended to be appended to a new Marten stream per
// template id (future: prr-201 / prr-202 own the authoring aggregate).
// prr-200 introduces the event shape only; the persistence path is out of
// scope.
// =============================================================================

namespace Cena.Actors.QuestionBank.Templates;

/// <summary>
/// Emitted when a parametric variant is accepted by the compiler and
/// canonicalised by the CAS gate. The stored slot snapshot is a value-typed
/// list of <see cref="ParametricSlotValue"/> — no Unicode, no reflection,
/// no private fields — safe for JSON serialisation by Marten.
/// </summary>
public sealed record ParametricItemGenerated_V1(
    string TemplateId,
    int TemplateVersion,
    long Seed,
    IReadOnlyList<ParametricSlotValue> SlotSnapshot,
    string CanonicalAnswer,
    string CanonicalHash,
    DateTimeOffset GeneratedAt);

/// <summary>
/// Emitted when the compiler rejects a slot combo. Kept at the event layer
/// so ops can aggregate drop-reason frequency across templates via the
/// replay projection (prr-201 owns the projection; we just shape the event).
/// </summary>
public sealed record ParametricItemDropped_V1(
    string TemplateId,
    int TemplateVersion,
    long Seed,
    ParametricDropKind Reason,
    string Detail,
    double LatencyMs,
    DateTimeOffset DroppedAt);
