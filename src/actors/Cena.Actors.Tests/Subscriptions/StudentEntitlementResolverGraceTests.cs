// =============================================================================
// Cena Platform — StudentEntitlementResolver grace tests (EPIC-PRR-I PRR-344)
//
// Locks the alpha-migration grace path inside StudentEntitlementResolver.
// Tests run without Marten or ASP.NET: in-memory binding store +
// in-memory grace marker reader drive the resolver directly.
//
// DoD coverage (PRR-344):
//   1. Student whose parent has an active grace marker → Premium
//      entitlement with source tagged "alpha-grace:<parentId>" + valid-
//      until set to the marker's GraceEndAt.
//   2. Student whose parent's marker has EXPIRED → Unsubscribed.
//   3. Student with no parent binding → Unsubscribed (no grace).
// =============================================================================

using Cena.Actors.Parent;
using Cena.Actors.Subscriptions;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class StudentEntitlementResolverGraceTests
{
    private const string ParentId = "enc::parent::alpha-01";
    private const string StudentId = "enc::student::alpha-01-kid";
    private const string InstituteId = "school-01";

    private static readonly DateTimeOffset TestNow =
        new(2026, 4, 23, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeProvider FakeClock =
        new FixedClock(TestNow);

    [Fact]
    public async Task Student_with_active_parent_grace_marker_gets_premium()
    {
        var bindings = new InMemoryParentChildBindingStore();
        await bindings.GrantAsync(ParentId, StudentId, InstituteId, TestNow, CancellationToken.None);

        var reader = new InMemoryAlphaGraceMarkerReader();
        reader.Upsert(new AlphaGraceMarker
        {
            Id = ParentId,
            GraceStartAt = TestNow.AddDays(-10),
            GraceEndAt = TestNow.AddDays(50), // still active
            Reason = "alpha-user",
        });

        var resolver = new StudentEntitlementResolver(
            store: new InMemorySubscriptionAggregateStore(),
            documentStore: null,
            graceReader: reader,
            parentBindings: bindings,
            clock: FakeClock);

        var view = await resolver.ResolveAsync(StudentId, CancellationToken.None);

        Assert.Equal(SubscriptionTier.Premium, view.EffectiveTier);
        Assert.StartsWith(
            StudentEntitlementResolver.AlphaGraceSourcePrefix,
            view.SourceParentSubjectIdEncrypted);
        Assert.Contains(ParentId, view.SourceParentSubjectIdEncrypted);
        Assert.Equal(TestNow.AddDays(50), view.ValidUntil);
    }

    [Fact]
    public async Task Student_whose_parent_grace_expired_falls_through_to_unsubscribed()
    {
        var bindings = new InMemoryParentChildBindingStore();
        await bindings.GrantAsync(ParentId, StudentId, InstituteId, TestNow, CancellationToken.None);

        var reader = new InMemoryAlphaGraceMarkerReader();
        reader.Upsert(new AlphaGraceMarker
        {
            Id = ParentId,
            GraceStartAt = TestNow.AddDays(-70),
            GraceEndAt = TestNow.AddDays(-10), // already expired
            Reason = "alpha-user",
        });

        var resolver = new StudentEntitlementResolver(
            store: new InMemorySubscriptionAggregateStore(),
            documentStore: null,
            graceReader: reader,
            parentBindings: bindings,
            clock: FakeClock);

        var view = await resolver.ResolveAsync(StudentId, CancellationToken.None);

        Assert.Equal(SubscriptionTier.Unsubscribed, view.EffectiveTier);
        Assert.Equal(string.Empty, view.SourceParentSubjectIdEncrypted);
    }

    [Fact]
    public async Task Student_with_no_parent_binding_gets_unsubscribed()
    {
        var bindings = new InMemoryParentChildBindingStore();
        // No Grant called — student has zero bindings.

        var reader = new InMemoryAlphaGraceMarkerReader();
        // Even if there's a marker for SOME parent, without a binding
        // to this student we can't honour it — no guessing.
        reader.Upsert(new AlphaGraceMarker
        {
            Id = "enc::parent::someone-else",
            GraceStartAt = TestNow.AddDays(-10),
            GraceEndAt = TestNow.AddDays(50),
            Reason = "alpha-user",
        });

        var resolver = new StudentEntitlementResolver(
            store: new InMemorySubscriptionAggregateStore(),
            documentStore: null,
            graceReader: reader,
            parentBindings: bindings,
            clock: FakeClock);

        var view = await resolver.ResolveAsync(StudentId, CancellationToken.None);

        Assert.Equal(SubscriptionTier.Unsubscribed, view.EffectiveTier);
    }

    [Fact]
    public async Task Resolver_without_grace_dependencies_keeps_prerr344_behaviour()
    {
        // Backward-compat check: legacy callers that don't wire the grace
        // reader + binding store get the same Unsubscribed fallback they
        // always got. Ensures we didn't force every caller to upgrade.
        var resolver = new StudentEntitlementResolver(
            store: new InMemorySubscriptionAggregateStore());

        var view = await resolver.ResolveAsync(StudentId, CancellationToken.None);

        Assert.Equal(SubscriptionTier.Unsubscribed, view.EffectiveTier);
    }

    [Fact]
    public async Task Grace_source_prefix_distinguishes_from_natural_premium()
    {
        // Analytics invariant (Memory "Labels match data"): the source
        // field carries "alpha-grace:<parentId>", NOT a bare parent id.
        // This lets analytics split grace-Premium from natural-Premium
        // at query time without joining against a second table.
        var bindings = new InMemoryParentChildBindingStore();
        await bindings.GrantAsync(ParentId, StudentId, InstituteId, TestNow, CancellationToken.None);

        var reader = new InMemoryAlphaGraceMarkerReader();
        reader.Upsert(new AlphaGraceMarker
        {
            Id = ParentId,
            GraceStartAt = TestNow.AddDays(-1),
            GraceEndAt = TestNow.AddDays(59),
            Reason = "alpha-user",
        });

        var resolver = new StudentEntitlementResolver(
            store: new InMemorySubscriptionAggregateStore(),
            documentStore: null,
            graceReader: reader,
            parentBindings: bindings,
            clock: FakeClock);

        var view = await resolver.ResolveAsync(StudentId, CancellationToken.None);
        Assert.Equal(
            StudentEntitlementResolver.AlphaGraceSourcePrefix + ParentId,
            view.SourceParentSubjectIdEncrypted);
    }

    // ---- test doubles -----------------------------------------------------

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
