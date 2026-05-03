// =============================================================================
// Cena Platform — UnsubscribeEndpoint integration tests (prr-051).
//
// Covers:
//   - Valid token: first use unsubscribes, appends ParentDigestUnsubscribed_V1,
//     second use returns already_unsubscribed (idempotent 200).
//   - Tampered token: 403, no event.
//   - Expired token: 410, no event.
//   - Cross-tenant token: 403, no event, nonce NOT consumed.
//   - Missing / unresolvable tenant header: 400.
// =============================================================================

using Cena.Actors.ParentDigest;
using Cena.Actors.ParentDigest.Events;
using Cena.Admin.Api.Features.ParentConsole;
using Marten;
using Marten.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Admin.Api.Tests.ParentConsole;

[Trait("Category", "ParentDigestUnsubscribe")]
public sealed class UnsubscribeEndpointTests
{
    private const string Secret = "prr-051-unsub-test-secret-longenough";
    private const string ParentA = "parent-A";
    private const string ChildA = "child-A";
    private const string InstX = "institute-X";
    private const string InstY = "institute-Y";

    private static readonly DateTimeOffset Now =
        new(2026, 4, 21, 12, 0, 0, TimeSpan.Zero);

    // ── Fixture ─────────────────────────────────────────────────────────

    private sealed class TestContext
    {
        public required IUnsubscribeTokenService TokenService { get; init; }
        public required IParentDigestPreferencesStore PreferencesStore { get; init; }
        public required IDocumentStore DocumentStore { get; init; }
        public required IEventStoreOperations Events { get; init; }
        public required IUnsubscribeTokenNonceStore Nonces { get; init; }
    }

    private static TestContext NewFixture()
    {
        var nonces = new InMemoryUnsubscribeTokenNonceStore();
        var tokens = new UnsubscribeTokenService(Secret, nonces);
        var prefs = new InMemoryParentDigestPreferencesStore();

        var store = Substitute.For<IDocumentStore>();
        var session = Substitute.For<IDocumentSession>();
        var events = Substitute.For<IEventStoreOperations>();
        session.Events.Returns(events);
        store.LightweightSession().Returns(session);

        return new TestContext
        {
            TokenService = tokens,
            PreferencesStore = prefs,
            DocumentStore = store,
            Events = events,
            Nonces = nonces,
        };
    }

