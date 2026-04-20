// =============================================================================
// Cena Platform — ParentDigestPreferencesEndpoints integration tests (prr-051).
//
// Exercises the GET + POST handlers end-to-end through their public
// entrypoints. Covers the DoD cases in the task body:
//
//   (a) Defaults correct — a fresh parent sees safety_alerts opted_in
//       and every other purpose opted_out in the GET response.
//   (b) POST with a partial map only touches the mentioned purposes.
//   (c) Cross-tenant PARENT (session bound at institute X tries to write
//       preferences for a child that only exists at institute Y) is blocked
//       by ParentAuthorizationGuard.
//   (d) Unknown purpose / status strings are rejected with 400.
//   (e) An audit event (ParentDigestPreferencesUpdated_V1) is appended.
// =============================================================================

using System.Collections.Immutable;
using System.Security.Claims;
using Cena.Actors.Parent;
using Cena.Actors.ParentDigest;
using Cena.Actors.ParentDigest.Events;
using Cena.Admin.Api.Features.ParentConsole;
using Cena.Infrastructure.Security;
using Marten;
using Marten.Events;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Admin.Api.Tests.ParentConsole;

[Trait("Category", "ParentDigestPreferences")]
public sealed class ParentDigestPreferencesEndpointsTests
{
    private const string ParentA = "parent-A";
    private const string ParentB = "parent-B";
    private const string ChildA = "child-A";
    private const string ChildB = "child-B";
    private const string InstX = "institute-X";
    private const string InstY = "institute-Y";

    // ── Fixture helpers ─────────────────────────────────────────────────

    private sealed class TestContext
    {
        public required IParentDigestPreferencesStore PreferencesStore { get; init; }
        public required IParentChildBindingStore BindingStore { get; init; }
        public required IDocumentStore DocumentStore { get; init; }
        public required IEventStoreOperations Events { get; init; }
        public required IServiceProvider Services { get; init; }
    }

    private static TestContext NewFixture()
    {
        var prefs = new InMemoryParentDigestPreferencesStore();
        var bindings = new InMemoryParentChildBindingStore();
        bindings.GrantAsync(ParentA, ChildA, InstX, DateTimeOffset.UtcNow).GetAwaiter().GetResult();
        bindings.GrantAsync(ParentB, ChildB, InstX, DateTimeOffset.UtcNow).GetAwaiter().GetResult();

        var store = Substitute.For<IDocumentStore>();
        var session = Substitute.For<IDocumentSession>();
        var events = Substitute.For<IEventStoreOperations>();
        session.Events.Returns(events);
        store.LightweightSession().Returns(session);

        var services = new ServiceCollection()
            .AddSingleton<IParentChildBindingService>(new ParentChildBindingService(bindings))
            .AddSingleton<IParentDigestPreferencesStore>(prefs)
            .AddSingleton(store)
            .AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance)
            .AddLogging()
            .BuildServiceProvider();

