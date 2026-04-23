// =============================================================================
// Cena Platform — PhotoDiagnosticQuotaGate (EPIC-PRR-J PRR-400/401/402/391)
//
// Single call the intake endpoint makes before invoking the assembler.
// Composes:
//   - IStudentEntitlementResolver  : resolves current tier caps
//   - IPhotoDiagnosticMonthlyUsage : reads current count for this month
//   - IPerTierCapEnforcer          : maps caps + usage -> decision
//   - IDiagnosticCreditLedger (opt): subtracts support-issued free credits
//   - IHardCapExtensionAdjuster (opt): adds support-granted month-end
//                                       extensions to the effective hard cap
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
//
// Ledger-not-decrement: the PRR-391 CreditLedger and the PRR-402 hard-cap
// support-ticket aggregate BOTH use the same discipline — the raw upload
// counter is never mutated by a "forgiveness" or "extension" event. Instead
// those events land as additive rows in their respective aggregates, and
// this gate reads them at check-time:
//
//     effectiveUsage = max(0, rawUsage - monthlyCredits)
//     effectiveHardCap = baseHardCap + monthlyHardCapExtension
//
// That keeps the upload counter truthful for metrics, audit samplers, and
// abuse detection, while making the cap behave as the student expects. The
// credit adjuster targets the "error" path (support confirmed our system
// made a mistake → forgive the upload); the hard-cap adjuster targets the
// "legitimate heavy use" path (student hit 300/mo on Premium, support
// manually approves a one-time month-end extension). Both dependencies
// are nullable so test harnesses that don't care about either path don't
// have to wire a fixture; production DI always binds both.
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
    private readonly IDiagnosticCreditLedger? _credits;
    private readonly IHardCapExtensionAdjuster? _hardCapAdjuster;

    public PhotoDiagnosticQuotaGate(
        IStudentEntitlementResolver entitlements,
        IPhotoDiagnosticMonthlyUsage usage,
        IPerTierCapEnforcer enforcer,
        IDiagnosticCreditLedger? credits = null,
        IHardCapExtensionAdjuster? hardCapAdjuster = null)
    {
        _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));
        _usage = usage ?? throw new ArgumentNullException(nameof(usage));
        _enforcer = enforcer ?? throw new ArgumentNullException(nameof(enforcer));
        _credits = credits;
        _hardCapAdjuster = hardCapAdjuster;
    }

    public async Task<PhotoDiagnosticQuotaDecision> CheckAsync(
        string studentSubjectIdHash, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));

        var entitlement = await _entitlements.ResolveAsync(studentSubjectIdHash, ct).ConfigureAwait(false);
        var rawUsage = await _usage.GetAsync(studentSubjectIdHash, asOfUtc, ct).ConfigureAwait(false);

        // PRR-391: subtract monthly support credits. Credits-not-bound is
        // equivalent to "no credits issued this month" — behaviourally
        // identical to the pre-PRR-391 gate.
        var credits = _credits is null
            ? 0
            : await _credits.CountFreeCreditsAsync(studentSubjectIdHash, asOfUtc, ct).ConfigureAwait(false);
        var effectiveUsage = Math.Max(0, rawUsage - credits);

        var cap = _enforcer.Check(entitlement, CapCounter.PhotoDiagnosticPerMonth, effectiveUsage);

        // PRR-402: bump the effective hard cap by any support-granted
        // extensions active this month. Not-bound or zero-grants is
        // behaviourally identical to the pre-PRR-402 gate — we return
        // the enforcer's decision verbatim. When there IS an active grant
        // we recompute the decision against the bumped cap so the student
        // gets Allow / SoftCapReached / HardCapReached correctly.
        var hardCapExtension = _hardCapAdjuster is null
            ? 0
            : await _hardCapAdjuster.GetActiveExtensionAsync(studentSubjectIdHash, asOfUtc, ct)
                .ConfigureAwait(false);

        if (hardCapExtension <= 0)
        {
            return new PhotoDiagnosticQuotaDecision(
                cap.Decision, cap.CurrentUsage, cap.SoftCap, cap.HardCap, entitlement.EffectiveTier);
        }

        var bumpedHardCap = cap.HardCap + hardCapExtension;
        var adjustedDecision = RecomputeDecision(
            effectiveUsage, cap.SoftCap, bumpedHardCap);
        return new PhotoDiagnosticQuotaDecision(
            adjustedDecision, cap.CurrentUsage, cap.SoftCap, bumpedHardCap, entitlement.EffectiveTier);
    }

    /// <summary>
    /// Decision recomputation against a bumped hard cap. Mirrors the
    /// tri-state logic in PerTierCapEnforcer: usage at/above the hard cap
    /// is HardCapReached; at/above soft but below hard is SoftCapReached;
    /// otherwise Allow. Lives inline (not on the enforcer) because the
    /// support-granted extension is specifically a PhotoDiagnostic concern
    /// — the enforcer itself stays agnostic of which counters have adjusters.
    /// </summary>
    private static CapDecision RecomputeDecision(int effectiveUsage, int softCap, int bumpedHardCap)
    {
        if (effectiveUsage >= bumpedHardCap) return CapDecision.HardCapReached;
        if (softCap > 0 && effectiveUsage >= softCap) return CapDecision.SoftCapReached;
        return CapDecision.Allow;
    }

    public Task CommitAsync(string studentSubjectIdHash, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        return _usage.IncrementAsync(studentSubjectIdHash, asOfUtc, ct);
    }
}
