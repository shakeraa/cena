// =============================================================================
// Cena Platform — StudentEntitlementProjection (EPIC-PRR-I PRR-310 + 1D-fix-2)
//
// Marten inline projection: subscription events → per-student entitlement
// documents. Runs inline with SaveChangesAsync so the read model is always
// consistent with the writes that preceded it within the same session.
//
// Marten convention (EventProjection base): method names must be
// Project / Create / Transform. Apply is used on aggregates, not on
// EventProjection-derived read-models.
//
// Registered in MartenConfiguration as Inline.
//
// HasPaymentMethodOnFile is intentionally NOT projected onto the document.
// The Phase 1D-fix-2 iteration 1 attempt to project it had a real-event-
// order defect: the Attached event arrives BEFORE Activated on the trial-
// then-pay sequence, so the projector would query for documents that
// don't exist yet and silently drop the flag. The resolver derives
// HasPaymentMethodOnFile at query-time from the live subscription
// aggregate, which is always correct regardless of event order. One
// extra Marten round-trip per projection-backed resolve is the cost; the
// alternative (a parent-keyed shadow document) adds invasive surface
// area for marginal benefit.
// =============================================================================

using Cena.Actors.Subscriptions.Events;
using Marten;
using Marten.Events.Projections;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Maintains one <see cref="StudentEntitlementDocument"/> per linked student.
/// Runs inline per Marten's <c>ProjectionLifecycle.Inline</c>.
///
/// Trial events are intentionally NOT projected here: trial views always
/// flow through the parent-bindings path of
/// <see cref="StudentEntitlementResolver"/> so the pinned
/// <see cref="TrialCapsSnapshot"/> is available on the hot path.
/// HasPaymentMethodOnFile is also derived at resolve-time, not projected.
/// </summary>
public sealed class StudentEntitlementProjection : EventProjection
{
    /// <summary>Activation: upsert entitlement doc for the primary student.</summary>
    public void Project(SubscriptionActivated_V1 e, IDocumentOperations ops)
    {
        ops.Store(new StudentEntitlementDocument
        {
            Id = e.PrimaryStudentSubjectIdEncrypted,
            EffectiveTier = e.Tier,
            SourceParentSubjectIdEncrypted = e.ParentSubjectIdEncrypted,
            ValidUntil = e.RenewsAt,
            LastUpdatedAt = e.ActivatedAt,
        });
    }

    /// <summary>Sibling linked: upsert entitlement doc for the sibling.</summary>
    public void Project(SiblingEntitlementLinked_V1 e, IDocumentOperations ops)
    {
        ops.Store(new StudentEntitlementDocument
        {
            Id = e.SiblingStudentSubjectIdEncrypted,
            EffectiveTier = e.Tier,
            SourceParentSubjectIdEncrypted = e.ParentSubjectIdEncrypted,
            ValidUntil = null,   // inherits parent renewal; dedicated fan-out follow-up
            LastUpdatedAt = e.LinkedAt,
        });
    }

    /// <summary>Sibling unlinked: remove entitlement doc.</summary>
    public void Project(SiblingEntitlementUnlinked_V1 e, IDocumentOperations ops)
    {
        ops.Delete<StudentEntitlementDocument>(e.SiblingStudentSubjectIdEncrypted);
    }
}
