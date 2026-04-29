// =============================================================================
// Cena Platform — EntitlementEndpoints (Phase 1D + 1D-fix, trial-then-paywall §11)
//
// Two consumer-facing endpoints:
//
//   GET  /api/me/entitlement     — entitlement read surface (tier + trial state
//                                  + sub state + applicable discount peek +
//                                  payment-method-on-file flag)
//   POST /api/me/start-trial     — trial creation (SelfPay/ParentPay/InstituteCode)
//
// /api/me/redeem-code was removed in Phase 1D-fix (item 3): the codebase has
// no code-driven redemption registry — admin issues per-email and the binding
// is automatic at Stripe checkout via IDiscountCouponProvider.
// PromotionCodeString is never persisted in event state, so there is no
// honest way to validate a typed code against the registry. The SPA's
// existing GET /api/me/applicable-discount path already covers email-based
// peek; a redundant POST endpoint that ignored its argument was a contract
// lie and is gone.
//
// Auth: both endpoints require an authenticated student token.
//
// Wire format: see Cena.Api.Contracts/Subscriptions/EntitlementDtos.cs.
//
// Failure response convention: structured JSON with `error` + `message` +
// optional `field`. 4xx codes:
//   400 invalid payload (field-level reason)
//   401 unauthenticated   (framework before filter)
//   404 caller has no parent binding (start-trial only)
//   409 trial_already_used (fingerprint or email collision) /
//        already_entitled (existing Active/Trialing/PastDue) /
//        command_rejected (domain command refused)
//   410 trial_not_offered  (TrialAllotmentConfig.TrialEnabled = false)
//   422 setupintent_unverified (Stripe verify returned non-Succeeded)
//
// Idempotency: start-trial is naturally idempotent because the StartTrial
// command requires Status = Unsubscribed. Re-calls from a caller already in
// Trialing get a structured 409 (`already_entitled`). The
// SubscriptionPaymentMethodAttached_V1 event de-dupes on
// SubscriptionState.LastAttachedPaymentMethodFingerprintHash so re-running
// SetupIntent verify against the same card emits no second event.
// =============================================================================

using System.Security.Claims;
using Cena.Actors.Parent;
using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Cena.Api.Contracts.Subscriptions;
using Cena.Infrastructure.Compliance;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Cena.Api.Host.Endpoints;

/// <summary>
/// Wires the two /api/me/* endpoints that constitute the consumer-side
/// entitlement surface for Phase 1D + 1D-fix.
/// </summary>
public static class EntitlementEndpoints
{
    public const string EntitlementRoute = "/api/me/entitlement";
    public const string StartTrialRoute = "/api/me/start-trial";

    /// <summary>Register the endpoints on the host's route builder.</summary>
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

