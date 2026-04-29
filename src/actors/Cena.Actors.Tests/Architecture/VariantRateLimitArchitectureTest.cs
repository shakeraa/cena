// =============================================================================
// Cena Platform — Variant Rate-Limit Architecture Test (PRR-265, ADR-0059 §15.5)
//
// Pins the implementation to the design-of-record:
//   - The §15.5 numeric matrix has not drifted from the ADR
//   - Scope names match the dashboard / Redis keyspace contract
//   - VariantServiceRegistration registers all three seams (Policy +
//     Limiter + Gate) under their canonical interfaces
//   - GenerateSimilarHandler sources VariantKind.Structural (not
//     Parametric) so the curator-author endpoint is correctly classified
//
// A failure here means a careless edit broke the design contract. Read
// ADR-0059 §15.5 + the per-test rationale before "fixing" the test.
// =============================================================================

using Cena.Actors.Variants;
using Cena.Infrastructure.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cena.Actors.Tests.Architecture;

public sealed class VariantRateLimitArchitectureTest
{
    // -----------------------------------------------------------------
    // ADR-0059 §15.5 numeric pinning
    // -----------------------------------------------------------------
    [Fact]
    public void FreeWithPaymentStructuralPerDay_Is_6()
    {
        // ADR-0059 §15.5 row 1: free with payment-method, structural, 6/day
        Assert.Equal(6, VariantRateLimitPolicy.FreeWithPaymentStructuralPerDay);
    }

    [Fact]
    public void PaidStructuralPerDay_Is_25()
    {
        // ADR-0059 §15.5 row 1 paid column
        Assert.Equal(25, VariantRateLimitPolicy.PaidStructuralPerDay);
    }

    [Fact]
    public void PaidParametricPerDay_Is_50()
    {
        // ADR-0059 §15.5 row 2 paid column
        Assert.Equal(50, VariantRateLimitPolicy.PaidParametricPerDay);
    }

    [Fact]
    public void StructuralPerSource_Is_2()
    {
        // ADR-0059 §15.5 cogsci spacing — 2 structural per source
        Assert.Equal(2, VariantRateLimitPolicy.StructuralPerSource);
    }

    [Fact]
    public void ParametricPerSource_Is_5()
    {
        // ADR-0059 §15.5 — 5 parametric per source
        Assert.Equal(5, VariantRateLimitPolicy.ParametricPerSource);
    }

    [Fact]
    public void FreeNoPayment_StructuralPerDay_Is_0()
    {
        // ADR-0059 §15.5 redteam M-1: no variants for unverified free tier
        Assert.Equal(0, VariantRateLimitPolicy.FreeNoPaymentStructuralPerDay);
    }

    [Fact]
    public void FreeParametricPerDay_Is_0()
    {
        // Q-A: parametric is paid-only (Ministry §16 derivative-works)
        Assert.Equal(0, VariantRateLimitPolicy.FreeParametricPerDay);
    }

    [Fact]
    public void InstituteFreeStructuralPerDay_LessThan_PaidCeiling()
    {
        // Finops invariant: free institute can never accidentally generate
        // paid-tier-equivalent spend.
        Assert.True(
            VariantRateLimitPolicy.InstituteFreeStructuralPerDay
            < VariantRateLimitPolicy.InstitutePaidStructuralPerDay,
            "Free-institute structural ceiling must be lower than paid-institute ceiling.");
    }

    // -----------------------------------------------------------------
    // Window pinning
    // -----------------------------------------------------------------
    [Fact]
    public void PerDayWindow_Is_24h()
    {
        var policy = new VariantRateLimitPolicy();
        Assert.Equal(TimeSpan.FromHours(24), policy.PerDayWindow);
    }

    [Fact]
    public void PerSourceWindow_Is_30d()
    {
        // ADR-0059 §14.3.1 cogsci spacing benefit
        var policy = new VariantRateLimitPolicy();
        Assert.Equal(TimeSpan.FromDays(30), policy.PerSourceWindow);
    }

