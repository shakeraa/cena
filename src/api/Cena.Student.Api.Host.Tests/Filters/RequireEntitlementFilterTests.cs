// =============================================================================
// Cena Platform — RequireEntitlementFilterTests (Phase 1D-fix item 4)
//
// Locks the §5.5.1 decision matrix end-to-end through IEndpointFilter.InvokeAsync:
//
//   EffectiveStatus  | Cap | Filter outcome
//   ─────────────────|─────|───────────────────────────────────────────────
//   Active           |  -  | next() runs, returns 200
//   PastDue          |  -  | next() runs, returns 200 (grace per resolver)
//   Trialing         | <   | next() runs, returns 200 (under cap)
//   Trialing         | >=  | 402 with reason="trial_cap_reached", feature
//   Trialing         |  0  | next() runs (cap of 0 = unbounded)
//   Unsubscribed     |  -  | 402 reason="unsubscribed"
//   Expired          |  -  | 402 reason="expired"
//   Cancelled        |  -  | 402 reason="cancelled"
//   Refunded         |  -  | 402 reason="refunded"
//   no auth claim    |  -  | 401
//
// Pattern mirrors src/shared/Cena.Infrastructure.Tests/Compliance/
// ConsentEnforcementTests.cs — DefaultEndpointFilterInvocationContext +
// DefaultHttpContext + service collection. No WebApplicationFactory needed.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Cena.Api.Host.Filters;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Cena.Student.Api.Host.Tests.Filters;

public sealed class RequireEntitlementFilterTests
{
    private const string StudentId = "enc::student::filter-tests";

    private static readonly DateTimeOffset Now =
        new(2026, 4, 29, 10, 0, 0, TimeSpan.Zero);

    private static readonly TrialCapsSnapshot DefaultTrialCaps = new(
        TrialDurationDays: 14, TrialTutorTurns: 5,
        TrialPhotoDiagnostics: 2, TrialPracticeSessions: 1);

