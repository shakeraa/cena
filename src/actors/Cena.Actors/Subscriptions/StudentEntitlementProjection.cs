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
///
/// Trial events are intentionally NOT projected here: trial views always
/// flow through the parent-bindings path of
/// <see cref="StudentEntitlementResolver"/> so the pinned
/// <see cref="TrialCapsSnapshot"/> is available on the hot path. Trial
/// data has no place in this read model.
/// </summary>
public sealed class StudentEntitlementProjection : EventProjection
{
    /// <summary>
    /// Activation: upsert entitlement doc for the primary student. Preserves
    /// any pre-existing <see cref="StudentEntitlementDocument.HasPaymentMethodOnFile"/>
    /// flag — when an Attach event landed before Activate (the common
    /// trial-then-pay sequence), we must not regress the flag.
    /// </summary>
    public async Task Project(
        SubscriptionActivated_V1 e, IDocumentOperations ops, CancellationToken ct)
    {
        var existing = await ops.LoadAsync<StudentEntitlementDocument>(
            e.PrimaryStudentSubjectIdEncrypted, ct).ConfigureAwait(false);
        ops.Store(new StudentEntitlementDocument
        {
            Id = e.PrimaryStudentSubjectIdEncrypted,
            EffectiveTier = e.Tier,
            SourceParentSubjectIdEncrypted = e.ParentSubjectIdEncrypted,
            ValidUntil = e.RenewsAt,
            LastUpdatedAt = e.ActivatedAt,
            HasPaymentMethodOnFile = existing?.HasPaymentMethodOnFile ?? false,
        });
    }

    /// <summary>Sibling linked: upsert entitlement doc for the sibling.</summary>
    public async Task Project(
        SiblingEntitlementLinked_V1 e, IDocumentOperations ops, CancellationToken ct)
    {
        var existing = await ops.LoadAsync<StudentEntitlementDocument>(
            e.SiblingStudentSubjectIdEncrypted, ct).ConfigureAwait(false);
        ops.Store(new StudentEntitlementDocument
        {
            Id = e.SiblingStudentSubjectIdEncrypted,
            EffectiveTier = e.Tier,
            SourceParentSubjectIdEncrypted = e.ParentSubjectIdEncrypted,
            ValidUntil = null,   // inherits parent renewal; dedicated fan-out follow-up
            LastUpdatedAt = e.LinkedAt,
            // A sibling sharing the parent's stream inherits the parent's
            // payment-method-on-file state. Preserve any prior flag (rare
            // edge case where the same student id was previously linked).
            HasPaymentMethodOnFile = existing?.HasPaymentMethodOnFile ?? false,
        });
    }

    /// <summary>Sibling unlinked: remove entitlement doc.</summary>
    public void Project(SiblingEntitlementUnlinked_V1 e, IDocumentOperations ops)
    {
        ops.Delete<StudentEntitlementDocument>(e.SiblingStudentSubjectIdEncrypted);
    }

    /// <summary>
    /// Phase 1D-fix-2 item 4: payment-method-attached fan-out. Updates the
    /// HasPaymentMethodOnFile flag on every entitlement document that
    /// currently sources from this parent. We don't carry a parent→students
    /// reverse index in the projection, so we query the documents by
    /// SourceParentSubjectIdEncrypted. At pilot scale this is cheap; at
    /// 10k+ households a parent→student-list index document is the
    /// follow-up.
    /// </summary>
    public async Task Project(
        SubscriptionPaymentMethodAttached_V1 e,
        IDocumentOperations ops,
        CancellationToken ct)
    {
        // Marten's IDocumentOperations supports IQueryable via Query<T>().
        // The SourceParentSubjectIdEncrypted column is plain-stringly indexed
        // by Marten's default JSON column → small scan at pilot scale.
        var matched = await ops
            .Query<StudentEntitlementDocument>()
            .Where(d => d.SourceParentSubjectIdEncrypted == e.ParentSubjectIdEncrypted)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        foreach (var doc in matched)
        {
            if (doc.HasPaymentMethodOnFile) continue;
            doc.HasPaymentMethodOnFile = true;
            doc.LastUpdatedAt = e.AttachedAt;
            ops.Store(doc);
        }
    }
}
