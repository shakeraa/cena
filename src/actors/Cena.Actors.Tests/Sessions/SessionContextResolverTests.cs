// =============================================================================
// Cena Platform — SessionContextResolver + NullSessionContextResolver tests
// (EPIC-PRR-I PRR-310, SLICE 1)
//
// Covers:
//   - Resolver composes IStudentEntitlementResolver → SessionContext
//   - Resolver with an Unsubscribed entitlement still returns a
//     non-null SessionContext (with Unsubscribed tier + zero caps)
//   - NullSessionContextResolver deny-everything fallback
//   - Input validation at the resolver seam
// =============================================================================

using Cena.Actors.Sessions;
using Cena.Actors.Subscriptions;
using NSubstitute;

namespace Cena.Actors.Tests.Sessions;

public class SessionContextResolverTests
{
    private static StudentEntitlementView EntitlementFor(
        SubscriptionTier tier, string studentId) =>
        new(
            StudentSubjectIdEncrypted: studentId,
            EffectiveTier: tier,
            SourceParentSubjectIdEncrypted: "parent-enc-1",
            ValidUntil: DateTimeOffset.UtcNow.AddDays(30),
            LastUpdatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task Resolver_pins_tier_caps_and_features_from_entitlement_resolver()
    {
        var entitlements = Substitute.For<IStudentEntitlementResolver>();
        entitlements
            .ResolveAsync("student-enc-1", Arg.Any<CancellationToken>())
            .Returns(EntitlementFor(SubscriptionTier.Premium, "student-enc-1"));

        var sut = new SessionContextResolver(entitlements);

        var started = new DateTimeOffset(2026, 4, 23, 9, 0, 0, TimeSpan.Zero);
        var ctx = await sut.ResolveAtSessionStartAsync(
            "sess-1", "student-enc-1", started, CancellationToken.None);

        Assert.Equal("sess-1", ctx.SessionId);
        Assert.Equal("student-enc-1", ctx.StudentSubjectIdEncrypted);
        Assert.Equal(SubscriptionTier.Premium, ctx.PinnedTier);
        // Caps & features snapshot must match what the catalog says Premium
        // is at the moment the snapshot was taken.
        var expected = TierCatalog.Get(SubscriptionTier.Premium);
        Assert.Equal(expected.Caps, ctx.PinnedCaps);
        Assert.Equal(expected.Features, ctx.PinnedFeatures);
        Assert.Equal(started, ctx.StartedAt);
    }

    [Fact]
    public async Task Resolver_returns_Unsubscribed_context_when_entitlement_is_Unsubscribed()
    {
        // A student with no active subscription still gets a SessionContext —
        // the hot path always has a live snapshot to enforce against
        // (deny-everything posture). Must NOT throw.
        var entitlements = Substitute.For<IStudentEntitlementResolver>();
        entitlements
            .ResolveAsync("student-enc-2", Arg.Any<CancellationToken>())
            .Returns(EntitlementFor(SubscriptionTier.Unsubscribed, "student-enc-2"));

        var sut = new SessionContextResolver(entitlements);

        var ctx = await sut.ResolveAtSessionStartAsync(
            "sess-2", "student-enc-2", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.NotNull(ctx);
        Assert.Equal(SubscriptionTier.Unsubscribed, ctx.PinnedTier);
        Assert.Equal(0, ctx.PinnedCaps.SonnetEscalationsPerWeek);
        Assert.Equal(0, ctx.PinnedCaps.PhotoDiagnosticsPerMonth);
        Assert.Equal(0, ctx.PinnedCaps.HintRequestsPerMonth);
        Assert.False(ctx.PinnedFeatures.ParentDashboard);
    }

    [Fact]
    public async Task Resolver_throws_on_empty_session_id()
    {
        var entitlements = Substitute.For<IStudentEntitlementResolver>();
        var sut = new SessionContextResolver(entitlements);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ResolveAtSessionStartAsync(
                "", "student-enc-1", DateTimeOffset.UtcNow, CancellationToken.None));
    }

    [Fact]
    public async Task Resolver_throws_on_empty_student_id()
    {
        var entitlements = Substitute.For<IStudentEntitlementResolver>();
        var sut = new SessionContextResolver(entitlements);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sut.ResolveAtSessionStartAsync(
                "sess-1", "", DateTimeOffset.UtcNow, CancellationToken.None));
    }

    [Fact]
    public async Task NullSessionContextResolver_returns_Unsubscribed_context()
    {
        var sut = NullSessionContextResolver.Instance;

        var ctx = await sut.ResolveAtSessionStartAsync(
            "sess-null", "student-null", DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(SubscriptionTier.Unsubscribed, ctx.PinnedTier);
        Assert.Equal(0, ctx.PinnedCaps.SonnetEscalationsPerWeek);
        Assert.Equal(0, ctx.PinnedCaps.PhotoDiagnosticsPerMonth);
        Assert.Equal(0, ctx.PinnedCaps.HintRequestsPerMonth);
        Assert.False(ctx.PinnedFeatures.ParentDashboard);
        Assert.False(ctx.PinnedFeatures.TutorHandoffPdf);
        Assert.False(ctx.PinnedFeatures.PrioritySupport);
    }

    [Fact]
    public void Resolver_rejects_null_entitlement_dependency()
    {
        Assert.Throws<ArgumentNullException>(() => new SessionContextResolver(null!));
    }
}
