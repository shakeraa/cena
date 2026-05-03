// =============================================================================
// Cena Platform — AiSettings audit events.
//
// Every mutation to AiSettingsDocument.ModelOverridesByTask appends an
// AiSettingsChangedEvent to a Marten event stream so the GET surface can
// answer "who changed quality_gate from Haiku to Sonnet on 2026-05-03 and
// why?". The stream is keyed by AiSettingsDocument.SingletonId — there's
// only one settings doc per environment, so a single stream per-host is
// the right shape.
//
// The event-sourced audit complements (does NOT replace) the existing
// AuditEventDocument table — that table is the cross-domain SIEM feed,
// while this stream is the domain's own narrow, queryable "what model
// did we use last week?" history. Both write on the same code path so
// neither can drift.
// =============================================================================

namespace Cena.Admin.Api.AiSettings;

/// <summary>
/// Append-only event emitted whenever the per-task model override map on
/// AiSettingsDocument changes. <see cref="OldModelId"/> is null when the
/// task did not have an override before this change (i.e. it was using the
/// routing-config default); <see cref="NewModelId"/> is null when the
/// curator cleared the override (PUT body <c>{"modelId": null}</c>) so the
/// task falls back to the routing-config default.
/// </summary>
/// <param name="TaskName">
/// Canonical task name, e.g. <c>concept_extraction</c>. Closed-set against
/// <see cref="RoutingConfigTaskDefaults.KnownTaskNames"/> at write time.
/// </param>
/// <param name="OldModelId">Override before this change; null = "no override / using routing-config default".</param>
/// <param name="NewModelId">Override after this change; null = "cleared / using routing-config default".</param>
/// <param name="ChangedBy">User id of the curator who issued the change.</param>
/// <param name="ChangedAt">UTC timestamp of the change.</param>
public sealed record AiSettingsChangedEvent(
    string TaskName,
    string? OldModelId,
    string? NewModelId,
    string ChangedBy,
    DateTimeOffset ChangedAt);