    private static HttpContext MakeHttp(string? instituteHeader)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().Build());
        services.AddLogging();

        var ctx = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
        };
        if (!string.IsNullOrWhiteSpace(instituteHeader))
        {
            ctx.Request.Headers[UnsubscribeEndpoint.TenantHeaderName] = instituteHeader;
        }
        return ctx;
    }

    private static ILogger<UnsubscribeEndpoint.UnsubscribeEndpointMarker> NullLogger()
        => NullLoggerFactory.Instance
            .CreateLogger<UnsubscribeEndpoint.UnsubscribeEndpointMarker>();

    // ── Valid + idempotent ──────────────────────────────────────────────

    [Fact]
    public async Task ValidToken_FirstClick_UnsubscribesAndAppendsEvent()
    {
        var ctx = NewFixture();
        var token = ctx.TokenService.Issue(ParentA, ChildA, InstX, Now, TimeSpan.FromDays(14));
        var http = MakeHttp(InstX);

        var result = await UnsubscribeEndpoint.HandleUnsubscribeAsync(
            token, http, ctx.TokenService, ctx.PreferencesStore,
            ctx.DocumentStore, NullLogger(), CancellationToken.None);

        AssertOk200(result);
        var prefs = await ctx.PreferencesStore.FindAsync(ParentA, ChildA, InstX);
        Assert.NotNull(prefs);
        Assert.NotNull(prefs!.UnsubscribedAtUtc);
        foreach (var p in DigestPurposes.KnownPurposes)
            Assert.False(prefs.ShouldSend(p));

        // Audit event was appended with a non-empty fingerprint.
        var appendCall = ctx.Events.ReceivedCalls()
            .FirstOrDefault(c => c.GetMethodInfo().Name == "Append");
        Assert.NotNull(appendCall);
        var args = appendCall!.GetArguments();
        Assert.Equal(ChildA, args[0]);
        var appended = (args[1] as object[])?.FirstOrDefault() ?? args[1];
        var ev = Assert.IsType<ParentDigestUnsubscribed_V1>(appended);
        Assert.Equal(ParentA, ev.ParentActorId);
        Assert.Equal(ChildA, ev.StudentSubjectId);
        Assert.Equal(InstX, ev.InstituteId);
        Assert.NotEmpty(ev.TokenFingerprint);
    }

    [Fact]
    public async Task ValidToken_SecondClick_IsIdempotentAndNoSecondEvent()
    {
        var ctx = NewFixture();
        var token = ctx.TokenService.Issue(ParentA, ChildA, InstX, Now, TimeSpan.FromDays(14));
        var http = MakeHttp(InstX);

        await UnsubscribeEndpoint.HandleUnsubscribeAsync(
            token, http, ctx.TokenService, ctx.PreferencesStore,
            ctx.DocumentStore, NullLogger(), CancellationToken.None);

        var second = await UnsubscribeEndpoint.HandleUnsubscribeAsync(
            token, http, ctx.TokenService, ctx.PreferencesStore,
            ctx.DocumentStore, NullLogger(), CancellationToken.None);
        AssertOk200(second);
        var dto = Assert.IsType<UnsubscribeResultDto>(ExtractValue(second));
        Assert.Equal("already_unsubscribed", dto.Status);

        var appends = ctx.Events.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == "Append");
        Assert.Equal(1, appends);
    }

    // ── Failure cases ───────────────────────────────────────────────────

    [Fact]
    public async Task TamperedToken_Returns403_NoEvent()
    {
        var ctx = NewFixture();
        var token = ctx.TokenService.Issue(ParentA, ChildA, InstX, Now, TimeSpan.FromDays(14));
        var parts = token.Split('.');
        var tampered = parts[0] + "." + (parts[1][^1] == 'A'
            ? parts[1][..^1] + "B" : parts[1][..^1] + "A");
        var http = MakeHttp(InstX);

        var result = await UnsubscribeEndpoint.HandleUnsubscribeAsync(
            tampered, http, ctx.TokenService, ctx.PreferencesStore,
            ctx.DocumentStore, NullLogger(), CancellationToken.None);
        AssertForbid(result);
        Assert.Empty(ctx.Events.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == "Append"));
    }

    [Fact]
    public async Task ExpiredToken_Returns410_NoEvent()
    {
        var ctx = NewFixture();
        // Expiry in the past relative to our arbitrary "now".
        var token = ctx.TokenService.Issue(ParentA, ChildA, InstX,
            nowUtc: Now.AddDays(-20), lifetime: TimeSpan.FromDays(14));
        var http = MakeHttp(InstX);

        var result = await UnsubscribeEndpoint.HandleUnsubscribeAsync(
            token, http, ctx.TokenService, ctx.PreferencesStore,
            ctx.DocumentStore, NullLogger(), CancellationToken.None);
        AssertStatus(result, StatusCodes.Status410Gone);
        Assert.Empty(ctx.Events.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == "Append"));
    }

    [Fact]
    public async Task CrossTenantToken_Returns403_AndDoesNotBurnNonce()
    {
        var ctx = NewFixture();
        // Token issued for InstX but the request is handled from InstY's edge.
        var token = ctx.TokenService.Issue(ParentA, ChildA, InstX, Now, TimeSpan.FromDays(14));
        var httpY = MakeHttp(InstY);

        var result = await UnsubscribeEndpoint.HandleUnsubscribeAsync(
            token, httpY, ctx.TokenService, ctx.PreferencesStore,
            ctx.DocumentStore, NullLogger(), CancellationToken.None);
        AssertForbid(result);

        // Preferences were NOT written — cross-tenant click is a no-op.
        Assert.Null(await ctx.PreferencesStore.FindAsync(ParentA, ChildA, InstX));

        // The legitimate click from the right tenant still works — the
        // cross-tenant probe did not burn the nonce.
        var httpX = MakeHttp(InstX);
        var legit = await UnsubscribeEndpoint.HandleUnsubscribeAsync(
            token, httpX, ctx.TokenService, ctx.PreferencesStore,
            ctx.DocumentStore, NullLogger(), CancellationToken.None);
        AssertOk200(legit);
    }

    [Fact]
    public async Task NoTenantHeader_Returns400()
    {
        var ctx = NewFixture();
        var token = ctx.TokenService.Issue(ParentA, ChildA, InstX, Now, TimeSpan.FromDays(14));
        var http = MakeHttp(instituteHeader: null);

        var result = await UnsubscribeEndpoint.HandleUnsubscribeAsync(
            token, http, ctx.TokenService, ctx.PreferencesStore,
            ctx.DocumentStore, NullLogger(), CancellationToken.None);
        AssertStatus(result, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task EmptyToken_Returns400()
    {
        var ctx = NewFixture();
        var http = MakeHttp(InstX);

        var result = await UnsubscribeEndpoint.HandleUnsubscribeAsync(
            "", http, ctx.TokenService, ctx.PreferencesStore,
            ctx.DocumentStore, NullLogger(), CancellationToken.None);
        AssertStatus(result, StatusCodes.Status400BadRequest);
    }

    // ── Result-shape helpers ────────────────────────────────────────────

    private static void AssertOk200(IResult result)
    {
        AssertStatus(result, StatusCodes.Status200OK);
    }

    private static void AssertStatus(IResult result, int expected)
    {
        var statusProp = result.GetType().GetProperty("StatusCode");
        var status = (int?)statusProp?.GetValue(result);
        Assert.True(status == expected,
            $"Expected {expected}, got {status} ({result.GetType().Name})");
    }

    private static void AssertForbid(IResult result)
    {
        var typeName = result.GetType().Name;
        Assert.Contains("Forbid", typeName, StringComparison.Ordinal);
    }

    private static object? ExtractValue(IResult result)
    {
        var valueProp = result.GetType().GetProperty("Value");
        return valueProp?.GetValue(result);
    }
}
