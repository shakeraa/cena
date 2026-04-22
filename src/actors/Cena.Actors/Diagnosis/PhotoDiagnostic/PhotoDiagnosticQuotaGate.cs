// =============================================================================
// Cena Platform — PhotoDiagnosticQuotaGate (EPIC-PRR-J PRR-400/401/402)
//
// Single call the intake endpoint makes before invoking the assembler.
// Composes:
//   - IStudentEntitlementResolver  : resolves current tier caps
//   - IPhotoDiagnosticMonthlyUsage : reads current count for this month
//   - IPerTierCapEnforcer          : maps caps + usage -> decision
//
// Returns a QuotaDecision with Allow / SoftCapReached / HardCapReached
// (the same three-state enum used elsewhere). SoftCapReached still lets
// the diagnostic run; the UI layers positive-framing upsell (per
// ADR-0048 — no scarcity copy, no countdown pressure).
//
// On Allow + SoftCapReached the caller MUST invoke CommitAsync after
// running the diagnostic so the counter moves forward. This two-phase
// (check -> commit) shape avoids counting failed invocations against
// the cap.
// =============================================================================

using Cena.Actors.Subscriptions;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>Quota-gate decision returned to intake.</summary>
public sealed record PhotoDiagnosticQuotaDecision(
    CapDecision Decision,
    int CurrentUsage,
    int SoftCap,
    int HardCap,
    SubscriptionTier Tier);

/// <summary>Port.</summary>
public interface IPhotoDiagnosticQuotaGate
{
    /// <summary>
    /// Check whether the student may run a photo diagnostic right now.
    /// Does not mutate usage — commit separately after a successful run.
    /// </summary>
    Task<PhotoDiagnosticQuotaDecision> CheckAsync(
        string studentSubjectIdHash, DateTimeOffset asOfUtc, CancellationToken ct);

    /// <summary>
    /// Increment the usage counter. Call this only after a diagnostic
    /// actually ran (so failed attempts don't burn the student's cap).
    /// </summary>
    Task CommitAsync(string studentSubjectIdHash, DateTimeOffset asOfUtc, CancellationToken ct);
}

/// <summary>Default implementation.</summary>
public sealed class PhotoDiagnosticQuotaGate : IPhotoDiagnosticQuotaGate
{
    private readonly IStudentEntitlementResolver _entitlements;
    private readonly IPhotoDiagnosticMonthlyUsage _usage;
    private readonly IPerTierCapEnforcer _enforcer;

    public PhotoDiagnosticQuotaGate(
        IStudentEntitlementResolver entitlements,
        IPhotoDiagnosticMonthlyUsage usage,
        IPerTierCapEnforcer enforcer)
    {
        _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));
        _usage = usage ?? throw new ArgumentNullException(nameof(usage));
        _enforcer = enforcer ?? throw new ArgumentNullException(nameof(enforcer));
    }

    public async Task<PhotoDiagnosticQuotaDecision> CheckAsync(
        string studentSubjectIdHash, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));

        var entitlement = await _entitlements.ResolveAsync(studentSubjectIdHash, ct).ConfigureAwait(false);
        var currentUsage = await _usage.GetAsync(studentSubjectIdHash, asOfUtc, ct).ConfigureAwait(false);
        var cap = _enforcer.Check(entitlement, CapCounter.PhotoDiagnosticPerMonth, currentUsage);
        return new PhotoDiagnosticQuotaDecision(
            cap.Decision, cap.CurrentUsage, cap.SoftCap, cap.HardCap, entitlement.EffectiveTier);
    }

    public Task CommitAsync(string studentSubjectIdHash, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        return _usage.IncrementAsync(studentSubjectIdHash, asOfUtc, ct);
    }
}
