// =============================================================================
// Cena Platform — Authenticated subscription management endpoints
// (EPIC-PRR-I PRR-292/293/306/310, ADR-0057)
//
// Parent-scoped endpoints for managing their own subscription:
//   POST   /api/me/subscription/activate         — activate after checkout
//   GET    /api/me/subscription                  — current status
//   POST   /api/me/subscription/siblings         — link a sibling
//   DELETE /api/me/subscription/siblings/{id}    — unlink a sibling
//   PATCH  /api/me/subscription/tier             — change tier
//   PATCH  /api/me/subscription/cycle            — change billing cycle
//   POST   /api/me/subscription/refund           — request refund (30-day window)
//   GET    /api/me/subscription/guarantee-window — 30-day money-back window status (PRR-294)
//   POST   /api/me/subscription/cancel           — terminal cancel
//
// The caller MUST be authenticated; the parent's subject id comes from the
// session claim. All write endpoints pass through the payment gateway and
// the SubscriptionCommands validator — no direct event append.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Cena.Api.Contracts.Subscriptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Cena.Student.Api.Host.Endpoints;

/// <summary>Authenticated subscription-management endpoints under /api/me/subscription.</summary>
public static class SubscriptionManagementEndpoints
{
    /// <summary>Register the /api/me/subscription group.</summary>
    public static IEndpointRouteBuilder MapSubscriptionManagementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me/subscription")
            .WithTags("Subscriptions")
            .RequireAuthorization();

        group.MapGet("", GetStatus).WithName("GetSubscriptionStatus");
        group.MapPost("checkout-session", CreateCheckoutSession).WithName("CreateCheckoutSession");
        group.MapPost("activate", Activate).WithName("ActivateSubscription");
        group.MapPost("siblings", LinkSibling).WithName("LinkSibling");
        group.MapDelete("siblings/{siblingId}", UnlinkSibling).WithName("UnlinkSibling");
        group.MapPatch("tier", ChangeTier).WithName("ChangeSubscriptionTier");
        group.MapPatch("cycle", ChangeCycle).WithName("ChangeBillingCycle");
        group.MapPost("refund", RequestRefund).WithName("RequestRefund");
        group.MapGet("guarantee-window", GetGuaranteeWindow).WithName("GetGuaranteeWindow");
        group.MapPost("cancel", Cancel).WithName("CancelSubscription");