    [Fact]
    public async Task Active_status_passes_through()
    {
        var filter = new RequireEntitlementFilter(EntitlementFeature.TutorTurn);
        var (ctx, _, _) = BuildContext(view: ActiveView());
        var nextCalled = false;
        var result = await InvokeFilter(filter, ctx, _ =>
        {
            nextCalled = true;
            return new ValueTask<object?>(Results.Ok(new { ok = true }));
        });
        Assert.True(nextCalled);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task PastDue_status_passes_through()
    {
        var filter = new RequireEntitlementFilter(EntitlementFeature.TutorTurn);
        var (ctx, _, _) = BuildContext(view: PastDueView());
        var nextCalled = false;
        var result = await InvokeFilter(filter, ctx, _ =>
        {
            nextCalled = true;
            return new ValueTask<object?>(Results.Ok());
        });
        Assert.True(nextCalled);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Trialing_under_cap_passes_through()
    {
        var filter = new RequireEntitlementFilter(EntitlementFeature.TutorTurn);
        var (ctx, _, consumption) = BuildContext(view: TrialingView());
        // 4 turns used; cap is 5 → still under.
        for (var i = 0; i < 4; i++)
        {
            await consumption.IncrementAsync(
                StudentId, EntitlementFeature.TutorTurn, Now, CancellationToken.None);
        }
        var nextCalled = false;
        var result = await InvokeFilter(filter, ctx, _ =>
        {
            nextCalled = true;
            return new ValueTask<object?>(Results.Ok());
        });
        Assert.True(nextCalled);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Trialing_at_cap_returns_402_trial_cap_reached()
    {
        var filter = new RequireEntitlementFilter(EntitlementFeature.TutorTurn);
        var (ctx, _, consumption) = BuildContext(view: TrialingView());
        // 5 turns used; cap is 5 → reached.
        for (var i = 0; i < 5; i++)
        {
            await consumption.IncrementAsync(
                StudentId, EntitlementFeature.TutorTurn, Now, CancellationToken.None);
        }
        var nextCalled = false;
        var result = await InvokeFilter(filter, ctx, _ =>
        {
            nextCalled = true;
            return new ValueTask<object?>(Results.Ok());
        });
        Assert.False(nextCalled);
        await AssertResultAsync(ctx, result, expectedStatus: 402, expectedReason: "trial_cap_reached",
            expectedFeature: "tutor_turn");
        Assert.Equal("true", ctx.Response.Headers["X-Entitlement-Required"]);
    }

    [Fact]
    public async Task Trialing_over_cap_returns_402()
    {
        var filter = new RequireEntitlementFilter(EntitlementFeature.PhotoDiagnostic);
        var (ctx, _, consumption) = BuildContext(view: TrialingView());
        // 3 photos used; cap is 2 → over.
        for (var i = 0; i < 3; i++)
        {
            await consumption.IncrementAsync(
                StudentId, EntitlementFeature.PhotoDiagnostic, Now, CancellationToken.None);
        }
        var nextCalled = false;
        var result = await InvokeFilter(filter, ctx, _ =>
        {
            nextCalled = true;
            return new ValueTask<object?>(Results.Ok());
        });
        Assert.False(nextCalled);
        await AssertResultAsync(ctx, result, expectedStatus: 402, expectedReason: "trial_cap_reached",
            expectedFeature: "photo_diagnostic");
    }

    [Fact]
    public async Task Trialing_with_zero_cap_passes_through()
    {
        // cap = 0 means unbounded. Filter must NOT gate on this dimension
        // even when consumption is high.
        var unbounded = new TrialCapsSnapshot(
            TrialDurationDays: 14, TrialTutorTurns: 0,
            TrialPhotoDiagnostics: 0, TrialPracticeSessions: 0);
        var view = TrialingView() with { TrialCaps = unbounded };

        var filter = new RequireEntitlementFilter(EntitlementFeature.TutorTurn);
        var (ctx, _, consumption) = BuildContext(view: view);
        for (var i = 0; i < 1000; i++)
        {
            await consumption.IncrementAsync(
                StudentId, EntitlementFeature.TutorTurn, Now, CancellationToken.None);
        }
        var nextCalled = false;
        var result = await InvokeFilter(filter, ctx, _ =>
        {
            nextCalled = true;
            return new ValueTask<object?>(Results.Ok());
        });
        Assert.True(nextCalled);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task Trialing_Generic_feature_never_gates_on_cap()
    {
        // Generic is a pure entitlement check (no per-feature cap).
        var filter = new RequireEntitlementFilter(EntitlementFeature.Generic);
        var (ctx, _, consumption) = BuildContext(view: TrialingView());
        // Burn through every cap dimension.
        for (var i = 0; i < 10; i++)
        {
            await consumption.IncrementAsync(
                StudentId, EntitlementFeature.TutorTurn, Now, CancellationToken.None);
        }
        var nextCalled = false;
        await InvokeFilter(filter, ctx, _ =>
        {
            nextCalled = true;
            return new ValueTask<object?>(Results.Ok());
        });
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Unsubscribed_returns_402_with_reason_unsubscribed()
    {
        var filter = new RequireEntitlementFilter(EntitlementFeature.TutorTurn);
        var (ctx, _, _) = BuildContext(view: UnsubscribedView());
        var result = await InvokeFilter(filter, ctx, _ => new ValueTask<object?>(Results.Ok()));
        await AssertResultAsync(ctx, result, expectedStatus: 402, expectedReason: "unsubscribed",
            expectedFeature: null);
    }

    [Fact]
    public async Task Expired_returns_402_with_reason_expired()
    {
        var filter = new RequireEntitlementFilter(EntitlementFeature.TutorTurn);
        var (ctx, _, _) = BuildContext(view: SimpleStatusView(SubscriptionStatus.Expired));
        var result = await InvokeFilter(filter, ctx, _ => new ValueTask<object?>(Results.Ok()));
        await AssertResultAsync(ctx, result, expectedStatus: 402, expectedReason: "expired",
            expectedFeature: null);
    }

    [Fact]
    public async Task Cancelled_returns_402_with_reason_cancelled()
    {
        var filter = new RequireEntitlementFilter(EntitlementFeature.TutorTurn);
        var (ctx, _, _) = BuildContext(view: SimpleStatusView(SubscriptionStatus.Cancelled));
        var result = await InvokeFilter(filter, ctx, _ => new ValueTask<object?>(Results.Ok()));
        await AssertResultAsync(ctx, result, expectedStatus: 402, expectedReason: "cancelled",
            expectedFeature: null);
    }

    [Fact]
    public async Task Refunded_returns_402_with_reason_refunded()
    {
        var filter = new RequireEntitlementFilter(EntitlementFeature.TutorTurn);
        var (ctx, _, _) = BuildContext(view: SimpleStatusView(SubscriptionStatus.Refunded));
        var result = await InvokeFilter(filter, ctx, _ => new ValueTask<object?>(Results.Ok()));
        await AssertResultAsync(ctx, result, expectedStatus: 402, expectedReason: "refunded",
            expectedFeature: null);
    }

    [Fact]
    public async Task Missing_subject_claim_returns_401()
    {
        var filter = new RequireEntitlementFilter(EntitlementFeature.TutorTurn);
        var (ctx, _, _) = BuildContext(view: ActiveView(), withSubject: false);
        var result = await InvokeFilter(filter, ctx, _ => new ValueTask<object?>(Results.Ok()));
        Assert.NotNull(result);
        // Results.Unauthorized() returns Microsoft.AspNetCore.Http.HttpResults.UnauthorizedHttpResult
        // — assert by type name to avoid the framework-execute path.
        Assert.Equal("UnauthorizedHttpResult", result!.GetType().Name);
    }

    // ----- direct unit tests for the static cap-decision helper ----------

    [Fact]
    public async Task IsTrialCapReachedAsync_unit_5_of_5_returns_true()
    {
        var consumption = new InMemoryStudentTrialConsumptionStore();
        for (var i = 0; i < 5; i++)
        {
            await consumption.IncrementAsync(
                StudentId, EntitlementFeature.TutorTurn, Now, CancellationToken.None);
        }
        var view = TrialingView();
        var reached = await RequireEntitlementFilter.IsTrialCapReachedAsync(
            view, consumption, EntitlementFeature.TutorTurn, CancellationToken.None);
        Assert.True(reached);
    }

    [Fact]
    public async Task IsTrialCapReachedAsync_unit_4_of_5_returns_false()
    {
        var consumption = new InMemoryStudentTrialConsumptionStore();
        for (var i = 0; i < 4; i++)
        {
            await consumption.IncrementAsync(
                StudentId, EntitlementFeature.TutorTurn, Now, CancellationToken.None);
        }
        var view = TrialingView();
        var reached = await RequireEntitlementFilter.IsTrialCapReachedAsync(
            view, consumption, EntitlementFeature.TutorTurn, CancellationToken.None);
        Assert.False(reached);
    }

    [Fact]
    public async Task IsTrialCapReachedAsync_unit_null_caps_returns_false()
    {
        // Defensive: a view that somehow lost its TrialCaps must not falsely
        // gate. The resolver always populates TrialCaps for Trialing views,
        // but the filter must remain robust.
        var consumption = new InMemoryStudentTrialConsumptionStore();
        var view = TrialingView() with { TrialCaps = null };
        var reached = await RequireEntitlementFilter.IsTrialCapReachedAsync(
            view, consumption, EntitlementFeature.TutorTurn, CancellationToken.None);
        Assert.False(reached);
    }

    // ----- helpers -------------------------------------------------------

    private static StudentEntitlementView ActiveView() => new(
        StudentSubjectIdEncrypted: StudentId,
        EffectiveTier: SubscriptionTier.Plus,
        SourceParentSubjectIdEncrypted: "enc::parent::active",
        ValidUntil: Now.AddDays(30),
        LastUpdatedAt: Now,
        EffectiveStatus: SubscriptionStatus.Active);

    private static StudentEntitlementView PastDueView() => new(
        StudentSubjectIdEncrypted: StudentId,
        EffectiveTier: SubscriptionTier.Plus,
        SourceParentSubjectIdEncrypted: "enc::parent::pastdue",
        ValidUntil: Now.AddDays(-3),
        LastUpdatedAt: Now,
        EffectiveStatus: SubscriptionStatus.PastDue);

    private static StudentEntitlementView TrialingView() => new(
        StudentSubjectIdEncrypted: StudentId,
        EffectiveTier: SubscriptionTier.TrialPlus,
        SourceParentSubjectIdEncrypted: "enc::parent::trialing",
        ValidUntil: Now.AddDays(14),
        LastUpdatedAt: Now,
        EffectiveStatus: SubscriptionStatus.Trialing,
        TrialCaps: DefaultTrialCaps);

    private static StudentEntitlementView UnsubscribedView() => new(
        StudentSubjectIdEncrypted: StudentId,
        EffectiveTier: SubscriptionTier.Unsubscribed,
        SourceParentSubjectIdEncrypted: string.Empty,
        ValidUntil: null,
        LastUpdatedAt: Now,
        EffectiveStatus: SubscriptionStatus.Unsubscribed);

    private static StudentEntitlementView SimpleStatusView(SubscriptionStatus status) => new(
        StudentSubjectIdEncrypted: StudentId,
        EffectiveTier: SubscriptionTier.Unsubscribed,
        SourceParentSubjectIdEncrypted: string.Empty,
        ValidUntil: null,
        LastUpdatedAt: Now,
        EffectiveStatus: status);

    private static (HttpContext ctx, FakeResolver resolver, InMemoryStudentTrialConsumptionStore consumption)
        BuildContext(StudentEntitlementView view, bool withSubject = true)
    {
        var consumption = new InMemoryStudentTrialConsumptionStore();
        var resolver = new FakeResolver(view);
        var services = new ServiceCollection();
        services.AddSingleton<IStudentEntitlementResolver>(resolver);
        services.AddSingleton<IStudentTrialConsumptionStore>(consumption);
        services.AddSingleton(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (withSubject)
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("sub", StudentId),
            }, "TestAuth"));
        }
        else
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity());
        }
        return (ctx, resolver, consumption);
    }

