// =============================================================================
// Cena Platform — VariantGenerationGate tests (PRR-265, ADR-0059 §15.5 R1)
//
// Composed-gate tests using NSubstitute fakes for IStudentEntitlementResolver
// and IVariantRateLimiter, with the real VariantRateLimitPolicy. Exercises
// every denial reason + the allow path + the commit path.
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Variants;
using Cena.Infrastructure.RateLimiting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Actors.Tests.Variants;

public sealed class VariantGenerationGateTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 4, 29, 10, 0, 0, TimeSpan.Zero);

    private readonly IStudentEntitlementResolver _entitlements = Substitute.For<IStudentEntitlementResolver>();
    private readonly IVariantRateLimiter _limiter = Substitute.For<IVariantRateLimiter>();
    private readonly VariantRateLimitPolicy _policy = new();

    private VariantGenerationGate BuildGate() =>
        new(_entitlements, _policy, _limiter, NullLogger<VariantGenerationGate>.Instance);

    private void SeedTier(SubscriptionTier tier)
    {
        var view = new StudentEntitlementView(
            StudentSubjectIdEncrypted: "student-1",
            EffectiveTier: tier,
            SourceParentSubjectIdEncrypted: "parent-1",
            ValidUntil: null,
            LastUpdatedAt: Now);
        _entitlements.ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(view);
    }

    private void SeedLimiter(VariantRateLimitDecision decision)
    {
        _limiter.CheckAsync(
            Arg.Any<IReadOnlyList<VariantRateLimitScope>>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>())
            .Returns(decision);
    }

    private static VariantGenerationContext Context(
        bool legalFlagEnabled = true,
        VariantKind kind = VariantKind.Structural,
        bool paymentVerified = true,
        string? sourcePaperCode = "035581",
        string? instituteId = "school-A") =>
        new(
            StudentSubjectIdEncrypted: "student-1",
            InstituteId: instituteId,
            SourcePaperCode: sourcePaperCode,
            Kind: kind,
            PaymentVerified: paymentVerified,
            LegalFlagEnabled: legalFlagEnabled);

    // -----------------------------------------------------------------
    [Fact]
    public async Task Allow_Path_When_LegalFlagOn_TierEntitled_LimitOk()
    {
        SeedTier(SubscriptionTier.Premium);
        SeedLimiter(VariantRateLimitDecision.Allow());

        var gate = BuildGate();
        var decision = await gate.CheckAsync(Context(), Now, default);

        Assert.True(decision.Allowed);
        Assert.Null(decision.DeniedReason);
    }

    [Fact]
    public async Task Deny_LegalFlagDisabled_ShortCircuits_BeforeResolverHit()
    {
        var gate = BuildGate();
        var decision = await gate.CheckAsync(Context(legalFlagEnabled: false), Now, default);

        Assert.False(decision.Allowed);
        Assert.Equal(VariantGenerationDenialReason.LegalFlagDisabled, decision.DeniedReason);
        Assert.Equal("variant_generation_legal_flag_disabled", decision.DeniedReasonCode);
        // resolver MUST NOT be hit when the legal flag is off (cost +
        // honesty: don't load entitlement state we won't use)
        await _entitlements.DidNotReceive().ResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deny_FreeTier_Parametric_TierDoesNotPermit()
    {
        SeedTier(SubscriptionTier.Unsubscribed);

        var gate = BuildGate();
        var decision = await gate.CheckAsync(
            Context(kind: VariantKind.Parametric, paymentVerified: true),
            Now, default);

        Assert.False(decision.Allowed);
        Assert.Equal(VariantGenerationDenialReason.TierDoesNotPermitVariants, decision.DeniedReason);
        Assert.Equal("tier_does_not_permit_variants", decision.DeniedReasonCode);
        Assert.Equal(403, VariantGenerationGateDecision.HttpStatusFor(decision.DeniedReason!.Value));
    }

    [Fact]
    public async Task Deny_FreeTier_Structural_NoPayment_PaymentRequired()
    {
        SeedTier(SubscriptionTier.Unsubscribed);

        var gate = BuildGate();
        var decision = await gate.CheckAsync(
            Context(kind: VariantKind.Structural, paymentVerified: false),
            Now, default);

        Assert.False(decision.Allowed);
        Assert.Equal(VariantGenerationDenialReason.PaymentMethodRequired, decision.DeniedReason);
        Assert.Equal("payment_method_required", decision.DeniedReasonCode);
        Assert.Equal(403, VariantGenerationGateDecision.HttpStatusFor(decision.DeniedReason!.Value));
    }

    [Fact]
    public async Task Deny_PerStudentDayCap_Returns_429_With_RetryAfter()
    {
        SeedTier(SubscriptionTier.Premium);
        SeedLimiter(new VariantRateLimitDecision(
            Allowed: false,
            DeniedScopeName: VariantGenerationGate.ScopeNames.StudentDay,
            CurrentCount: 25,
            Limit: 25,
            RetryAfter: TimeSpan.FromMinutes(7)));

        var gate = BuildGate();
        var decision = await gate.CheckAsync(Context(), Now, default);

        Assert.False(decision.Allowed);
        Assert.Equal(VariantGenerationDenialReason.PerStudentDayCapReached, decision.DeniedReason);
        Assert.Equal("per_student_day_cap_reached", decision.DeniedReasonCode);
        Assert.Equal(429, VariantGenerationGateDecision.HttpStatusFor(decision.DeniedReason!.Value));
        Assert.Equal(TimeSpan.FromMinutes(7), decision.RetryAfter);
    }

    [Fact]
    public async Task Deny_PerStudentSourceCap_Mapped()
    {
        SeedTier(SubscriptionTier.Premium);
        SeedLimiter(new VariantRateLimitDecision(
            Allowed: false,
            DeniedScopeName: VariantGenerationGate.ScopeNames.StudentSource,
            CurrentCount: 2,
            Limit: 2,
            RetryAfter: TimeSpan.FromHours(20)));

        var gate = BuildGate();
        var decision = await gate.CheckAsync(Context(), Now, default);

        Assert.Equal(VariantGenerationDenialReason.PerStudentSourceCapReached, decision.DeniedReason);
    }

    [Fact]
    public async Task Deny_PerInstituteSource_Mapped()
    {
        SeedTier(SubscriptionTier.Premium);
        SeedLimiter(new VariantRateLimitDecision(
            Allowed: false,
            DeniedScopeName: VariantGenerationGate.ScopeNames.InstituteSourceDay,
            CurrentCount: 30,
            Limit: 30,
            RetryAfter: TimeSpan.FromMinutes(15)));

        var gate = BuildGate();
        var decision = await gate.CheckAsync(Context(), Now, default);

        Assert.Equal(VariantGenerationDenialReason.PerInstituteSourceCapReached, decision.DeniedReason);
    }

    // -----------------------------------------------------------------
    [Fact]
    public async Task BuildScopes_OmitsSourceScopes_WhenSourceIsNull()
    {
        SeedTier(SubscriptionTier.Premium);
        IReadOnlyList<VariantRateLimitScope>? captured = null;
        _limiter.CheckAsync(
            Arg.Do<IReadOnlyList<VariantRateLimitScope>>(s => captured = s),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>())
            .Returns(VariantRateLimitDecision.Allow());

        var gate = BuildGate();
        await gate.CheckAsync(Context(sourcePaperCode: null), Now, default);

        Assert.NotNull(captured);
        Assert.DoesNotContain(captured!, s => s.ScopeName == VariantGenerationGate.ScopeNames.StudentSource);
        Assert.DoesNotContain(captured!, s => s.ScopeName == VariantGenerationGate.ScopeNames.InstituteSourceDay);
    }

    [Fact]
    public async Task BuildScopes_OmitsInstituteScopes_WhenInstituteIsNull()
    {
        SeedTier(SubscriptionTier.Premium);
        IReadOnlyList<VariantRateLimitScope>? captured = null;
        _limiter.CheckAsync(
            Arg.Do<IReadOnlyList<VariantRateLimitScope>>(s => captured = s),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>())
            .Returns(VariantRateLimitDecision.Allow());

        var gate = BuildGate();
        await gate.CheckAsync(Context(instituteId: null), Now, default);

        Assert.NotNull(captured);
        Assert.DoesNotContain(captured!, s => s.ScopeName == VariantGenerationGate.ScopeNames.InstituteDay);
        Assert.DoesNotContain(captured!, s => s.ScopeName == VariantGenerationGate.ScopeNames.InstituteSourceDay);
    }

    // -----------------------------------------------------------------
    [Fact]
    public async Task CommitAsync_DoesNothing_WhenLegalFlagDisabled()
    {
        var gate = BuildGate();
        await gate.CommitAsync(
            Context(legalFlagEnabled: false), "commit-1", Now, default);

        await _limiter.DidNotReceive().CommitAsync(
            Arg.Any<IReadOnlyList<VariantRateLimitScope>>(),
            Arg.Any<string>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitAsync_PassesScopesAndCommitId_WhenAllowed()
    {
        SeedTier(SubscriptionTier.Premium);

        var gate = BuildGate();
        await gate.CommitAsync(Context(), "commit-XYZ", Now, default);

        await _limiter.Received(1).CommitAsync(
            Arg.Is<IReadOnlyList<VariantRateLimitScope>>(s => s.Count >= 2),
            "commit-XYZ",
            Now,
            Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------
    [Fact]
    public void DeniedReasonCodes_AreStableWireFormat()
    {
        // Wire-format pinning. Any rename here must be coordinated with
        // the SPA + observability dashboards.
        Assert.Equal("tier_does_not_permit_variants",
            VariantGenerationGateDecision.ReasonCode(VariantGenerationDenialReason.TierDoesNotPermitVariants));
        Assert.Equal("payment_method_required",
            VariantGenerationGateDecision.ReasonCode(VariantGenerationDenialReason.PaymentMethodRequired));
        Assert.Equal("per_student_day_cap_reached",
            VariantGenerationGateDecision.ReasonCode(VariantGenerationDenialReason.PerStudentDayCapReached));
        Assert.Equal("per_student_source_cap_reached",
            VariantGenerationGateDecision.ReasonCode(VariantGenerationDenialReason.PerStudentSourceCapReached));
        Assert.Equal("per_institute_day_cap_reached",
            VariantGenerationGateDecision.ReasonCode(VariantGenerationDenialReason.PerInstituteDayCapReached));
        Assert.Equal("per_institute_source_cap_reached",
            VariantGenerationGateDecision.ReasonCode(VariantGenerationDenialReason.PerInstituteSourceCapReached));
        Assert.Equal("variant_generation_legal_flag_disabled",
            VariantGenerationGateDecision.ReasonCode(VariantGenerationDenialReason.LegalFlagDisabled));
    }
}