        return app;
    }

    // ----- GET status -----

    private static async Task<IResult> GetStatus(
        HttpContext http,
        [FromServices] ISubscriptionAggregateStore store,
        CancellationToken ct)
    {
        var parentId = RequireParentId(http);
        var aggregate = await store.LoadAsync(parentId, ct);
        return Results.Ok(ToStatusDto(aggregate.State));
    }

    // ----- GET guarantee-window (PRR-294 30-day money-back CTA visibility) -----

    private static async Task<IResult> GetGuaranteeWindow(
        HttpContext http,
        [FromServices] ISubscriptionAggregateStore store,
        [FromServices] TimeProvider clock,
        CancellationToken ct)
    {
        var parentId = RequireParentId(http);
        var aggregate = await store.LoadAsync(parentId, ct);
        var now = clock.GetUtcNow();
        var status = MoneyBackGuaranteeWindow.Evaluate(aggregate.State, now);
        return Results.Ok(new GuaranteeWindowStatusDto(
            IsWithinWindow: status.IsWithinWindow,
            DaysRemaining: status.DaysRemaining,
            WindowEndsAtUtc: status.WindowEndsAtUtc,
            Reason: status.Reason));
    }

    // ----- POST checkout-session (Stripe-style hosted checkout) -----

    private static async Task<IResult> CreateCheckoutSession(
        HttpContext http,
        [FromBody] CheckoutSessionRequestDto body,
        [FromServices] ICheckoutSessionProvider provider,
        CancellationToken ct)
    {
        var parentId = RequireParentId(http);
        if (!Enum.TryParse<SubscriptionTier>(body.Tier, ignoreCase: true, out var tier) ||
            !TierCatalog.Get(tier).IsRetail)
        {
            return Results.BadRequest(new { error = "invalid_tier" });
        }
        if (!Enum.TryParse<BillingCycle>(body.BillingCycle, ignoreCase: true, out var cycle) ||
            cycle == BillingCycle.None)
        {
            return Results.BadRequest(new { error = "invalid_cycle" });
        }
        if (string.IsNullOrWhiteSpace(body.IdempotencyKey))
        {
            return Results.BadRequest(new { error = "idempotency_key_required" });
        }

        // Success/cancel URLs come from provider options (Stripe) or sandbox
        // defaults. The caller doesn't pass these — the gateway owns them.
        var req = new Cena.Actors.Subscriptions.CheckoutSessionRequest(
            ParentSubjectIdEncrypted: parentId,
            PrimaryStudentSubjectIdEncrypted: body.PrimaryStudentId,
            Tier: tier,
            Cycle: cycle,
            IdempotencyKey: body.IdempotencyKey,
            SuccessUrl: "https://cena.test/subscription/confirm",
            CancelUrl: "https://cena.test/pricing");

        var result = await provider.CreateSessionAsync(req, ct);
        return Results.Ok(new CheckoutSessionResponseDto(
            CheckoutUrl: result.CheckoutUrl,
            SessionId: result.SessionId,
            ProviderName: provider.Name));
    }

    // ----- POST activate -----

    private static async Task<IResult> Activate(
        HttpContext http,
        [FromBody] ActivateSubscriptionRequest body,
        [FromServices] ISubscriptionAggregateStore store,
        [FromServices] IPaymentGateway gateway,
        [FromServices] TimeProvider clock,
        CancellationToken ct)
    {
        var parentId = RequireParentId(http);
        if (!Enum.TryParse<SubscriptionTier>(body.Tier, ignoreCase: true, out var tier) ||
            !TierCatalog.Get(tier).IsRetail)
        {
            return Results.BadRequest(new { error = "invalid_tier", details = "tier must be Basic/Plus/Premium" });
        }
        if (!Enum.TryParse<BillingCycle>(body.BillingCycle, ignoreCase: true, out var cycle) ||
            cycle == BillingCycle.None)
        {
            return Results.BadRequest(new { error = "invalid_cycle" });
        }

        var aggregate = await store.LoadAsync(parentId, ct);
        if (aggregate.State.Status != SubscriptionStatus.Unsubscribed)
        {
            return Results.Conflict(new { error = "already_active" });
        }

        // Payment first: charge the gateway. Activation event only on success.
        var definition = TierCatalog.Get(tier);
        var grossAmount = cycle == BillingCycle.Annual ? definition.AnnualPrice : definition.MonthlyPrice;
        var intent = new PaymentIntent(
            ParentSubjectIdEncrypted: parentId,
            GrossAmount: grossAmount,
            Kind: PaymentIntentKind.Activation,
            IdempotencyKey: body.PaymentIdempotencyKey);
        var result = await gateway.AuthorizeAsync(intent, ct);
        if (!result.Succeeded)
        {
            return Results.BadRequest(new { error = "payment_failed", reason = result.FailureReason });
        }

        var now = clock.GetUtcNow();
        try
        {
            var evt = SubscriptionCommands.Activate(
                aggregate.State, parentId, body.PrimaryStudentId, tier, cycle,
                paymentTransactionIdEncrypted: result.TransactionId!, activatedAt: now);
            await store.AppendAsync(parentId, evt, ct);
            aggregate.Apply(evt);
        }
        catch (SubscriptionCommandException ex)
        {
            return Results.BadRequest(new { error = "command_rejected", details = ex.Message });
        }
        return Results.Ok(ToStatusDto(aggregate.State));
    }

    // ----- POST siblings -----

    private static async Task<IResult> LinkSibling(
        HttpContext http,
        [FromBody] LinkSiblingRequest body,
        [FromServices] ISubscriptionAggregateStore store,
        [FromServices] TimeProvider clock,
        CancellationToken ct)
    {
        var parentId = RequireParentId(http);
        if (!Enum.TryParse<SubscriptionTier>(body.Tier, ignoreCase: true, out var tier))
        {
            return Results.BadRequest(new { error = "invalid_tier" });
        }

        var aggregate = await store.LoadAsync(parentId, ct);
        var now = clock.GetUtcNow();
        try
        {
            var evt = SubscriptionCommands.LinkSibling(aggregate.State, body.SiblingStudentId, tier, now);
            await store.AppendAsync(parentId, evt, ct);
            aggregate.Apply(evt);
            return Results.Ok(ToStatusDto(aggregate.State));
        }
        catch (SubscriptionCommandException ex)
        {
            return Results.BadRequest(new { error = "command_rejected", details = ex.Message });
        }
    }

    // ----- DELETE siblings/{id} -----

    private static async Task<IResult> UnlinkSibling(
        HttpContext http,
        string siblingId,
        [FromServices] ISubscriptionAggregateStore store,
        [FromServices] TimeProvider clock,
        CancellationToken ct)
    {
        var parentId = RequireParentId(http);
        var aggregate = await store.LoadAsync(parentId, ct);
        if (!aggregate.State.LinkedStudents.Any(s => s.StudentSubjectIdEncrypted == siblingId))
        {
            return Results.NotFound(new { error = "sibling_not_linked" });
        }
        var now = clock.GetUtcNow();
        try
        {
            // PRR-293: SubscriptionCommands.UnlinkSibling computes the
            // pro-rata credit from the sibling's ordinal + the parent's
            // current cycle. The command rejects unlink of ordinal 0
            // (primary) + terminal-state subscriptions.
            var evt = SubscriptionCommands.UnlinkSibling(aggregate.State, siblingId, now);
            await store.AppendAsync(parentId, evt, ct);
            aggregate.Apply(evt);
            return Results.Ok(ToStatusDto(aggregate.State));
        }
        catch (SubscriptionCommandException ex)
        {
            return Results.BadRequest(new { error = "command_rejected", details = ex.Message });
        }
    }

    // ----- PATCH tier -----

    private static async Task<IResult> ChangeTier(
        HttpContext http,
        [FromBody] ChangeTierRequest body,
        [FromServices] ISubscriptionAggregateStore store,
        [FromServices] TimeProvider clock,
        CancellationToken ct)
    {
        var parentId = RequireParentId(http);
        if (!Enum.TryParse<SubscriptionTier>(body.NewTier, ignoreCase: true, out var tier))
        {
            return Results.BadRequest(new { error = "invalid_tier" });
        }
        var aggregate = await store.LoadAsync(parentId, ct);
        try
        {
            var evt = SubscriptionCommands.ChangeTier(aggregate.State, tier, clock.GetUtcNow());
            await store.AppendAsync(parentId, evt, ct);
            aggregate.Apply(evt);
            return Results.Ok(ToStatusDto(aggregate.State));
        }
        catch (SubscriptionCommandException ex)
        {
            return Results.BadRequest(new { error = "command_rejected", details = ex.Message });
        }
    }

    // ----- PATCH cycle -----

    private static async Task<IResult> ChangeCycle(
        HttpContext http,
        [FromBody] ChangeCycleRequest body,
        [FromServices] ISubscriptionAggregateStore store,
        [FromServices] TimeProvider clock,
        CancellationToken ct)
    {
        var parentId = RequireParentId(http);
        if (!Enum.TryParse<BillingCycle>(body.NewCycle, ignoreCase: true, out var cycle) ||
            cycle == BillingCycle.None)
        {
            return Results.BadRequest(new { error = "invalid_cycle" });
        }
        var aggregate = await store.LoadAsync(parentId, ct);
        if (aggregate.State.Status != SubscriptionStatus.Active)
        {
            return Results.Conflict(new { error = "not_active" });
        }
        if (aggregate.State.CurrentCycle == cycle)
        {
            return Results.BadRequest(new { error = "no_change" });
        }
        var now = clock.GetUtcNow();
        var evt = new BillingCycleChanged_V1(
            ParentSubjectIdEncrypted: parentId,
            FromCycle: aggregate.State.CurrentCycle,
            ToCycle: cycle,
            ChangedAt: now,
            EffectiveAt: aggregate.State.RenewsAt ?? now);
        await store.AppendAsync(parentId, evt, ct);
        aggregate.Apply(evt);
        return Results.Ok(ToStatusDto(aggregate.State));
    }

    // ----- POST refund -----

    private static async Task<IResult> RequestRefund(
        HttpContext http,
        [FromBody] RefundRequest body,
        [FromServices] ISubscriptionAggregateStore store,
        [FromServices] RefundService refundService,
        CancellationToken ct)
    {
        // PRR-306: full self-service refund workflow.
        // The endpoint is a thin HTTP adapter — all orchestration
        // (policy, gateway refund, event emit, email) lives in
        // RefundService so the same path is reused from the cancellation
        // worker when an auto-refund is in scope.
        var parentId = RequireParentId(http);
        var outcome = await refundService
            .RequestRefundAsync(parentId, body.Reason ?? "requested_by_customer", ct);

        if (!outcome.Succeeded)
        {
            // Surface stable machine-readable codes. UI maps these to
            // honest, localized copy — no generic "request denied".
            var errorCode = outcome.DenialReason ?? outcome.GatewayFailureReason
                ?? "refund_failed";
            return Results.BadRequest(new
            {
                error = errorCode,
                refund_amount_agorot = outcome.RefundAmountAgorot,
            });
        }

        // Load the post-refund state and return the canonical status DTO.
        var aggregate = await store.LoadAsync(parentId, ct);
        return Results.Ok(ToStatusDto(aggregate.State));
    }

    // ----- POST cancel -----

    private static async Task<IResult> Cancel(
        HttpContext http,
        [FromBody] CancelRequest body,
        [FromServices] ISubscriptionAggregateStore store,
        [FromServices] IChurnReasonRepository churnRepo,
        [FromServices] TimeProvider clock,
        CancellationToken ct)
    {
        var parentId = RequireParentId(http);
        var aggregate = await store.LoadAsync(parentId, ct);
        try
        {
            var now = clock.GetUtcNow();
            var evt = SubscriptionCommands.Cancel(aggregate.State, body.Reason, "parent", now);
            await store.AppendAsync(parentId, evt, ct);
            aggregate.Apply(evt);

            // PRR-331: structured churn-reason capture. Optional on the
            // wire — absence is treated as "parent declined the survey";
            // presence must parse to a known enum value, otherwise 400
            // (so the UI cannot ship a typo that silently drops data).
            if (!string.IsNullOrWhiteSpace(body.ChurnReasonCategory))
            {
                if (!Enum.TryParse<ChurnReasonCategory>(
                    body.ChurnReasonCategory, ignoreCase: true, out var category))
                {
                    return Results.BadRequest(new
                    {
                        error = "invalid_churn_category",
                        allowed = Enum.GetNames<ChurnReasonCategory>(),
                    });
                }
                if (body.ChurnFreeText is { Length: > ChurnReasonReport.MaxFreeTextLength })
                {
                    return Results.BadRequest(new
                    {
                        error = "churn_free_text_too_long",
                        max = ChurnReasonReport.MaxFreeTextLength,
                    });
                }

                await churnRepo.RecordAsync(new ChurnReasonReport
                {
                    Id = ChurnReasonReport.BuildId(parentId, now),
                    ParentSubjectIdEncrypted = parentId,
                    Category = category,
                    FreeText = body.ChurnFreeText,
                    CollectedAt = now,
                    FollowedByRefund = null,
                }, ct);
            }

            return Results.Ok(ToStatusDto(aggregate.State));
        }
        catch (SubscriptionCommandException ex)
        {
            return Results.BadRequest(new { error = "command_rejected", details = ex.Message });
        }
    }

    // ----- helpers -----

    private static string RequireParentId(HttpContext http)
    {
        var sub = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                  ?? http.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(sub))
        {
            throw new InvalidOperationException("Parent subject id missing from session claims.");
        }
        return sub;
    }

    /// <summary>Map aggregate state to the wire DTO.</summary>
    public static SubscriptionStatusDto ToStatusDto(SubscriptionState state) => new(
        Status: state.Status.ToString(),
        CurrentTier: state.Status == SubscriptionStatus.Unsubscribed ? null : state.CurrentTier.ToString(),
        CurrentBillingCycle: state.CurrentCycle == BillingCycle.None ? null : state.CurrentCycle.ToString(),
        ActivatedAt: state.ActivatedAt,
        RenewsAt: state.RenewsAt,
        LinkedStudentCount: state.LinkedStudents.Count);
}
