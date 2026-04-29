// =============================================================================
// Cena Platform — Default Variant Generation Gate (PRR-265, ADR-0059 §15.5 R1)
//
// Composes:
//   - IStudentEntitlementResolver  (tier lookup; the entitlement view is
//     ADR-0059's source-of-truth for "which caps apply")
//   - IVariantRateLimitPolicy      (caps-by-tier+kind+paymentVerified)
//   - IVariantRateLimiter          (Redis sliding-window counter)
//
// Decision order (each step short-circuits to Deny):
//
//   1. Legal flag (Cena:Variants:BagrutSeedToLlmEnabled)
//      → caller-supplied; we do NOT read IConfiguration directly so the
//        gate stays testable + composable inside non-host contexts.
//   2. Tier eligibility
//      → free-tier parametric is forbidden (Q-A); covered by policy
//        returning PerDayLimit=0 — surfaced here as TierDoesNotPermitVariants.
//   3. Payment-method verification
//      → free-tier structural without payment is forbidden (M-1); covered
//        by policy returning RequiresPayment=true and PerDayLimit=0.
//   4. Rate-limit scopes (in caller-deterministic order)
//      → per-(student, day), per-(student, source, 30d),
//        per-(institute, day), per-(institute, source, day).
//        Source scopes are skipped when SourcePaperCode is null.
//        Institute scopes are skipped when InstituteId is null.
//
// CommitAsync mirrors CheckAsync but calls IVariantRateLimiter.CommitAsync
// for every applicable scope. The legal-flag and tier/payment checks are
// not "committed" because they are stateless guards.
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Infrastructure.RateLimiting;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Variants;

/// <summary>
/// Default gate composition.
/// </summary>
public sealed class VariantGenerationGate : IVariantGenerationGate
{
    private readonly IStudentEntitlementResolver _entitlements;
    private readonly IVariantRateLimitPolicy _policy;
    private readonly IVariantRateLimiter _limiter;
    private readonly ILogger<VariantGenerationGate> _logger;

