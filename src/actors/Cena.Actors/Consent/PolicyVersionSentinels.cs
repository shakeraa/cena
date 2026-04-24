// =============================================================================
// Cena Platform — Privacy-policy version sentinels (prr-123)
//
// Centralises the small set of non-user-supplied version strings that the
// consent-aggregate write path and audit exporter use to reason about
// "accepted policy version" gaps (legacy data, missing value on upcast,
// automated/system grants where no human accepted a policy at all).
// =============================================================================

namespace Cena.Actors.Consent;

/// <summary>
/// Non-user-supplied version strings the consent surface may carry.
/// None of these are valid version strings; they exist so the audit
/// trail never has to render "(null)".
/// </summary>
public static class PolicyVersionSentinels
{
    /// <summary>
    /// Applied to grants that pre-date the dual-version privacy policy
    /// rollout (prr-123). Upcasters rewrite V1 events to V2 with this
    /// value; the admin audit export surfaces it verbatim so that
    /// counsel can trivially grep for pre-versioning records.
    /// </summary>
    public const string PreVersioning = "v0.0.0-pre-versioning";

    /// <summary>
    /// Applied to automated grants emitted by the System role
    /// (retention worker, birthday age-band transitions, compliance
    /// flips). The System role does not accept a privacy policy, so
    /// emitting a real version string would be misleading.
    /// </summary>
    public const string SystemOp = "v0.0.0-system-op";

    /// <summary>
    /// True when the supplied version matches any sentinel (caller may
    /// want to render it differently in audit reports).
    /// </summary>
    public static bool IsSentinel(string? value)
        => string.Equals(value, PreVersioning, StringComparison.Ordinal)
           || string.Equals(value, SystemOp, StringComparison.Ordinal);
}