        return new TestContext
        {
            PreferencesStore = prefs,
            BindingStore = bindings,
            DocumentStore = store,
            Events = events,
            Services = services,
        };
    }

    private static HttpContext MakeHttp(TestContext ctx, ClaimsPrincipal user)
        => new DefaultHttpContext
        {
            User = user,
            RequestServices = ctx.Services,
        };

    private static ClaimsPrincipal MakeParent(
        string parentActorId, string instituteId,
        params (string studentId, string instituteId)[] boundPairs)
    {
        var claims = new List<Claim>
        {
            new("sub", parentActorId),
            new("parentAnonId", parentActorId),
            new(ClaimTypes.Role, "PARENT"),
            new("institute_id", instituteId),
        };
        foreach (var (sid, iid) in boundPairs)
        {
            claims.Add(new Claim(
                "parent_of",
                $"{{\"studentId\":\"{sid}\",\"instituteId\":\"{iid}\"}}"));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
    }

    // ── (a) Defaults correct ────────────────────────────────────────────

    [Fact]
    public async Task Get_WithNoStoredRow_ReturnsDefaultResolvedDto()
    {
        var ctx = NewFixture();
        var http = MakeHttp(ctx, MakeParent(ParentA, InstX, (ChildA, InstX)));

        // Exercise the GET handler directly. We can't invoke the delegate
        // through MapGet without a WebApplicationFactory, but the handler
        // function is an async Task<IResult> static that we can call with
        // the DI services resolved from http.RequestServices.
        var result = await InvokeGet(ctx, http, ChildA);
        var dto = Assert.IsType<ParentDigestPreferencesDto>(ExtractValue(result));

        Assert.Equal(ChildA, dto.StudentAnonId);
        Assert.False(dto.Unsubscribed);
        Assert.Null(dto.UnsubscribedAtUtc);

        // Every known purpose is present, and defaults match the task body.
        Assert.Equal(5, dto.Purposes.Count);
        var safety = dto.Purposes.Single(p => p.Purpose == "safety_alerts");
        Assert.Equal("opted_in", safety.Status);
        foreach (var wire in new[] { "weekly_summary", "homework_reminders", "exam_readiness", "accommodations_changes" })
        {
            Assert.Equal("opted_out", dto.Purposes.Single(p => p.Purpose == wire).Status);
        }
    }

    // ── (b) Partial update only touches mentioned purposes ──────────────

    [Fact]
    public async Task Post_PartialUpdate_OnlyMentionedPurposesChange_AndEventAppended()
    {
        var ctx = NewFixture();
        var http = MakeHttp(ctx, MakeParent(ParentA, InstX, (ChildA, InstX)));

        var request = new SetParentDigestPreferencesRequest(
            StudentAnonId: ChildA,
            Purposes: new Dictionary<string, string>
            {
                ["weekly_summary"] = "opted_in",
            });

        var result = await InvokePost(ctx, http, request);
        var dto = Assert.IsType<ParentDigestPreferencesDto>(ExtractValue(result));

        // weekly_summary flipped on; safety_alerts still at default on;
        // homework_reminders still at default off.
        Assert.Equal("opted_in",
            dto.Purposes.Single(p => p.Purpose == "weekly_summary").Status);
        Assert.Equal("opted_in",
            dto.Purposes.Single(p => p.Purpose == "safety_alerts").Status);
        Assert.Equal("opted_out",
            dto.Purposes.Single(p => p.Purpose == "homework_reminders").Status);

        // Audit event was appended to the student stream.
        var calls = ctx.Events.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == "Append")
            .ToList();
        Assert.Single(calls);
        var args = calls[0].GetArguments();
        Assert.Equal(ChildA, args[0]);
        var appended = (args[1] as object[])?.FirstOrDefault() ?? args[1];
        var ev = Assert.IsType<ParentDigestPreferencesUpdated_V1>(appended);
        Assert.Equal(ParentA, ev.ParentActorId);
        Assert.Equal(ChildA, ev.StudentSubjectId);
        Assert.Equal(InstX, ev.InstituteId);
        Assert.True(ev.PurposeStatuses.ContainsKey(DigestPurpose.WeeklySummary));
    }

    [Fact]
    public async Task Post_Twice_StoresLatest_And_EmitsTwoEvents()
    {
        var ctx = NewFixture();
        var http = MakeHttp(ctx, MakeParent(ParentA, InstX, (ChildA, InstX)));

        await InvokePost(ctx, http, new SetParentDigestPreferencesRequest(
            StudentAnonId: ChildA,
            Purposes: new Dictionary<string, string> { ["weekly_summary"] = "opted_in" }));
        await InvokePost(ctx, http, new SetParentDigestPreferencesRequest(
            StudentAnonId: ChildA,
            Purposes: new Dictionary<string, string> { ["weekly_summary"] = "opted_out" }));

        var latest = await ctx.PreferencesStore.FindAsync(ParentA, ChildA, InstX);
        Assert.NotNull(latest);
        Assert.False(latest!.ShouldSend(DigestPurpose.WeeklySummary));

        var appends = ctx.Events.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == "Append");
        Assert.Equal(2, appends);
    }

    // ── (c) Cross-tenant PARENT blocked ────────────────────────────────

    [Fact]
    public async Task Post_ParentB_TargetingChildOfParentA_Returns403()
    {
        var ctx = NewFixture();
        var http = MakeHttp(ctx, MakeParent(ParentB, InstX, (ChildB, InstX)));

        var request = new SetParentDigestPreferencesRequest(
            StudentAnonId: ChildA,
            Purposes: new Dictionary<string, string> { ["weekly_summary"] = "opted_in" });

        var result = await InvokePost(ctx, http, request);
        AssertIsForbid(result);

        // No event was appended.
        Assert.Empty(ctx.Events.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == "Append"));
    }

    [Fact]
    public async Task Post_CrossTenant_Blocked()
    {
        // Parent A signed into institute Y attempting to write preferences
        // for their institute-X child — guard says no.
        var ctx = NewFixture();
        var http = MakeHttp(ctx, MakeParent(ParentA, InstY, (ChildA, InstY)));

        var request = new SetParentDigestPreferencesRequest(
            StudentAnonId: ChildA,
            Purposes: new Dictionary<string, string> { ["weekly_summary"] = "opted_in" });

        var result = await InvokePost(ctx, http, request);
        AssertIsForbid(result);
    }

    // ── (d) Bad input ───────────────────────────────────────────────────

    [Fact]
    public async Task Post_UnknownPurpose_Returns400()
    {
        var ctx = NewFixture();
        var http = MakeHttp(ctx, MakeParent(ParentA, InstX, (ChildA, InstX)));

        var request = new SetParentDigestPreferencesRequest(
            StudentAnonId: ChildA,
            Purposes: new Dictionary<string, string> { ["grade_reports"] = "opted_in" });

        var result = await InvokePost(ctx, http, request);
        AssertIsBadRequest(result);
        Assert.Empty(ctx.Events.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == "Append"));
    }

    [Fact]
    public async Task Post_UnknownStatus_Returns400()
    {
        var ctx = NewFixture();
        var http = MakeHttp(ctx, MakeParent(ParentA, InstX, (ChildA, InstX)));

        var request = new SetParentDigestPreferencesRequest(
            StudentAnonId: ChildA,
            Purposes: new Dictionary<string, string> { ["weekly_summary"] = "maybe" });

        var result = await InvokePost(ctx, http, request);
        AssertIsBadRequest(result);
    }

    [Fact]
    public async Task Post_MissingStudentAnonId_Returns400()
    {
        var ctx = NewFixture();
        var http = MakeHttp(ctx, MakeParent(ParentA, InstX, (ChildA, InstX)));

        var request = new SetParentDigestPreferencesRequest(
            StudentAnonId: "",
            Purposes: new Dictionary<string, string> { ["weekly_summary"] = "opted_in" });

        var result = await InvokePost(ctx, http, request);
        AssertIsBadRequest(result);
    }

    // ── Non-parent callers ──────────────────────────────────────────────

    [Fact]
    public async Task Get_StudentCaller_Returns403()
    {
        var ctx = NewFixture();
        var student = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("sub", "s1"),
            new Claim(ClaimTypes.Role, "STUDENT"),
            new Claim("institute_id", InstX),
        }, "test"));
        var http = MakeHttp(ctx, student);

        var result = await InvokeGet(ctx, http, ChildA);
        AssertIsForbid(result);
    }

    // ── Invocation helpers ──────────────────────────────────────────────

    private static async Task<IResult> InvokeGet(
        TestContext ctx, HttpContext http, string studentAnonId)
    {
        // Reflect the private static handler — avoids needing a WebApplication
        // host just to exercise the logic. This is the same pattern
        // MeEndpointsCqrsRaceTests uses.
        var handler = typeof(ParentDigestPreferencesEndpoints)
            .GetMethod("HandleGetAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var logger = NullLoggerFactory.Instance.CreateLogger<
            ParentDigestPreferencesEndpoints.ParentDigestPreferencesEndpointMarker>();
        var task = (Task<IResult>)handler.Invoke(null, new object[]
        {
            studentAnonId,
            http,
            ctx.PreferencesStore,
            logger,
            CancellationToken.None,
        })!;
        return await task.ConfigureAwait(false);
    }

    private static async Task<IResult> InvokePost(
        TestContext ctx, HttpContext http, SetParentDigestPreferencesRequest request)
    {
        var handler = typeof(ParentDigestPreferencesEndpoints)
            .GetMethod("HandleSetAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var logger = NullLoggerFactory.Instance.CreateLogger<
            ParentDigestPreferencesEndpoints.ParentDigestPreferencesEndpointMarker>();
        var task = (Task<IResult>)handler.Invoke(null, new object[]
        {
            request,
            http,
            ctx.PreferencesStore,
            ctx.DocumentStore,
            logger,
            CancellationToken.None,
        })!;
        return await task.ConfigureAwait(false);
    }

    private static object? ExtractValue(IResult result)
    {
        // Minimal-API OkObjectResult equivalent exposes `Value` on a property.
        var valueProp = result.GetType().GetProperty("Value");
        return valueProp?.GetValue(result);
    }

    private static void AssertIsBadRequest(IResult result)
    {
        var statusProp = result.GetType().GetProperty("StatusCode");
        var status = (int?)statusProp?.GetValue(result);
        Assert.True(status == StatusCodes.Status400BadRequest,
            $"Expected 400, got {status}");
    }

    private static void AssertIsForbid(IResult result)
    {
        // Results.Forbid() returns a type whose name includes "ForbidHttpResult".
        var typeName = result.GetType().Name;
        Assert.Contains("Forbid", typeName, StringComparison.Ordinal);
    }
}
