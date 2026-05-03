// =============================================================================
// Cena Platform — ParentChildBoundV1 (prr-009 / TASK-E2E-A-04-BE)
//
// Emitted when a parent successfully consumes a single-use bind invite token
// (POST /api/parent/bind/{token}). Distinct from the admin-driven binding
// path: the admin path goes through ParentChildBindingService.GrantAsync
// directly; this event is specifically for the parent-driven invite + accept
// edge that ADR-0041 § "parent provisioning" calls out.
//
// Stream key: ParentChildBindingService is not event-sourced (it's a
// document store); this event is published on NATS only. Consumers:
//
//   * E2E flow tests (`tests/e2e-flow/fixtures/bus-probe.ts`) for the A-04
//     bus boundary assertion
//   * Parent-notifications service for "you're now linked to {childName}"
//     emails (out of scope for this task; tracked separately)
//   * Admin analytics for bind-invite conversion metrics
//
// NATS subject: cena.events.parent.{parentUid}.bound — see
// NatsSubjects.ParentEvent + NatsSubjects.ParentChildBound.
// =============================================================================

namespace Cena.Actors.Parent.Events;

/// <summary>
/// Emitted when a parent → child binding is granted via the invite flow.
/// </summary>
/// <param name="ParentUid">Firebase uid of the parent who consumed the invite.</param>
/// <param name="StudentSubjectId">Bound student's anon subject id (same as the
/// student's Firebase uid in the current schema).</param>
/// <param name="TenantId">ADR-0001 institute id; matches the invite's tenant
/// claim — cross-tenant invites are rejected at the endpoint.</param>
/// <param name="Relationship">"parent" | "guardian" | "stepparent" | etc.
/// Free-form per the task body; receiver localizes for display.</param>
/// <param name="BoundAt">Server clock at consume time.</param>
/// <param name="CorrelationId">For trace stitching across HTTP → Marten →
/// NATS → downstream consumers.</param>
/// <param name="InviteJti">jti of the consumed invite. Lets downstream
/// consumers dedup by invite (e.g. notification service that runs at-most-
/// once per binding).</param>
public sealed record ParentChildBoundV1(
    string ParentUid,
    string StudentSubjectId,
    string TenantId,
    string Relationship,
    DateTimeOffset BoundAt,
    Guid CorrelationId,
    string InviteJti);