    private static async ValueTask<object?> InvokeFilter(
        IEndpointFilter filter, HttpContext ctx, EndpointFilterDelegate next)
    {
        var fctx = new DefaultEndpointFilterInvocationContext(ctx);
        return await filter.InvokeAsync(fctx, next);
    }

    /// <summary>
    /// Inspect the IResult's serialised body via JSON round-trip rather than
    /// executing it through the framework pipeline (which would require the
    /// full hosting stack). For Results.Json the result type is
    /// <c>JsonHttpResult&lt;object&gt;</c> with the value accessible via
    /// reflection on the public Value property.
    /// </summary>
    private static Task AssertResultAsync(
        HttpContext ctx, object? result,
        int expectedStatus, string expectedReason, string? expectedFeature)
    {
        Assert.NotNull(result);
        // JsonHttpResult<T> exposes StatusCode and Value publicly.
        var t = result!.GetType();
        var statusCodeProp = t.GetProperty("StatusCode");
        var valueProp = t.GetProperty("Value");
        Assert.NotNull(statusCodeProp);
        Assert.NotNull(valueProp);
        var actualStatus = (int?)statusCodeProp!.GetValue(result);
        Assert.Equal(expectedStatus, actualStatus);
        var value = valueProp!.GetValue(result);
        Assert.NotNull(value);
        var json = System.Text.Json.JsonSerializer.Serialize(value);
        Assert.Contains($"\"Reason\":\"{expectedReason}\"", json,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Error\":\"entitlement_required\"", json,
            StringComparison.OrdinalIgnoreCase);
        if (expectedFeature is null)
        {
            Assert.Contains("\"Feature\":null", json, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains($"\"Feature\":\"{expectedFeature}\"", json,
                StringComparison.OrdinalIgnoreCase);
        }
        return Task.CompletedTask;
    }

    private sealed class FakeResolver : IStudentEntitlementResolver
    {
        private readonly StudentEntitlementView _view;
        public FakeResolver(StudentEntitlementView view) { _view = view; }
        public Task<StudentEntitlementView> ResolveAsync(
            string studentSubjectIdEncrypted, CancellationToken ct) =>
            Task.FromResult(_view);
    }

}
