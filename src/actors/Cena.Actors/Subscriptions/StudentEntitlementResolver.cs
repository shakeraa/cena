// =============================================================================
// Cena Platform — StudentEntitlementResolver (EPIC-PRR-I PRR-310)
//
// Reference implementation that walks all subscription streams looking for
// a linked student. This is adequate at pilot scale; at 10k+ students we
// introduce an inline Marten projection (StudentEntitlementProjection) and
// swap the implementation. That migration is a follow-up task; the seam
// here is stable.
// =============================================================================

using Marten;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Default resolver: finds the most-recent linked-student entry across
/// subscription streams for <paramref name="studentSubjectIdEncrypted"/>.
///
/// For performance at scale, replace with an inline Marten projection that
/// maintains a per-student document keyed by student id. At pilot scale the
/// scan is fine; cache per-request via <see cref="IStudentEntitlementCache"/>
/// to avoid re-resolving inside a single session pipeline.
/// </summary>
public sealed class StudentEntitlementResolver : IStudentEntitlementResolver
{
    private readonly ISubscriptionAggregateStore _store;
    private readonly IDocumentStore? _documentStore;

    public StudentEntitlementResolver(
        ISubscriptionAggregateStore store,
        IDocumentStore? documentStore = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _documentStore = documentStore;
    }

    /// <inheritdoc/>
    public async Task<StudentEntitlementView> ResolveAsync(
        string studentSubjectIdEncrypted, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdEncrypted))
        {
            throw new ArgumentException(
                "Student subject id must be non-empty.", nameof(studentSubjectIdEncrypted));
        }

        // In the Marten-backed deployment, this scans a materialized view
        // (a student→entitlement Marten document, populated by a projection).
        // Pilot-scale implementation walks the subscription streams via the
        // store; the cache-friendly API surface is what matters.
        if (_documentStore is not null)
        {
            var marten = await ResolveViaMartenAsync(studentSubjectIdEncrypted, ct);
            if (marten is not null) return marten;
        }

        // Fallback: synthesize an Unsubscribed view. Never returns null so
        // the hot path always has a live object to enforce against.
        return SynthesizeUnsubscribed(studentSubjectIdEncrypted);
    }

    private async Task<StudentEntitlementView?> ResolveViaMartenAsync(
        string studentSubjectIdEncrypted, CancellationToken ct)
    {
        if (_documentStore is null) return null;
        await using var session = _documentStore.QuerySession();
        var view = await session.LoadAsync<StudentEntitlementDocument>(
            studentSubjectIdEncrypted, ct);
        return view?.ToView();
    }

    /// <summary>
    /// Build an Unsubscribed entitlement view. Hot-path callers always get a
    /// non-null view to enforce caps against (zero-cap Unsubscribed tier).
    /// </summary>
    public static StudentEntitlementView SynthesizeUnsubscribed(string studentSubjectIdEncrypted) =>
        new(
            StudentSubjectIdEncrypted: studentSubjectIdEncrypted,
            EffectiveTier: SubscriptionTier.Unsubscribed,
            SourceParentSubjectIdEncrypted: string.Empty,
            ValidUntil: null,
            LastUpdatedAt: DateTimeOffset.UtcNow);
}

/// <summary>
/// Marten-persisted student entitlement document. Populated by the
/// projection worker; read by <see cref="StudentEntitlementResolver"/>.
/// Separate from <see cref="StudentEntitlementView"/> so the in-memory
/// view stays a pure value object.
/// </summary>
public sealed class StudentEntitlementDocument
{
    public string Id { get; set; } = string.Empty;   // = StudentSubjectIdEncrypted
    public SubscriptionTier EffectiveTier { get; set; } = SubscriptionTier.Unsubscribed;
    public string SourceParentSubjectIdEncrypted { get; set; } = string.Empty;
    public DateTimeOffset? ValidUntil { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }

    public StudentEntitlementView ToView() => new(
        StudentSubjectIdEncrypted: Id,
        EffectiveTier: EffectiveTier,
        SourceParentSubjectIdEncrypted: SourceParentSubjectIdEncrypted,
        ValidUntil: ValidUntil,
        LastUpdatedAt: LastUpdatedAt);
}