        return app;
    }

    // ----- GET /api/me/entitlement --------------------------------------

    /// <summary>
    /// Internal entry point for testability. Tests inject the dependencies
    /// directly rather than spinning up a full ASP.NET pipeline.
    /// </summary>
    internal static async Task<IResult> GetEntitlementAsync(
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

        if (view.EffectiveStatus == SubscriptionStatus.Trialing)
        {
            // Phase 1D-fix item 1: caps now ride on the view (resolver pins
            // them from SubscriptionState.TrialCaps). No parent re-load.
            var snapshot = await consumption.GetAsync(subjectId, ct).ConfigureAwait(false);
            trialDto = BuildTrialStateDto(view, view.TrialCaps, snapshot);
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
            HasPaymentMethodOnFile: view.HasPaymentMethodOnFile,
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
            // Floor at 0 so we never report a negative count if the worker
            // hasn't fired yet on a calendar-elapsed trial.
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

    /// <summary>
    /// Internal entry point for testability. Composes:
    ///   1. allotment-config gate                 → 410
    ///   2. existing-entitlement guard            → 409
    ///   3. parent-binding lookup                 → 404
    ///   4. SetupIntent server-side re-read       → 422
    ///   5. fingerprint ledger duplicate-check    → 409
    ///   6. StartTrial command + Append           → 409 (command_rejected)
    ///   7. SubscriptionPaymentMethodAttached_V1  → silent on idempotent re-attach
    ///   8. consumption-counter Reset             → silent
    ///   9. fresh entitlement read for the response shape
    /// </summary>
    internal static async Task<IResult> StartTrialAsync(
        HttpContext ctx,
        StartTrialRequestDto body,
        IStudentEntitlementResolver resolver,
        ISubscriptionAggregateStore subscriptions,
        IStudentTrialConsumptionStore consumption,
        ITrialAllotmentConfigStore allotment,
        ITrialFingerprintLedgerStore ledger,
        IPaymentMethodSetupProvider payment,
        IParentChildBindingStore parentBindings,
        EncryptedFieldAccessor encryptor,
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

        // Resolve the parent stream. The student-side caller must have a
        // parent record on file before a trial can start; the parent stream
        // is keyed on parentSubjectIdEncrypted, not on the student's own id
        // (ADR-0057 §2 parent-keyed subscription).
        var parents = parentBindings is IStudentParentIndex idx
            ? await idx.ListParentsForStudentAsync(subjectId, ct).ConfigureAwait(false)
            : Array.Empty<string>();
        if (parents.Count == 0)
        {
            return ProblemJson(404, "no_parent_binding",
                "student has no parent binding on file; cannot start a trial");
        }
        var parentId = parents[0];

        // Fingerprint hash + (for card flows) Stripe payment-method id.
        string fingerprintHash;
        string? paymentMethodIdEncrypted = null;
        if (trialKind == TrialKind.InstituteCode)
        {
            // InstituteCode trials carry no card. The ledger row is keyed on
            // a derived value so abuse defense can spot institute-code
            // recycling within the same email; the fingerprint slot uses a
            // stable namespace prefix that cannot collide with a real card hash.
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
            // Phase 1D-fix-2 item 1: real AES-GCM encryption via the parent
            // subject's derived key (ADR-0038, ADR-0061 §"Encryption").
            // EncryptAsync throws InvalidOperationException when the parent's
            // key has been tombstoned by RTBF → translate to graceful 410
            // rather than letting it bubble as 500.
            try
            {
                paymentMethodIdEncrypted = await encryptor
                    .EncryptAsync(verify.PaymentMethodId ?? string.Empty, parentId, ct)
                    .ConfigureAwait(false)
                    ?? string.Empty;
            }
            catch (InvalidOperationException)
            {
                return ProblemJson(410, "parent_erased",
                    "the parent's data has been erased; a new subscription stream is required");
            }
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

        // Phase 1D-fix-2 item 3: append TrialStarted_V1 + (optional)
        // SubscriptionPaymentMethodAttached_V1 atomically. Either both land
        // or neither does — partial commit would leave a trial without the
        // captured payment method, breaking the conversion promise.
        // Idempotency on payment-method-attached: when the same card is
        // already on file (same fingerprint hash), we omit the second event
        // from the batch. We check the PRE-trial aggregate state because
        // TrialStarted_V1 doesn't touch payment-method state — checking
        // before vs after is equivalent here.
        var alreadyAttached = paymentMethodIdEncrypted is not null
            && !string.IsNullOrEmpty(aggregate.State.LastAttachedPaymentMethodFingerprintHash)
            && string.Equals(
                aggregate.State.LastAttachedPaymentMethodFingerprintHash,
                fingerprintHash,
                StringComparison.Ordinal);

        var batch = paymentMethodIdEncrypted is not null && !alreadyAttached
            ? new object[]
            {
                trialEvent,
                new SubscriptionPaymentMethodAttached_V1(
                    ParentSubjectIdEncrypted: parentId,
                    PaymentMethodIdEncrypted: paymentMethodIdEncrypted,
                    FingerprintHash: fingerprintHash,
                    AttachedAt: startedAt,
                    Source: PaymentMethodAttachSource.TrialStartSetupIntent),
            }
            : new object[] { trialEvent };
        await subscriptions.AppendManyAsync(parentId, batch, ct).ConfigureAwait(false);

        await consumption.ResetAsync(subjectId, ct).ConfigureAwait(false);

        // Build the response by re-reading the entitlement view — same shape
        // as GET /api/me/entitlement so the SPA can replace state without a
        // separate fetch.
        var freshView = await resolver.ResolveAsync(subjectId, ct).ConfigureAwait(false);
        var freshSnapshot = await consumption.GetAsync(subjectId, ct).ConfigureAwait(false);
        var trialDto = BuildTrialStateDto(freshView, freshView.TrialCaps ?? caps, freshSnapshot);
        var responseDto = new EntitlementResponseDto(
            Tier: freshView.EffectiveTier.ToString(),
            EffectiveStatus: freshView.EffectiveStatus.ToString(),
            HasPaymentMethodOnFile: freshView.HasPaymentMethodOnFile,
            Trial: trialDto,
            Subscription: null,
            DiscountApplied: null);
        return Results.Ok(responseDto);
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
