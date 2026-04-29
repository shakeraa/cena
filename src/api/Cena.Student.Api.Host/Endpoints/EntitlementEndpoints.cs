// =============================================================================
// Cena Platform — EntitlementEndpoints (Phase 1D, trial-then-paywall §11)
//
// Three consumer-facing endpoints:
//
//   GET  /api/me/entitlement     — entitlement read surface
//   POST /api/me/start-trial     — trial creation (SelfPay/ParentPay/InstituteCode)
//   POST /api/me/redeem-code     — peek for an applicable discount on the caller's email
//
// Auth: all three require an authenticated student token.
//
// Wire format: see Cena.Api.Contracts/Subscriptions/EntitlementDtos.cs.
//
// Failure response convention: structured JSON with `error` + `reason` +
// optional `field`. 4xx codes:
//   400 invalid payload (field-level reason)
//   401 unauthenticated   (framework before filter)
//   404 caller has no parent binding (start-trial only)
//   409 trial_already_used (fingerprint or email collision)
//   410 trial_not_offered  (TrialAllotmentConfig.TrialEnabled = false)
//   422 setupintent_unverified (Stripe verify returned non-Succeeded)
//
// Idempotency: start-trial is naturally idempotent because StartTrial command
// requires Status = Unsubscribed. A retry from the same caller after success
// gets a structured 409 ("trial_already_started_on_stream").
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Parent;
using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Cena.Api.Contracts.Subscriptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Api.Host.Endpoints;

/// <summary>
/// Wires the three /api/me/* endpoints that constitute the consumer-side
/// entitlement surface for Phase 1D.
/// </summary>
public static class EntitlementEndpoints
{
    public const string EntitlementRoute = "/api/me/entitlement";
    public const string StartTrialRoute = "/api/me/start-trial";
    public const string RedeemCodeRoute = "/api/me/redeem-code";

