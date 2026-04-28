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

        // task t_dc70d2cd9ab9 — trial-then-paywall §5.5.1 precedence scan.
        // We walk the parent-bindings reverse index, load each parent's
        // subscription aggregate, and collect ALL candidate views. The
        // §5.5.1 ordering then picks one:
        //
        //     Active > Trialing > PastDue > Expired/Cancelled/Unsubscribed
        //
        // (Institute-seat is the highest tier in §5.5.1 but is wired in a
        // sibling task — slot left for it in <see cref="PrecedenceRank"/>
        // but not exercised here.) Running this scan FIRST honours the
        // §5.5.1 rule that a paid Active stream from one parent outranks
        // a Trialing stream from another parent, even if the Marten doc
        // (which is keyed on student id, projected by the existing
        // StudentEntitlementProjection) reflects a different one.
        if (_parentBindings is not null)
        {
            var precedenceWinner = await ResolveViaSubscriptionStreamsAsync(
                studentSubjectIdEncrypted, ct);
            if (precedenceWinner is not null) return precedenceWinner;
        }

        // Marten doc lookup. In the Marten-backed deployment, this scans a
        // materialized view populated by StudentEntitlementProjection. Used
        // when we have no parent-bindings store wired (legacy hosts) and the
        // projection is the authoritative source.
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

    /// <summary>
    /// Walk the parent bindings for a student, load each parent's
    /// subscription aggregate, and return the highest-precedence view per
    /// design §5.5.1. Returns null when no parent stream produces an
    /// effective entitlement (then the caller falls through to alpha-grace
    /// + unsubscribed).
    /// </summary>
    private async Task<StudentEntitlementView?> ResolveViaSubscriptionStreamsAsync(
        string studentSubjectIdEncrypted, CancellationToken ct)
    {
        var parentIds = await EnumerateParentsForStudentAsync(
            studentSubjectIdEncrypted, ct);
        if (parentIds.Count == 0) return null;

        var now = _clock.GetUtcNow();
        StudentEntitlementView? best = null;
        int bestRank = int.MaxValue;

        foreach (var parentId in parentIds)
        {
            var aggregate = await _store.LoadAsync(parentId, ct);
            var view = SynthesizeFromAggregate(
                studentSubjectIdEncrypted, parentId, aggregate.State, now);
            if (view is null) continue;

            var rank = PrecedenceRank(view.EffectiveStatus);
            if (rank < bestRank)
            {
                best = view;
                bestRank = rank;
                // Active is the strongest signal we can find here (Institute
                // is enqueued for a later task). Short-circuit on Active so
                // we don't keep loading further aggregates.
                if (rank == ActiveRank) break;
            }
        }

        return best;
    }

    // ----- Precedence rank helpers --------------------------------------
    //
    // Lower rank = higher precedence. The constants are spaced so the
    // future Institute-seat winner can slot in below Active without
    // renumbering. Aligns 1:1 with design §5.5.1.

    /// <summary>Reserved for future institute-seat slot.</summary>
    internal const int InstituteRank = 0;
    internal const int ActiveRank = 10;
    internal const int TrialingRank = 20;
    internal const int PastDueRank = 30;
    // Expired / Cancelled / Refunded / Unsubscribed are not winners; we
    // synthesize Unsubscribed-equivalent and never make them rank-winners.

    private static int PrecedenceRank(SubscriptionStatus s) => s switch
    {
        SubscriptionStatus.Active => ActiveRank,
        SubscriptionStatus.Trialing => TrialingRank,
        SubscriptionStatus.PastDue => PastDueRank,
        _ => int.MaxValue,
    };

    /// <summary>
    /// Build a candidate <see cref="StudentEntitlementView"/> from a single
    /// parent's subscription aggregate, applying the §5.5.1 mapping:
    ///
    ///   Active  → use the parent's currently-active tier.
    ///   Trialing→ TrialPlus + EffectiveStatus=Trialing + ValidUntil = trial end.
    ///   PastDue → keep the parent's tier (grace window per design §5.16; the
    ///             actual grace-window expiry guard lives in the cap enforcer).
    ///   Expired / Cancelled / Refunded / Unsubscribed → null (no precedence
    ///             candidate; resolver falls through to unsubscribed-equivalent).
    ///
    /// Returns null when the candidate is not a precedence winner so the
    /// scan can shortcut on Active without false positives from terminal
    /// states.
    /// </summary>
    private static StudentEntitlementView? SynthesizeFromAggregate(
        string studentSubjectIdEncrypted,
        string parentSubjectIdEncrypted,
        SubscriptionState state,
        DateTimeOffset now)
    {
        // Only return a candidate if THIS student is linked to THIS parent's
        // subscription. A student bound to a parent that has never linked
        // them on the subscription stream gets no entitlement from that
        // parent. Empty linked-student list (fresh-stream Unsubscribed) also
        // returns null.
        var hasStudent = state.LinkedStudents
            .Any(ls => string.Equals(
                ls.StudentSubjectIdEncrypted,
                studentSubjectIdEncrypted,
                StringComparison.Ordinal));
        if (!hasStudent && state.Status != SubscriptionStatus.Trialing)
        {
            // For trials we still allow synthesis even if the linked-student
            // record was added late — the trial cycle pins a primary student
            // at start, but the parent-binding reverse index is the
            // authoritative "is this my child?" check.
            return null;
        }

        switch (state.Status)
        {
            case SubscriptionStatus.Active:
                return new StudentEntitlementView(
                    StudentSubjectIdEncrypted: studentSubjectIdEncrypted,
                    EffectiveTier: state.CurrentTier,
                    SourceParentSubjectIdEncrypted: parentSubjectIdEncrypted,
                    ValidUntil: state.RenewsAt,
                    LastUpdatedAt: now,
                    EffectiveStatus: SubscriptionStatus.Active);

            case SubscriptionStatus.Trialing:
                // Calendar boundary check: if the trial has passed its
                // pinned end without a TrialExpired_V1 yet, treat as not
                // entitled (the worker will catch up; the SPA must not
                // see one extra "free" call). Cap-only trials
                // (TrialEndsAt == TrialStartedAt) stay effective.
                if (!state.IsTrialingAsOf(now)) return null;
                return new StudentEntitlementView(
                    StudentSubjectIdEncrypted: studentSubjectIdEncrypted,
                    EffectiveTier: SubscriptionTier.TrialPlus,
                    SourceParentSubjectIdEncrypted: parentSubjectIdEncrypted,
                    ValidUntil: state.TrialEndsAt,
                    LastUpdatedAt: now,
                    EffectiveStatus: SubscriptionStatus.Trialing);

            case SubscriptionStatus.PastDue:
                return new StudentEntitlementView(
                    StudentSubjectIdEncrypted: studentSubjectIdEncrypted,
                    EffectiveTier: state.CurrentTier,
                    SourceParentSubjectIdEncrypted: parentSubjectIdEncrypted,
                    ValidUntil: state.RenewsAt,
                    LastUpdatedAt: now,
                    EffectiveStatus: SubscriptionStatus.PastDue);

            // Expired / Cancelled / Refunded / Unsubscribed: not a
            // precedence winner. Caller falls through.
            default:
                return null;
        }
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
    /// Used as the canonical fallback for expired-and-equivalent terminal
    /// states per design §5.5.1 (Expired returns the same paywall-hit view).
    /// </summary>
    public static StudentEntitlementView SynthesizeUnsubscribed(string studentSubjectIdEncrypted) =>
        new(
            StudentSubjectIdEncrypted: studentSubjectIdEncrypted,
            EffectiveTier: SubscriptionTier.Unsubscribed,
            SourceParentSubjectIdEncrypted: string.Empty,
            ValidUntil: null,
            LastUpdatedAt: DateTimeOffset.UtcNow,
            EffectiveStatus: SubscriptionStatus.Unsubscribed);

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
            LastUpdatedAt: DateTimeOffset.UtcNow,
            EffectiveStatus: SubscriptionStatus.Active);
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
