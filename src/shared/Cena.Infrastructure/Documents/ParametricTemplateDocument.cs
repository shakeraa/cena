// =============================================================================
// Cena Platform — ParametricTemplateDocument (prr-202)
//
// Marten-stored parametric template for the admin authoring surface. The
// document is a denormalised projection of the latest template state — the
// authoritative history lives in the ParametricTemplateAuthoringEvents_* event
// stream keyed on StreamKey=Id. prr-202 reads both:
//   * `LoadAsync<ParametricTemplateDocument>(id)` for list/detail/preview.
//   * `Events.AppendAsync(streamKey, events)` for every mutation, so a replay
//     projection (owned by prr-201) can rebuild the document from scratch.
//
// Soft-delete is implemented via `Active=false` (ADR convention — hard-deleting
// event-sourced entities breaks audit). Tenant scope: templates are global
// *content* (no school_id filter at query time) but every mutation is stamped
// with the caller's school_id in `LastMutatedBySchool` so the audit trail
// survives even if an admin moves schools. This matches the pattern used for
// global question documents (QuestionDocument.cs) and satisfies ADR-0001
// "tenant-aware writes" without forcing tenant-segregated reads.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Denormalised projection of a parametric template (the latest state after
/// applying every ParametricTemplateAuthoringEvents_* event in the stream).
/// Authoring CRUD lives in <c>Cena.Admin.Api.Templates.ParametricTemplateAuthoringService</c>.
/// </summary>
public sealed class ParametricTemplateDocument
{
    /// <summary>Stream key — stable slug-friendly identifier (letters, digits, ._-).</summary>
    public string Id { get; set; } = "";

    /// <summary>Monotonic version counter — bumps on every successful update.</summary>
    public int Version { get; set; } = 1;

    // ── Classification ──

    public string Subject { get; set; } = "";
    public string Topic { get; set; } = "";
    /// <summary>"FourUnit" | "FiveUnit" — matches TemplateTrack enum names.</summary>
    public string Track { get; set; } = "";
    /// <summary>"Easy" | "Medium" | "Hard" — matches TemplateDifficulty enum names.</summary>
    public string Difficulty { get; set; } = "";
    /// <summary>"Halabi" | "Rabinovitch" — matches TemplateMethodology enum names.</summary>
    public string Methodology { get; set; } = "";
    public int BloomsLevel { get; set; } = 3;
    public string Language { get; set; } = "en";

    // ── Content ──

    public string StemTemplate { get; set; } = "";
    public string SolutionExpr { get; set; } = "";
    public string? VariableName { get; set; }

    /// <summary>
    /// "integer" / "rational" / "decimal" / "symbolic" / "any" — one or more.
    /// Persisted as list of canonical shape strings, mapped to AcceptShape flags
    /// at compile time via <c>TemplateGenerateEndpoint.ParseAcceptShapes</c>.
    /// </summary>
    public List<string> AcceptShapes { get; set; } = new();

    public List<ParametricSlotPayload> Slots { get; set; } = new();
    public List<SlotConstraintPayload> Constraints { get; set; } = new();
    public List<DistractorRulePayload> DistractorRules { get; set; } = new();

    // ── Lifecycle ──

    /// <summary>
    /// False = soft-deleted. Queries must filter on <c>Active=true</c>. Hard
    /// delete is never allowed from the API surface — preserves audit.
    /// </summary>
    public bool Active { get; set; } = true;

    /// <summary>
    /// "draft" = editable but never served to students.
    /// "published" = CAS-verified by preview at least once and promoted.
    /// prr-202 introduces the states but does not gate on publish; the consumer
    /// (prr-201 waterfall) reads only <c>Active AND Status='published'</c>.
    /// </summary>
    public string Status { get; set; } = "draft";

    public DateTimeOffset CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public string CreatedBySchool { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }
    public string LastMutatedBy { get; set; } = "";
    public string LastMutatedBySchool { get; set; } = "";

    /// <summary>
    /// SHA-256 hex of the JSON-serialised pre-mutation state. Persisted on the
    /// UPDATE event so replay can detect stream interleaving / lost-update race
    /// without pulling the previous document. Hex to keep the event payload
    /// human-readable in Marten's JSONB storage.
    /// </summary>
    public string StateHash { get; set; } = "";
}

/// <summary>JSON-friendly mirror of <c>ParametricSlot</c> for persistence.</summary>
public sealed class ParametricSlotPayload
{
    public string Name { get; set; } = "";
    /// <summary>"integer" | "rational" | "choice"</summary>
    public string Kind { get; set; } = "";
    public int IntegerMin { get; set; }
    public int IntegerMax { get; set; }
    public List<int> IntegerExclude { get; set; } = new();
    public int NumeratorMin { get; set; }
    public int NumeratorMax { get; set; }
    public int DenominatorMin { get; set; } = 1;
    public int DenominatorMax { get; set; } = 1;
    public bool ReduceRational { get; set; } = true;
    public List<string> Choices { get; set; } = new();
}

public sealed class SlotConstraintPayload
{
    public string Description { get; set; } = "";
    public string PredicateExpr { get; set; } = "";
}

public sealed class DistractorRulePayload
{
    public string MisconceptionId { get; set; } = "";
    public string FormulaExpr { get; set; } = "";
    public string? LabelHint { get; set; }
}
