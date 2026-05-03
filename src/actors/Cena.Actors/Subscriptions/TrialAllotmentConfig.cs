// =============================================================================
// Cena Platform — TrialAllotmentConfig (task t_b89826b8bd60)
//
// Single Marten document holding the platform-wide trial allotment knobs.
// Super-admin writes; everyone reads. ALL DEFAULTS = 0 → no trial offered
// out of the box (per user directive 2026-04-28: "I do not want to give
// away for free. Default 0. Both trial days and trial quota configurable.").
//
// Companion design brief (proposal):
//   docs/design/trial-then-paywall-001-discussion.md §4.1, §4.2
// Companion recycle-defense brief:
//   docs/design/trial-recycle-defense-001-research.md
//
// Why a singleton document vs an event-sourced aggregate. The trial allotment
// is a small, infrequently-changed knob set (one row per platform). The
// audit trail is captured by a separate TrialAllotmentConfigChanged_V1
// event appended to a dedicated `trial-allotment-config` event stream so
// "who changed what when" is preserved without inflating the aggregate
// surface — same shape as AlphaMigrationSeedDocument (PRR-344) which sets
// the precedent for "singleton config doc + audit event stream".
//
// Range validation (mirrored on read AND write paths):
//   TrialDurationDays:        0..30
//   TrialTutorTurns:          0..200
//   TrialPhotoDiagnostics:    0..50
//   TrialPracticeSessions:    0..20
//
// Whichever-first semantics: at trial-start, the set of non-zero limits is
// snapshot-pinned into TrialStarted_V1 so an in-flight trial is immune to
// mid-trial config changes. The RequireEntitlementFilter (Phase 1 of the
// trial-then-paywall design — separate task) reads the pinned snapshot to
// decide cap-hit at request time.
//
// All-zero special case: GetTrialEnabled() returns false. The /api/me/
// entitlement/start-trial endpoint returns 410 trial_not_offered and the
// SPA hides the "Start free trial" CTA (per task DoD).
// =============================================================================

using Marten;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Marten document holding the current platform-wide trial allotment knobs.
/// Single row, id = <see cref="CurrentId"/>. Overwritten on every super-admin
/// PATCH; an audit-trail event is appended to the dedicated stream on each
/// change.
/// </summary>
public sealed class TrialAllotmentConfig
{
    /// <summary>Singleton document id. There is exactly one row.</summary>
    public const string CurrentId = "current";

    /// <summary>Marten identity. Always <see cref="CurrentId"/>.</summary>
    public string Id { get; set; } = CurrentId;

    /// <summary>
    /// Calendar bound for trials. 0 = no calendar bound (trial ends only on
    /// quota cap-hit). When non-zero, daemon expires trial at start + days.
    /// Range 0..30 enforced at write time.
    /// </summary>
    public int TrialDurationDays { get; set; }

    /// <summary>
    /// Total tutor turns allowed during a trial. 0 = no per-trial cap on
    /// tutor turns (effectively unlimited within other constraints). Range
    /// 0..200 enforced at write time.
    /// </summary>
    public int TrialTutorTurns { get; set; }

    /// <summary>
    /// Total photo-diagnostic uploads allowed during a trial. 0 = no per-
    /// trial cap. Range 0..50 enforced at write time.
    /// </summary>
    public int TrialPhotoDiagnostics { get; set; }

    /// <summary>
    /// Total practice sessions allowed during a trial. 0 = no per-trial
    /// cap. Range 0..20 enforced at write time.
    /// </summary>
    public int TrialPracticeSessions { get; set; }

    /// <summary>Encrypted subject id of the admin that last updated the config.</summary>
    public string LastUpdatedByAdminEncrypted { get; set; } = string.Empty;

    /// <summary>When the config was last written (UTC).</summary>
    public DateTimeOffset LastUpdatedAtUtc { get; set; }

    /// <summary>
    /// True iff at least one of the four allotment knobs is non-zero. When
    /// false, the platform is not currently offering any trial — start-trial
    /// returns 410 trial_not_offered and the SPA hides the trial CTA.
    /// </summary>
    public bool TrialEnabled =>
        TrialDurationDays > 0
        || TrialTutorTurns > 0
        || TrialPhotoDiagnostics > 0
        || TrialPracticeSessions > 0;

    /// <summary>
    /// Build the safe default document — all zeros. Used at first read when
    /// no row exists yet (treat absence as "trial not configured" rather
    /// than throwing; safer default).
    /// </summary>
    public static TrialAllotmentConfig DefaultZero() => new()
    {
        Id = CurrentId,
        TrialDurationDays = 0,
        TrialTutorTurns = 0,
        TrialPhotoDiagnostics = 0,
        TrialPracticeSessions = 0,
        LastUpdatedByAdminEncrypted = string.Empty,
        LastUpdatedAtUtc = DateTimeOffset.MinValue,
    };
}

/// <summary>
/// Validation outcome returned by <see cref="TrialAllotmentValidator"/>. Either
/// a clean (validated) snapshot ready for persistence, or a structured
/// failure naming the offending field + reason so the API layer can return
/// a precise 400.
/// </summary>
public sealed record TrialAllotmentValidationResult(
    bool IsValid,
    string? FailedField,
    string? Reason);

/// <summary>
/// Pure validator for trial-allotment knobs. Same rules apply on the API
/// write path AND on read-path enforcement so a caller cannot bypass via a
/// raw store write.
/// </summary>
public static class TrialAllotmentValidator
{
    public const int MaxDurationDays = 30;
    public const int MaxTutorTurns = 200;
    public const int MaxPhotoDiagnostics = 50;
    public const int MaxPracticeSessions = 20;

    /// <summary>
    /// Validate a proposed allotment update. Returns IsValid=true with no
    /// failure on success; IsValid=false with field+reason on failure.
    /// </summary>
    public static TrialAllotmentValidationResult Validate(
        int trialDurationDays,
        int trialTutorTurns,
        int trialPhotoDiagnostics,
        int trialPracticeSessions)
    {
        if (trialDurationDays < 0 || trialDurationDays > MaxDurationDays)
        {
            return new TrialAllotmentValidationResult(
                IsValid: false,
                FailedField: nameof(TrialAllotmentConfig.TrialDurationDays),
                Reason: $"must be 0..{MaxDurationDays}");
        }
        if (trialTutorTurns < 0 || trialTutorTurns > MaxTutorTurns)
        {
            return new TrialAllotmentValidationResult(
                IsValid: false,
                FailedField: nameof(TrialAllotmentConfig.TrialTutorTurns),
                Reason: $"must be 0..{MaxTutorTurns}");
        }
        if (trialPhotoDiagnostics < 0 || trialPhotoDiagnostics > MaxPhotoDiagnostics)
        {
            return new TrialAllotmentValidationResult(
                IsValid: false,
                FailedField: nameof(TrialAllotmentConfig.TrialPhotoDiagnostics),
                Reason: $"must be 0..{MaxPhotoDiagnostics}");
        }
        if (trialPracticeSessions < 0 || trialPracticeSessions > MaxPracticeSessions)
        {
            return new TrialAllotmentValidationResult(
                IsValid: false,
                FailedField: nameof(TrialAllotmentConfig.TrialPracticeSessions),
                Reason: $"must be 0..{MaxPracticeSessions}");
        }
        return new TrialAllotmentValidationResult(IsValid: true, FailedField: null, Reason: null);
    }
}