    public VariantGenerationGate(
        IStudentEntitlementResolver entitlements,
        IVariantRateLimitPolicy policy,
        IVariantRateLimiter limiter,
        ILogger<VariantGenerationGate> logger)
    {
        _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<VariantGenerationGateDecision> CheckAsync(
        VariantGenerationContext context,
        DateTimeOffset asOfUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateContext(context);

        // Step 1: legal flag
        if (!context.LegalFlagEnabled)
        {
            _logger.LogInformation(
                "[VARIANT_GATE_DENY] reason=legal_flag_disabled student={Student}",
                context.StudentSubjectIdEncrypted);
            return VariantGenerationGateDecision.Deny(
                VariantGenerationDenialReason.LegalFlagDisabled,
                "Cena:Variants:BagrutSeedToLlmEnabled is false; pending PRR-249 legal sign-off.");
        }

        // Step 2 + 3: tier + payment via policy
        var entitlement = await _entitlements.ResolveAsync(
            context.StudentSubjectIdEncrypted, ct).ConfigureAwait(false);
        var caps = await _policy.ResolveAsync(
            entitlement.EffectiveTier,
            context.Kind,
            context.PaymentVerified,
            context.InstituteId,
            asOfUtc,
            ct).ConfigureAwait(false);

        if (caps.PerDayLimit <= 0)
        {
            // Two reasons land at PerDayLimit=0: free-tier-no-payment
            // (RequiresPaymentVerification=true) → PaymentMethodRequired,
            // OR free-tier-parametric (forbidden outright) → TierDoesNotPermitVariants.
            var reason = caps.RequiresPaymentVerification && context.Kind == VariantKind.Structural
                ? VariantGenerationDenialReason.PaymentMethodRequired
                : VariantGenerationDenialReason.TierDoesNotPermitVariants;

            _logger.LogInformation(
                "[VARIANT_GATE_DENY] reason={Reason} student={Student} tier={Tier} kind={Kind}",
                reason, context.StudentSubjectIdEncrypted, entitlement.EffectiveTier, context.Kind);
            return VariantGenerationGateDecision.Deny(
                reason,
                $"Tier {entitlement.EffectiveTier} does not permit {context.Kind} variants " +
                $"(payment_verified={context.PaymentVerified}).");
        }

        // Step 4: rate-limit scopes
        var scopes = BuildScopes(context, caps, _policy);
        var decision = await _limiter.CheckAsync(scopes, asOfUtc, ct).ConfigureAwait(false);
        if (!decision.Allowed)
        {
            var rlReason = MapDeniedScopeToReason(decision.DeniedScopeName);
            _logger.LogInformation(
                "[VARIANT_GATE_DENY] reason={Reason} student={Student} tier={Tier} " +
                "scope={Scope} count={Count} limit={Limit} retry_after_seconds={Retry}",
                rlReason, context.StudentSubjectIdEncrypted, entitlement.EffectiveTier,
                decision.DeniedScopeName, decision.CurrentCount, decision.Limit,
                decision.RetryAfter is null ? "?" : ((int)decision.RetryAfter.Value.TotalSeconds).ToString());
            return VariantGenerationGateDecision.Deny(
                rlReason,
                $"{decision.DeniedScopeName} cap reached: {decision.CurrentCount}/{decision.Limit}.",
                decision.RetryAfter);
        }

        return VariantGenerationGateDecision.Allow();
    }

    /// <inheritdoc/>
    public async Task CommitAsync(
        VariantGenerationContext context,
        string commitId,
        DateTimeOffset asOfUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(commitId);
        ValidateContext(context);

        if (!context.LegalFlagEnabled) return;          // never count denied attempts

        var entitlement = await _entitlements.ResolveAsync(
            context.StudentSubjectIdEncrypted, ct).ConfigureAwait(false);
        var caps = await _policy.ResolveAsync(
            entitlement.EffectiveTier,
            context.Kind,
            context.PaymentVerified,
            context.InstituteId,
            asOfUtc,
            ct).ConfigureAwait(false);

        var scopes = BuildScopes(context, caps, _policy);
        await _limiter.CommitAsync(scopes, commitId, asOfUtc, ct).ConfigureAwait(false);
    }

    private static void ValidateContext(VariantGenerationContext context)
    {
        if (string.IsNullOrWhiteSpace(context.StudentSubjectIdEncrypted))
            throw new ArgumentException(
                "StudentSubjectIdEncrypted is required (use 'curator:{id}' for curator flows).",
                nameof(context));
    }

    /// <summary>
    /// Map limiter scope name → gate denial reason. Stable mapping; new
    /// scope names must extend <see cref="VariantGenerationDenialReason"/>
    /// in lockstep.
    /// </summary>
    private static VariantGenerationDenialReason MapDeniedScopeToReason(string? scopeName) => scopeName switch
    {
        ScopeNames.StudentDay => VariantGenerationDenialReason.PerStudentDayCapReached,
        ScopeNames.StudentSource => VariantGenerationDenialReason.PerStudentSourceCapReached,
        ScopeNames.InstituteDay => VariantGenerationDenialReason.PerInstituteDayCapReached,
        ScopeNames.InstituteSourceDay => VariantGenerationDenialReason.PerInstituteSourceCapReached,
        _ => VariantGenerationDenialReason.PerStudentDayCapReached,    // safe default
    };

    /// <summary>
    /// Build the ordered scope list. Order matters — denial reports
    /// the first denying scope, so we put per-student scopes first
    /// (more user-actionable than per-institute).
    /// </summary>
    internal static IReadOnlyList<VariantRateLimitScope> BuildScopes(
        VariantGenerationContext context,
        VariantRateLimitCaps caps,
        IVariantRateLimitPolicy policy)
    {
        var scopes = new List<VariantRateLimitScope>(capacity: 4);

        // Per-(student, day, kind)
        scopes.Add(new VariantRateLimitScope(
            ScopeName: ScopeNames.StudentDay,
            PartitionKey: $"student:{context.StudentSubjectIdEncrypted}|kind:{context.Kind}",
            Window: policy.PerDayWindow,
            Limit: caps.PerDayLimit));

        // Per-(student, source, 30d, kind) — only when source is known
        if (!string.IsNullOrWhiteSpace(context.SourcePaperCode))
        {
            scopes.Add(new VariantRateLimitScope(
                ScopeName: ScopeNames.StudentSource,
                PartitionKey: $"student:{context.StudentSubjectIdEncrypted}|source:{context.SourcePaperCode}|kind:{context.Kind}",
                Window: policy.PerSourceWindow,
                Limit: caps.PerSourceLimit));
        }

        // Per-(institute, day, kind) — only when institute is known
        if (!string.IsNullOrWhiteSpace(context.InstituteId))
        {
            scopes.Add(new VariantRateLimitScope(
                ScopeName: ScopeNames.InstituteDay,
                PartitionKey: $"institute:{context.InstituteId}|kind:{context.Kind}",
                Window: policy.PerDayWindow,
                Limit: caps.InstitutePerDayLimit));

            // Per-(institute, source, day, kind)
            if (!string.IsNullOrWhiteSpace(context.SourcePaperCode))
            {
                scopes.Add(new VariantRateLimitScope(
                    ScopeName: ScopeNames.InstituteSourceDay,
                    PartitionKey: $"institute:{context.InstituteId}|source:{context.SourcePaperCode}|kind:{context.Kind}",
                    Window: policy.PerDayWindow,
                    Limit: caps.InstitutePerSourceLimit));
            }
        }

        return scopes;
    }

    /// <summary>
    /// Stable scope names. Public so dashboards + the architecture test
    /// can pin them. Editing these strings rotates the Redis keyspace
    /// (existing counters become orphaned), so changes must be coordinated.
    /// </summary>
    public static class ScopeNames
    {
        public const string StudentDay = "student-day";
        public const string StudentSource = "student-source-30d";
        public const string InstituteDay = "institute-day";
        public const string InstituteSourceDay = "institute-source-day";
    }
}
