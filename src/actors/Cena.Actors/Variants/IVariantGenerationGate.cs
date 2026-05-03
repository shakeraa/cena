// =============================================================================
// Cena Platform — Variant Generation Gate (PRR-265, ADR-0059 §15.5 R1)
//
// Composed gate that callers (admin GenerateSimilarHandler + future student
// PRR-245 endpoint) consult before invoking the LLM. Returns a structured
// decision so the calling layer maps to the right HTTP code:
//
//   Allow                                  → 200 (caller proceeds)
//   Denied(tier_does_not_permit_variants)  → 403 (per ADR-0059 §15.5)
//   Denied(payment_method_required)        → 403
//   Denied(per_*_cap_reached)              → 429 + Retry-After
//   Denied(legal_flag_disabled)            → 403 (BagrutSeedToLlmEnabled=false)
//
// The gate does NOT perform the variant generation itself; callers retain
// the cost-bearing call (BatchGenerateAsync). After successful generation
// the caller MUST call CommitAsync so the counter advances.
// =============================================================================

namespace Cena.Actors.Variants;

/// <summary>
/// Why a variant-generation request was denied.
/// </summary>
public enum VariantGenerationDenialReason
{
    /// <summary>Free tier requested parametric (paid-only per §15.5 / Q-A).</summary>
    TierDoesNotPermitVariants,

    /// <summary>
    /// Free-tier structural without verified payment-method or institute-SSO
    /// (per §15.5 redteam M-1).
    /// </summary>
    PaymentMethodRequired,

    /// <summary>Per-(student, day) cap exhausted.</summary>
    PerStudentDayCapReached,

    /// <summary>Per-(student, source, 30d) cap exhausted.</summary>
    PerStudentSourceCapReached,

    /// <summary>Per-(institute, day) cap exhausted.</summary>
    PerInstituteDayCapReached,

    /// <summary>Per-(institute, source, day) cap exhausted.</summary>
    PerInstituteSourceCapReached,

    /// <summary>
    /// <c>Cena:Variants:BagrutSeedToLlmEnabled</c> is false (PRR-249 legal sign-off).
    /// </summary>
    LegalFlagDisabled,
}

/// <summary>
/// Outcome of a gate check.
/// </summary>
public sealed record VariantGenerationGateDecision(
    bool Allowed,
    VariantGenerationDenialReason? DeniedReason,
    string? DeniedReasonCode,
    string? DeniedDetail,
    TimeSpan? RetryAfter)
{
    /// <summary>
    /// Map a denial reason to the canonical wire-format error code that
    /// the SPA + telemetry consume. Stable strings — do NOT rename.
    /// </summary>
    public static string ReasonCode(VariantGenerationDenialReason r) => r switch
    {
        VariantGenerationDenialReason.TierDoesNotPermitVariants => "tier_does_not_permit_variants",
        VariantGenerationDenialReason.PaymentMethodRequired => "payment_method_required",
        VariantGenerationDenialReason.PerStudentDayCapReached => "per_student_day_cap_reached",
        VariantGenerationDenialReason.PerStudentSourceCapReached => "per_student_source_cap_reached",
        VariantGenerationDenialReason.PerInstituteDayCapReached => "per_institute_day_cap_reached",
        VariantGenerationDenialReason.PerInstituteSourceCapReached => "per_institute_source_cap_reached",
        VariantGenerationDenialReason.LegalFlagDisabled => "variant_generation_legal_flag_disabled",
        _ => throw new ArgumentOutOfRangeException(nameof(r), r, "Unknown denial reason."),
    };

    /// <summary>
    /// HTTP status code to surface for a denial. Caps return 429
    /// (rate-limited, retryable). Tier/payment return 403 (entitlement —
    /// retrying without changing tier won't help).
    /// </summary>
    public static int HttpStatusFor(VariantGenerationDenialReason r) => r switch
    {
        VariantGenerationDenialReason.TierDoesNotPermitVariants => 403,
        VariantGenerationDenialReason.PaymentMethodRequired => 403,
        VariantGenerationDenialReason.LegalFlagDisabled => 403,
        VariantGenerationDenialReason.PerStudentDayCapReached => 429,
        VariantGenerationDenialReason.PerStudentSourceCapReached => 429,
        VariantGenerationDenialReason.PerInstituteDayCapReached => 429,
        VariantGenerationDenialReason.PerInstituteSourceCapReached => 429,
        _ => throw new ArgumentOutOfRangeException(nameof(r), r, "Unknown denial reason."),
    };

    /// <summary>Convenience constructor for an allow.</summary>
    public static VariantGenerationGateDecision Allow() =>
        new(true, null, null, null, null);

    /// <summary>Convenience constructor for a denial.</summary>
    public static VariantGenerationGateDecision Deny(
        VariantGenerationDenialReason reason, string? detail = null, TimeSpan? retryAfter = null) =>
        new(false, reason, ReasonCode(reason), detail, retryAfter);
}

/// <summary>
/// Identity + context for one variant-generation attempt.
/// </summary>
/// <param name="StudentSubjectIdEncrypted">
/// The student id this generation is being attributed to. For curator-author
/// flows, pass a stable curator pseudonym (e.g. <c>"curator:{userId}"</c>);
/// callers MUST keep the namespace separate from real student ids so
/// per-student counters don't collide.
/// </param>
/// <param name="InstituteId">
/// Owning institute id, or null for self-pay (gate skips per-institute
/// scopes when null).
/// </param>
/// <param name="SourcePaperCode">
/// The Ministry שאלון / source-paper code being seeded from. Used to scope
/// per-source counters. May be null when the variant is not source-anchored
/// (gate skips per-source scopes when null).
/// </param>
/// <param name="Kind">Structural or parametric.</param>
/// <param name="PaymentVerified">
/// True iff the student/curator has verified payment-method OR institute-SSO
/// linkage (caller resolves this from the entitlement view + billing
/// state). Required for free-tier structural per §15.5 redteam M-1.
/// </param>
/// <param name="LegalFlagEnabled">
/// True iff <c>Cena:Variants:BagrutSeedToLlmEnabled</c> is true. Caller
/// passes the resolved value so the gate is not bound to IConfiguration.
/// </param>
public sealed record VariantGenerationContext(
    string StudentSubjectIdEncrypted,
    string? InstituteId,
    string? SourcePaperCode,
    Cena.Infrastructure.RateLimiting.VariantKind Kind,
    bool PaymentVerified,
    bool LegalFlagEnabled);

/// <summary>
/// Composed gate: tier check + payment check + multi-scope rate-limit check.
/// </summary>
public interface IVariantGenerationGate
{
    /// <summary>
    /// Non-mutating check. Returns Allow or a structured Denied. Caller
    /// MUST call <see cref="CommitAsync"/> after the variant generation
    /// completes successfully.
    /// </summary>
    Task<VariantGenerationGateDecision> CheckAsync(
        VariantGenerationContext context,
        DateTimeOffset asOfUtc,
        CancellationToken ct);

    /// <summary>
    /// Increment counters for a successful generation. Pass the same
    /// <paramref name="commitId"/> as the persisted question/variant id so
    /// retries inside one logical generation don't double-count.
    /// </summary>
    Task CommitAsync(
        VariantGenerationContext context,
        string commitId,
        DateTimeOffset asOfUtc,
        CancellationToken ct);
}