    /// <summary>Register the three endpoints on the host's route builder.</summary>
    public static IEndpointRouteBuilder MapEntitlementEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet(EntitlementRoute, GetEntitlementAsync)
            .WithName("GetEntitlement")
            .WithTags("Me", "Subscriptions", "Trial")
            .RequireAuthorization()
            .Produces<EntitlementResponseDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        app.MapPost(StartTrialRoute, StartTrialAsync)
            .WithName("StartTrial")
            .WithTags("Me", "Subscriptions", "Trial")
            .RequireAuthorization()
            .Produces<EntitlementResponseDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status410Gone)
            .Produces(StatusCodes.Status422UnprocessableEntity);

        app.MapPost(RedeemCodeRoute, RedeemCodeAsync)
            .WithName("RedeemCode")
            .WithTags("Me", "Subscriptions", "Discounts")
            .RequireAuthorization()
            .Produces<RedeemCodeResponseDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    // ----- GET /api/me/entitlement --------------------------------------

    private static async Task<IResult> GetEntitlementAsync(
        HttpContext ctx,
        IStudentEntitlementResolver resolver,
        IStudentTrialConsumptionStore consumption,
        ISubscriptionAggregateStore subscriptions,
        DiscountAssignmentService discounts,
        CancellationToken ct)
    {
        var subjectId = ExtractSubjectId(ctx);
        if (string.IsNullOrWhiteSpace(subjectId)) return Results.Unauthorized();

        var view = await resolver.ResolveAsync(subjectId, ct).ConfigureAwait(false);

        TrialStateDto? trialDto = null;
        SubscriptionStateDto? subDto = null;

        if (view.EffectiveStatus == SubscriptionStatus.Trialing
            && !string.IsNullOrEmpty(view.SourceParentSubjectIdEncrypted))
        {
            // Re-load the parent aggregate to read the pinned caps snapshot.
            // Pilot scale: one extra round-trip is cheap. Phase 2 read
            // optimization may push the caps into the view directly.
            var parentId = view.SourceParentSubjectIdEncrypted;
            if (parentId.StartsWith(StudentEntitlementResolver.AlphaGraceSourcePrefix,
                    StringComparison.Ordinal))
            {
                // Grace path — no trial caps; surface as Active in the view.
            }
            else
            {
                var aggregate = await subscriptions.LoadAsync(parentId, ct).ConfigureAwait(false);
                var caps = aggregate.State.TrialCaps;
                var snapshot = await consumption.GetAsync(subjectId, ct).ConfigureAwait(false);
                trialDto = BuildTrialStateDto(view, caps, snapshot);
            }
        }
        else if (view.EffectiveStatus is SubscriptionStatus.Active
                 or SubscriptionStatus.PastDue)
        {
            var parentId = view.SourceParentSubjectIdEncrypted;
            if (!string.IsNullOrEmpty(parentId)
                && !parentId.StartsWith(StudentEntitlementResolver.AlphaGraceSourcePrefix,
                    StringComparison.Ordinal))
            {
                var aggregate = await subscriptions.LoadAsync(parentId, ct).ConfigureAwait(false);
                subDto = new SubscriptionStateDto(
                    RenewsAt: aggregate.State.RenewsAt,
                    BillingCycle: aggregate.State.CurrentCycle.ToString());
            }
        }

        ApplicableDiscountDto? discountDto = null;
        var email = ExtractEmail(ctx);
        if (!string.IsNullOrEmpty(email))
        {
            var summary = await discounts.FindActiveForEmailAsync(email, ct).ConfigureAwait(false);
            if (summary is not null)
            {
                discountDto = new ApplicableDiscountDto(
                    AssignmentId: summary.AssignmentId,
                    DiscountKind: summary.Kind.ToString(),
                    DiscountValue: summary.Value,
                    DurationMonths: summary.DurationMonths,
                    Status: summary.Status.ToString());
            }
        }

        var dto = new EntitlementResponseDto(
            Tier: view.EffectiveTier.ToString(),
            EffectiveStatus: view.EffectiveStatus.ToString(),
            Trial: trialDto,
            Subscription: subDto,
            DiscountApplied: discountDto);
        return Results.Ok(dto);
    }

    private static TrialStateDto BuildTrialStateDto(
        StudentEntitlementView view,
        TrialCapsSnapshot? caps,
        StudentTrialConsumption snapshot)
    {
        int? daysRemaining = null;
        if (view.ValidUntil.HasValue)
        {
            var diff = view.ValidUntil.Value - view.LastUpdatedAt;
            // Floor at 0 so we never report a negative remaining count if
            // the worker hasn't fired yet.
            daysRemaining = (int)Math.Max(0, Math.Ceiling(diff.TotalDays));
        }
        return new TrialStateDto(
            EndsAt: view.ValidUntil,
            DaysRemaining: daysRemaining,
            TutorTurnsUsed: snapshot.TutorTurnsUsed,
            TutorTurnsCap: caps?.TrialTutorTurns ?? 0,
            PhotoDiagnosticsUsed: snapshot.PhotoDiagnosticsUsed,
            PhotoDiagnosticsCap: caps?.TrialPhotoDiagnostics ?? 0,
            SessionsStarted: snapshot.SessionsStarted,
            SessionsCap: caps?.TrialPracticeSessions ?? 0);
    }

    // ----- POST /api/me/start-trial -------------------------------------

    private static async Task<IResult> StartTrialAsync(
        HttpContext ctx,
        StartTrialRequestDto body,
        IStudentEntitlementResolver resolver,
        ISubscriptionAggregateStore subscriptions,
        IStudentTrialConsumptionStore consumption,
        ITrialAllotmentConfigStore allotment,
        ITrialFingerprintLedgerStore ledger,
        IPaymentMethodSetupProvider payment,
        IParentChildBindingStore parentBindings,
        TimeProvider clock,
        CancellationToken ct)
    {
        var subjectId = ExtractSubjectId(ctx);
        if (string.IsNullOrWhiteSpace(subjectId)) return Results.Unauthorized();
        if (body is null) return ProblemJson(400, "invalid_body", "request body is required");

        if (!Enum.TryParse<TrialKind>(body.TrialKind, ignoreCase: true, out var trialKind))
        {
            return ProblemJson(400, "invalid_trial_kind",
                "trialKind must be one of: SelfPay, ParentPay, InstituteCode",
                field: "trialKind");
        }

        // Allotment config gate. TrialEnabled = false → 410 Gone (the offer
        // is intentionally absent, not malformed).
        var config = await allotment.GetAsync(ct).ConfigureAwait(false);
        if (!config.TrialEnabled)
        {
            return ProblemJson(410, "trial_not_offered",
                "trial is not currently offered on this platform");
        }

        var existingView = await resolver.ResolveAsync(subjectId, ct).ConfigureAwait(false);
        if (existingView.EffectiveStatus is SubscriptionStatus.Active
            or SubscriptionStatus.Trialing
            or SubscriptionStatus.PastDue)
        {
            return ProblemJson(409, "already_entitled",
                $"caller already has an active entitlement (status={existingView.EffectiveStatus})");
        }

        // Find the parent binding for this caller. The student-side caller
        // must have a parent record on file before a trial can start; the
        // parent stream is keyed on parentSubjectIdEncrypted, not on the
        // student's own id (ADR-0057 §2 parent-keyed subscription).
        var parents = parentBindings is IStudentParentIndex idx
            ? await idx.ListParentsForStudentAsync(subjectId, ct).ConfigureAwait(false)
            : Array.Empty<string>();
        if (parents.Count == 0)
        {
            return ProblemJson(404, "no_parent_binding",
                "student has no parent binding on file; cannot start a trial");
        }
        var parentId = parents[0];

        // Fingerprint hash + normalised email for the L2/L3a ledger.
        string fingerprintHash;
        string? paymentMethodId = null;
        if (trialKind == TrialKind.InstituteCode)
        {
            // InstituteCode trials carry no card. The ledger still records a
            // row (keyed on a derived value) so abuse defense can spot
            // institute-code recycling within the same email; the
            // fingerprint slot uses a stable namespace prefix for the
            // institute code so it cannot collide with a real card hash.
            if (string.IsNullOrWhiteSpace(body.InstituteCode))
            {
                return ProblemJson(400, "missing_institute_code",
                    "InstituteCode trials require instituteCode in the body",
                    field: "instituteCode");
            }
            fingerprintHash = "inst:" + Hash(body.InstituteCode);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(body.SetupIntentId))
            {
                return ProblemJson(400, "missing_setup_intent",
                    $"{trialKind} trials require setupIntentId in the body",
                    field: "setupIntentId");
            }
            var verify = await payment
                .VerifyAndExtractFingerprintAsync(body.SetupIntentId, ct)
                .ConfigureAwait(false);
            if (verify.Status != SetupIntentStatus.Succeeded
                || string.IsNullOrEmpty(verify.CardFingerprint))
            {
                return ProblemJson(422, "setupintent_unverified",
                    $"setupIntent did not reach Succeeded (status={verify.Status})");
            }
            fingerprintHash = "card:" + Hash(verify.CardFingerprint);
            paymentMethodId = verify.PaymentMethodId;
        }

        var email = ExtractEmail(ctx) ?? string.Empty;
        var emailNormalized = string.IsNullOrEmpty(email)
            ? string.Empty
            : EmailNormalizer.Normalize(email);

        // Record-trial in the fingerprint ledger BEFORE appending
        // TrialStarted_V1 so a duplicate fingerprint cannot squeak past the
        // command. RecordTrialAsync throws TrialAbuseException on a
        // duplicate fingerprint or email — we map that to 409.
        try
        {
            await ledger.RecordTrialAsync(fingerprintHash, parentId, emailNormalized, ct)
                .ConfigureAwait(false);
        }
        catch (TrialAbuseException)
        {
            return ProblemJson(409, "trial_already_used",
                "a trial has already been used for this account");
        }

        // Pin the caps snapshot from the live config and append TrialStarted.
        var caps = new TrialCapsSnapshot(
            TrialDurationDays: config.TrialDurationDays,
            TrialTutorTurns: config.TrialTutorTurns,
            TrialPhotoDiagnostics: config.TrialPhotoDiagnostics,
            TrialPracticeSessions: config.TrialPracticeSessions);
        var startedAt = clock.GetUtcNow();
        var endsAt = caps.TrialDurationDays > 0
            ? startedAt.AddDays(caps.TrialDurationDays)
            : startedAt;
        var aggregate = await subscriptions.LoadAsync(parentId, ct).ConfigureAwait(false);

        TrialStarted_V1 trialEvent;
        try
        {
            trialEvent = SubscriptionCommands.StartTrial(
                currentState: aggregate.State,
                parentSubjectIdEncrypted: parentId,
                primaryStudentSubjectIdEncrypted: subjectId,
                trialKind: trialKind,
                trialStartedAt: startedAt,
                trialEndsAt: endsAt,
                fingerprintHash: trialKind == TrialKind.InstituteCode ? string.Empty : fingerprintHash,
                experimentVariantId: body.ExperimentVariantId ?? "v1-baseline",
                capsSnapshot: caps);
        }
        catch (SubscriptionCommandException ex)
        {
            return ProblemJson(409, "command_rejected", ex.Message);
        }

        await subscriptions.AppendAsync(parentId, trialEvent, ct).ConfigureAwait(false);
        await consumption.ResetAsync(subjectId, ct).ConfigureAwait(false);

        // _ paymentMethodId reserved for future write to a Stripe-payment-
        // method shadow store (Phase 1E+); silenced for now.
        _ = paymentMethodId;

        // Build the response by re-reading the entitlement view — same
        // shape as GET /api/me/entitlement so the SPA can replace state
        // without a separate fetch.
        var freshView = await resolver.ResolveAsync(subjectId, ct).ConfigureAwait(false);
        var freshSnapshot = await consumption.GetAsync(subjectId, ct).ConfigureAwait(false);
        var trialDto = BuildTrialStateDto(freshView, caps, freshSnapshot);
        var responseDto = new EntitlementResponseDto(
            Tier: freshView.EffectiveTier.ToString(),
            EffectiveStatus: freshView.EffectiveStatus.ToString(),
            Trial: trialDto,
            Subscription: null,
            DiscountApplied: null);
        return Results.Ok(responseDto);
    }

    // ----- POST /api/me/redeem-code -------------------------------------

    private static async Task<IResult> RedeemCodeAsync(
        HttpContext ctx,
        RedeemCodeRequestDto body,
        DiscountAssignmentService discounts,
        CancellationToken ct)
    {
        var email = ExtractEmail(ctx);
        if (string.IsNullOrWhiteSpace(email))
        {
            return Results.Ok(new RedeemCodeResponseDto(
                Applied: false,
                DiscountKind: null,
                DiscountValue: null,
                DurationMonths: null,
                Reason: "no_email_on_token"));
        }
        if (body is null || string.IsNullOrWhiteSpace(body.Code))
        {
            return Results.Ok(new RedeemCodeResponseDto(
                Applied: false,
                DiscountKind: null,
                DiscountValue: null,
                DurationMonths: null,
                Reason: "empty_code"));
        }

        // Phase 1D scope: the redeem-code endpoint reports availability of
        // an active discount for the caller's email. The actual binding
        // happens at Stripe checkout via the existing promotion-code flow.
        // We treat the supplied `code` as a hint — admin issues per-email,
        // so the lookup is by email, not by raw code (the code is bound to
        // the email Stripe-side).
        var summary = await discounts.FindActiveForEmailAsync(email, ct).ConfigureAwait(false);
        if (summary is null)
        {
            return Results.Ok(new RedeemCodeResponseDto(
                Applied: false,
                DiscountKind: null,
                DiscountValue: null,
                DurationMonths: null,
                Reason: "not_found"));
        }
        return Results.Ok(new RedeemCodeResponseDto(
            Applied: true,
            DiscountKind: summary.Kind.ToString(),
            DiscountValue: summary.Value,
            DurationMonths: summary.DurationMonths,
            Reason: null));
    }

    // ----- helpers ------------------------------------------------------

    private static string? ExtractSubjectId(HttpContext http) =>
        http.User.FindFirst("sub")?.Value
        ?? http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    private static string? ExtractEmail(HttpContext http) =>
        http.User.FindFirst("email")?.Value
        ?? http.User.FindFirst(ClaimTypes.Email)?.Value;

    private static string Hash(string s)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(s));
        return Convert.ToHexStringLower(bytes);
    }

    private static IResult ProblemJson(
        int statusCode, string error, string message, string? field = null)
    {
        var body = field is null
            ? (object)new { error, message }
            : new { error, message, field };
        return Results.Json(body, statusCode: statusCode);
    }
}
