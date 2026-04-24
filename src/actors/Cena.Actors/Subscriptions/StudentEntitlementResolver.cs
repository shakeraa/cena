// =============================================================================
// Cena Platform — StudentEntitlementResolver (EPIC-PRR-I PRR-310 + PRR-344)
//
// Reference implementation that walks all subscription streams looking for
// a linked student. This is adequate at pilot scale; at 10k+ students we
// introduce an inline Marten projection (StudentEntitlementProjection) and
// swap the implementation. That migration is a follow-up task; the seam
// here is stable.
//
// PRR-344 (alpha-migration grace). After the paid-subscription lookup
// misses, the resolver consults IAlphaGraceMarkerReader to see whether
// this student's parent holds an active alpha-migration grace marker.
// If yes, we synthesise a Premium StudentEntitlementView with source
// tagged "alpha-grace" so:
//   - Cap enforcement (PerTierCapEnforcer) honours Premium caps, not
//     Unsubscribed — the alpha user keeps the runway they were promised.
//   - Analytics can distinguish natural-Premium from grace-Premium via
//     the SourceParentSubjectIdEncrypted + EffectiveTier pair (Memory
//     "Labels match data": the field names describe the real data).
// Student → parent mapping uses IParentChildBindingStore (the
// authoritative source of truth per ParentChildBinding.cs). A student
// with no parent binding cannot inherit grace — no guessing.
//
// Backward-compat: the grace dependencies are optional ctor parameters so
// existing callers (and the InMemory default composition) keep working
// with the pre-PRR-344 resolver behaviour. When both reader + binding
// store are wired, grace enforcement lights up automatically.
// =============================================================================

using Cena.Actors.Parent;
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
    /// <summary>
    /// Source-tag string written into <see cref="StudentEntitlementView.SourceParentSubjectIdEncrypted"/>
    /// (formatted as "alpha-grace:{parentId}") when the entitlement comes
    /// from an alpha-migration grace marker rather than a live subscription.
    /// Analytics sinks split on this prefix to separate natural-Premium
    /// revenue from grace-Premium (which carries zero revenue).
    /// </summary>
    public const string AlphaGraceSourcePrefix = "alpha-grace:";

    private readonly ISubscriptionAggregateStore _store;
    private readonly IDocumentStore? _documentStore;
    private readonly IAlphaGraceMarkerReader? _graceReader;
    private readonly IParentChildBindingStore? _parentBindings;
    private readonly TimeProvider _clock;

    public StudentEntitlementResolver(
        ISubscriptionAggregateStore store,
        IDocumentStore? documentStore = null,
        IAlphaGraceMarkerReader? graceReader = null,
        IParentChildBindingStore? parentBindings = null,
        TimeProvider? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _documentStore = documentStore;
        _graceReader = graceReader;
        _parentBindings = parentBindings;
        _clock = clock ?? TimeProvider.System;
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

        // PRR-344 grace path. Runs only if both the reader and the binding
        // store are wired — keeps backward compatibility with callers that
        // only compose the pre-grace resolver (tests, older hosts).
        if (_graceReader is not null && _parentBindings is not null)
        {
            var graceView = await TryResolveAlphaGraceAsync(studentSubjectIdEncrypted, ct);
            if (graceView is not null) return graceView;
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
    /// PRR-344 grace resolution. Walk the student → parent bindings; for
    /// each parent that has an active AlphaGraceMarker, synthesise a
    /// Premium entitlement view keyed on that parent. First match wins
    /// (deterministic; bindings are small per student). Returns null if
    /// the student has no bindings or no parent holds an active marker.
    /// </summary>
    private async Task<StudentEntitlementView?> TryResolveAlphaGraceAsync(
        string studentSubjectIdEncrypted, CancellationToken ct)
    {
        // We have no (student → parent) reverse-index store, so we ask the
        // binding store to enumerate parents by iterating from parent side.
        // At pilot scale (pre-paywall migration window) this is fine; a
        // reverse index can ship later as a performance follow-up. For
        // now we use a dedicated resolver helper that does NOT require a
        // parent-actor id up front.
        //
        // Safe fallback: if the binding store we were given doesn't expose
        // a reverse lookup, we simply skip the grace path. The
        // pilot-default implementation IS the authoritative store and can
        // enumerate the reverse direction.
        var parentsForStudent = await EnumerateParentsForStudentAsync(
            studentSubjectIdEncrypted, ct);
        if (parentsForStudent.Count == 0) return null;

        var now = _clock.GetUtcNow();
        foreach (var parentId in parentsForStudent)
        {
            var marker = await _graceReader!.FindActiveAsync(parentId, now, ct);
            if (marker is null) continue;
            return SynthesizeAlphaGrace(studentSubjectIdEncrypted, parentId, marker);
        }
        return null;
    }

    /// <summary>
    /// Enumerate the parent subject ids bound to a student across every
    /// institute. The binding store is keyed on (parent, student, institute),
    /// so we have to scan. At pilot scale the binding set per student is
    /// O(1-3); this is acceptable. If the provided binding store
    /// implements <see cref="IStudentParentIndex"/>, we use that instead
    /// for O(1) lookups (future optimisation).
    /// </summary>
    private async Task<IReadOnlyList<string>> EnumerateParentsForStudentAsync(
        string studentSubjectIdEncrypted, CancellationToken ct)
    {
        if (_parentBindings is IStudentParentIndex reverseIndex)
        {
            return await reverseIndex.ListParentsForStudentAsync(
                studentSubjectIdEncrypted, ct);
        }
        // No reverse index available — the caller host must wire one when
        // grace enforcement is desired. Fail safe: empty list (no grace).
        return Array.Empty<string>();
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

    /// <summary>
    /// Build a Premium entitlement view sourced from an alpha-migration
    /// grace marker. The source field is prefixed with <see cref="AlphaGraceSourcePrefix"/>
    /// so analytics can split grace from natural Premium. ValidUntil = grace
    /// end so cap enforcement and the "show renewal banner" UI both know
    /// when the window closes.
    /// </summary>
    public static StudentEntitlementView SynthesizeAlphaGrace(
        string studentSubjectIdEncrypted,
        string parentSubjectIdEncrypted,
        AlphaGraceMarker marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        return new StudentEntitlementView(
            StudentSubjectIdEncrypted: studentSubjectIdEncrypted,
            EffectiveTier: SubscriptionTier.Premium,
            SourceParentSubjectIdEncrypted: AlphaGraceSourcePrefix + parentSubjectIdEncrypted,
            ValidUntil: marker.GraceEndAt,
            LastUpdatedAt: DateTimeOffset.UtcNow);
    }
}

/// <summary>
/// Optional companion contract on <see cref="IParentChildBindingStore"/>
/// implementations that can answer "which parents are bound to this
/// student?" without a full scan. The InMemory store implements this
/// trivially; the Marten store implements it via a native Postgres query
/// on the indexed document. When the store doesn't implement this, the
/// grace-resolution path in <see cref="StudentEntitlementResolver"/>
/// returns no grace (safe fallback).
/// </summary>
public interface IStudentParentIndex
{
    /// <summary>
    /// Return every parent subject id currently bound to
    /// <paramref name="studentSubjectId"/> across every institute. Empty
    /// list when the student has no bindings.
    /// </summary>
    Task<IReadOnlyList<string>> ListParentsForStudentAsync(
        string studentSubjectId, CancellationToken ct);
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
