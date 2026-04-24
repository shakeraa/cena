// =============================================================================
// Cena Platform — StudentEntitlementProjection (EPIC-PRR-I PRR-310)
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
// =============================================================================

using Cena.Actors.Subscriptions.Events;
using Marten;
using Marten.Events.Projections;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Maintains one <see cref="StudentEntitlementDocument"/> per linked student.
/// Runs inline per Marten's <c>ProjectionLifecycle.Inline</c>.
///
/// Only events that carry a student id in plaintext-encrypted-wire form can
/// be fanned out here; cancel/refund/renewal are parent-keyed and require a
/// separate fan-out projection that reads the current linked-student list —
/// tracked as a follow-up.
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