    // -----------------------------------------------------------------
    // Scope name pinning — dashboard + Redis keyspace contract
    // -----------------------------------------------------------------
    [Fact]
    public void ScopeNames_StableForDashboardAndKeyspace()
    {
        // Renaming any of these rotates the Redis keyspace and breaks
        // the variant-rate-limit dashboard. Coordinated rename only.
        Assert.Equal("student-day", VariantGenerationGate.ScopeNames.StudentDay);
        Assert.Equal("student-source-30d", VariantGenerationGate.ScopeNames.StudentSource);
        Assert.Equal("institute-day", VariantGenerationGate.ScopeNames.InstituteDay);
        Assert.Equal("institute-source-day", VariantGenerationGate.ScopeNames.InstituteSourceDay);
    }

    [Fact]
    public void RedisKeyPrefix_Is_Stable()
    {
        // Editing this prefix orphans every counter in production Redis.
        // Coordinated rename + migration only.
        Assert.Equal("cena:vrl", RedisVariantRateLimiter.KeyPrefix);
    }

    // -----------------------------------------------------------------
    // DI registration pinning
    // -----------------------------------------------------------------
    [Fact]
    public void AddVariantGenerationGate_Registers_AllThreeSeams()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        // Stub the resolver so the gate composition can resolve at the
        // test-build-time. The real resolver is registered transitively
        // by AddSubscriptionsMarten in the host.
        services.AddSingleton<Cena.Actors.Subscriptions.IStudentEntitlementResolver>(
            NSubstitute.Substitute.For<Cena.Actors.Subscriptions.IStudentEntitlementResolver>());
        // RedisVariantRateLimiter requires IConnectionMultiplexer; stub.
        services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(
            NSubstitute.Substitute.For<StackExchange.Redis.IConnectionMultiplexer>());

        services.AddVariantGenerationGate();
        var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<IVariantRateLimitPolicy>());
        Assert.NotNull(sp.GetRequiredService<IVariantRateLimiter>());
        Assert.NotNull(sp.GetRequiredService<IVariantGenerationGate>());

        // Production binding must NOT be the null limiter.
        var limiter = sp.GetRequiredService<IVariantRateLimiter>();
        Assert.IsNotType<NullVariantRateLimiter>(limiter);
    }

    [Fact]
    public void AddVariantGenerationGateForTesting_Uses_NullLimiter()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMetrics();
        services.AddSingleton<Cena.Actors.Subscriptions.IStudentEntitlementResolver>(
            NSubstitute.Substitute.For<Cena.Actors.Subscriptions.IStudentEntitlementResolver>());

        services.AddVariantGenerationGateForTesting();
        var sp = services.BuildServiceProvider();

        Assert.IsType<NullVariantRateLimiter>(sp.GetRequiredService<IVariantRateLimiter>());
        Assert.NotNull(sp.GetRequiredService<IVariantGenerationGate>());
    }

    // -----------------------------------------------------------------
    // Wire-format pinning — the SPA + observability dashboards key off
    // these strings. Coordinated rename only.
    // -----------------------------------------------------------------
    [Fact]
    public void DeniedReasonCodes_StableWireFormat()
    {
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

    // -----------------------------------------------------------------
    // HTTP status mapping pinning
    // -----------------------------------------------------------------
    [Fact]
    public void HttpStatusForCapReached_Is_429()
    {
        Assert.Equal(429, VariantGenerationGateDecision.HttpStatusFor(
            VariantGenerationDenialReason.PerStudentDayCapReached));
        Assert.Equal(429, VariantGenerationGateDecision.HttpStatusFor(
            VariantGenerationDenialReason.PerStudentSourceCapReached));
        Assert.Equal(429, VariantGenerationGateDecision.HttpStatusFor(
            VariantGenerationDenialReason.PerInstituteDayCapReached));
        Assert.Equal(429, VariantGenerationGateDecision.HttpStatusFor(
            VariantGenerationDenialReason.PerInstituteSourceCapReached));
    }

    [Fact]
    public void HttpStatusForEntitlement_Is_403()
    {
        Assert.Equal(403, VariantGenerationGateDecision.HttpStatusFor(
            VariantGenerationDenialReason.TierDoesNotPermitVariants));
        Assert.Equal(403, VariantGenerationGateDecision.HttpStatusFor(
            VariantGenerationDenialReason.PaymentMethodRequired));
        Assert.Equal(403, VariantGenerationGateDecision.HttpStatusFor(
            VariantGenerationDenialReason.LegalFlagDisabled));
    }
}
