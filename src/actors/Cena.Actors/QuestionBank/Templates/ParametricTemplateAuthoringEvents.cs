// =============================================================================
// Cena Platform — Parametric Template Authoring Events (prr-202)
//
// Event-sourced audit trail for the admin authoring surface. Every CRUD
// mutation on a ParametricTemplateDocument appends ONE of these events to the
// per-template Marten stream (streamKey = templateId). The projection that
// rebuilds the document is owned by prr-201 (wet-run ingestion path); prr-202
// only emits the events.
//
// Schema evolution contract:
//   * Suffix every record name with `_V<n>`. Bump the suffix when the event
//     payload shape changes; write a Marten upcaster from V(n-1) → V(n) under
//     Cena.Infrastructure.EventStore.EventUpcasters.cs.
//   * Never remove a field from an old version. Projection replay must be able
//     to rebuild years-old documents from the original event payloads.
//
// The events carry ONLY the delta semantics — the resulting projected state
// lives on ParametricTemplateDocument. Per CLAUDE.md DDD guidance, these
// records are immutable value objects with no behaviour.
// =============================================================================

using Cena.Infrastructure.Documents;

namespace Cena.Actors.QuestionBank.Templates;

/// <summary>
/// Emitted when a new parametric template is authored. Carries the full
/// initial payload — the projection builds a fresh document from this alone.
/// <c>PriorStateHash</c> is empty (no prior state).
/// </summary>
public sealed record ParametricTemplateCreated_V1(
    string TemplateId,
    int Version,
    ParametricTemplateDocument Snapshot,
    string ActorUserId,
    string ActorSchoolId,
    DateTimeOffset OccurredAt);

/// <summary>
/// Emitted on any non-delete mutation. Carries the post-mutation snapshot AND
/// the prior state hash — so a projection replayer can detect split-brain
/// interleaving (same stream written by two workers, stream key not enough).
/// The <c>ChangedFields</c> list is advisory — the full snapshot is authoritative.
/// </summary>
public sealed record ParametricTemplateUpdated_V1(
    string TemplateId,
    int Version,
    ParametricTemplateDocument Snapshot,
    string PriorStateHash,
    IReadOnlyList<string> ChangedFields,
    string ActorUserId,
    string ActorSchoolId,
    DateTimeOffset OccurredAt);

/// <summary>
/// Emitted when an admin soft-deletes a template. <c>Active</c> flips to false
/// on the projection but the document row is not physically removed — audit
/// queries must always be reconstructible.
/// </summary>
public sealed record ParametricTemplateDeleted_V1(
    string TemplateId,
    int PriorVersion,
    string PriorStateHash,
    string ActorUserId,
    string ActorSchoolId,
    string? Reason,
    DateTimeOffset OccurredAt);

/// <summary>
/// Emitted when an admin triggers a live preview. Persisted for two reasons:
///   1. Publish-gate audit: "someone verified at least one preview variant
///      was CAS-accepted before they promoted the template to published".
///   2. CAS oracle latency sampling — the preview path is the most expensive
///      sync path in the admin UI and ops wants p95 on it.
/// The sample outcomes are stored in a compressed form (counts + first-failure
/// detail) to keep events small; the full rendered variants are transient.
/// </summary>
public sealed record ParametricTemplatePreviewExecuted_V1(
    string TemplateId,
    int TemplateVersion,
    int SampleCount,
    int AcceptedCount,
    int FirstFailureKind, // ParametricDropKind ordinal or -1 if all accepted
    string? FirstFailureDetail,
    double TotalLatencyMs,
    string ActorUserId,
    string ActorSchoolId,
    DateTimeOffset OccurredAt);
