// =============================================================================
// Cena Platform — Parent Digest Preferences (prr-051 / EPIC-PRR-C).
//
// Purpose-based opt-in model for parent digests. Every (parent, child) pair
// carries an explicit per-purpose preference:
//
//   weekly_summary          — the Monday digest (prr-051 DEFAULT: Off).
//   homework_reminders      — per-session nudges (DEFAULT: Off).
//   exam_readiness          — ramp-up alerts before a scheduled exam (DEFAULT: Off).
//   accommodations_changes  — when the minor's Section 504 profile changes
//                             (DEFAULT: Off; parents can turn this on to track
//                             support-staff-initiated changes).
//   safety_alerts           — welfare signals (at-risk, sudden disengagement,
//                             parental-consent-breach probes). DEFAULT: On
//                             because the harm of missing a safety alert
//                             is asymmetric; revocable via the unsubscribe
//                             link and the preferences API.
//
// Design rules (from the source persona review + ADR-0003):
//
//   1. GDPR-respectful defaults: four of the five purposes are opt-OUT by
//      default. Safety alerts are opt-IN by default (noisy but necessary).
//      A parent who has never visited the preferences screen receives ONLY
//      safety alerts — no weekly summary, no homework reminders.
//
//   2. Per-(parent, child) granularity. A parent with two minors can opt
//      in for child A and not child B. The aggregate stores ONE record
//      per (parent, child) pair.
//
//   3. Purpose is an open set represented by an enum with an explicit
//      `Unknown` fallback. Adding a new purpose requires bumping the
//      event version (V1 → V2) so schema evolution is observable.
//
//   4. Unsubscribe-all is not a separate state — it's a bulk opt-out
//      across every purpose for a specific (parent, child) pair. The
//      unsubscribe link + /unsubscribe endpoint walk the set of purposes
//      and emit a single `ParentDigestUnsubscribed_V1` event summarising
//      the bulk action; the store materialises that into
//      `ParentDigestPreferencesUpdated_V1` per-purpose rows so the
//      downstream dispatcher sees a uniform shape.
//
//   5. No misconception data ever leaks through preferences. ADR-0003
//      prevents us from storing WHY a parent opted out (retaliation-
//      proof: a "flagged for aggression" parent must look identical to
//      a "test account" parent at the preferences-store level).
// =============================================================================

using System.Collections.Immutable;

namespace Cena.Actors.ParentDigest;

/// <summary>
/// The closed set of purposes a parent can opt in / out of. Renaming or
/// reordering members is a wire-format breaking change (they are persisted
/// by name in <c>ParentDigestPreferencesUpdated_V1</c>). Adding a new
/// purpose requires both: (a) an additive enum member and (b) a ship-gate
/// review for the default state.
/// </summary>
public enum DigestPurpose
{
    /// <summary>
    /// Defensive default for deserialising unknown (future) purposes from
    /// older code reading a newer event. Never emitted by new code.
    /// </summary>
    Unknown = 0,

    /// <summary>Weekly Monday-morning digest of the minor's activity.</summary>
    WeeklySummary = 1,

    /// <summary>Per-session or per-assignment homework-reminder nudges.</summary>
    HomeworkReminders = 2,

    /// <summary>Ramp-up alerts in the days before a scheduled exam.</summary>
    ExamReadiness = 3,

    /// <summary>Notifications when a minor's accommodations profile changes.</summary>
    AccommodationsChanges = 4,

    /// <summary>Welfare / at-risk / consent-breach alerts. Default-on.</summary>
    SafetyAlerts = 5,
}

/// <summary>
/// Per-purpose opt-in status. <see cref="NotSet"/> means the parent has
/// never expressed a preference for this purpose — the default table
/// in <see cref="ParentDigestPreferences"/> decides the effective value.
/// Used by the dispatcher decision; never leaked to API responses
/// (the API normalises NotSet to the effective default).
/// </summary>
public enum OptInStatus
{
    NotSet = 0,
    OptedIn = 1,
    OptedOut = 2,
}

/// <summary>
/// All purposes known to this code version. Exposed so tests and the API
/// endpoint can iterate without hard-coding the set (adding a new member
/// to <see cref="DigestPurpose"/> also requires adding it to
/// <see cref="KnownPurposes"/> — the ParentDigestPreferences unit tests
/// pin the set and fail on drift).
/// </summary>
public static class DigestPurposes
{
    /// <summary>
    /// Ordered, stable list of every purpose a parent can opt in / out of.
    /// Order matches enum member order; downstream callers must NOT depend
    /// on the order for correctness, only for deterministic rendering.
    /// </summary>
    public static readonly ImmutableArray<DigestPurpose> KnownPurposes =
        ImmutableArray.Create(
            DigestPurpose.WeeklySummary,
            DigestPurpose.HomeworkReminders,
            DigestPurpose.ExamReadiness,
            DigestPurpose.AccommodationsChanges,
            DigestPurpose.SafetyAlerts);

    /// <summary>
    /// The default opt-in state a purpose resolves to when the parent has
    /// never expressed a preference. Safety alerts default ON; all others
    /// default OFF per the prr-051 task body.
    /// </summary>
    public static bool DefaultOptedIn(DigestPurpose purpose) => purpose switch
    {
        DigestPurpose.SafetyAlerts => true,
        DigestPurpose.WeeklySummary => false,
        DigestPurpose.HomeworkReminders => false,
        DigestPurpose.ExamReadiness => false,
        DigestPurpose.AccommodationsChanges => false,
        // A future purpose, not yet known to this code version: conservative
        // default is OFF (don't send things we don't know the meaning of).
        _ => false,
    };

    /// <summary>
    /// Stable wire-format string for a purpose. Persisted in the event
    /// stream and surfaced to the API. Renaming requires a V2 upcast.
    /// </summary>
    public static string ToWire(DigestPurpose purpose) => purpose switch
    {
        DigestPurpose.WeeklySummary => "weekly_summary",
        DigestPurpose.HomeworkReminders => "homework_reminders",
        DigestPurpose.ExamReadiness => "exam_readiness",
        DigestPurpose.AccommodationsChanges => "accommodations_changes",
        DigestPurpose.SafetyAlerts => "safety_alerts",
        _ => "unknown",
    };

    /// <summary>
    /// Inverse of <see cref="ToWire"/>. Unknown wire strings resolve to
    /// <see cref="DigestPurpose.Unknown"/> so a client typo produces a
    /// Bad-Request rather than silently overwriting a known purpose.
    /// </summary>
    public static bool TryParseWire(string wire, out DigestPurpose purpose)
    {
        purpose = DigestPurpose.Unknown;
        if (string.IsNullOrWhiteSpace(wire)) return false;
        switch (wire.Trim().ToLowerInvariant())
        {
            case "weekly_summary": purpose = DigestPurpose.WeeklySummary; return true;
            case "homework_reminders": purpose = DigestPurpose.HomeworkReminders; return true;
            case "exam_readiness": purpose = DigestPurpose.ExamReadiness; return true;
            case "accommodations_changes": purpose = DigestPurpose.AccommodationsChanges; return true;
            case "safety_alerts": purpose = DigestPurpose.SafetyAlerts; return true;
            default: return false;
        }
    }
}

/// <summary>
/// Immutable per-(parent, child) preferences record. Stored and retrieved
/// by <see cref="IParentDigestPreferencesStore"/>; composed by the
/// dispatcher decision before sending a digest.
/// </summary>
/// <param name="ParentActorId">Opaque parent anon id.</param>
/// <param name="StudentSubjectId">Opaque student anon id.</param>
/// <param name="InstituteId">
/// Tenant the pair lives in (ADR-0001). Preferences for the same parent in
/// institute A are not visible to the same parent's session at institute B.
/// </param>
/// <param name="PurposeStatuses">
/// Map of purpose → explicit opt-in state. A purpose missing from the map
/// resolves to <see cref="OptInStatus.NotSet"/> (the default-table applies).
/// </param>
/// <param name="UpdatedAtUtc">Wall clock of the last write.</param>
/// <param name="UnsubscribedAtUtc">
/// Non-null when the parent used the one-click unsubscribe link. Emitted
/// so auditors can tell bulk unsubscribe from per-purpose edits.
/// </param>
public sealed record ParentDigestPreferences(
    string ParentActorId,
    string StudentSubjectId,
    string InstituteId,
    ImmutableDictionary<DigestPurpose, OptInStatus> PurposeStatuses,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? UnsubscribedAtUtc = null)
{
    /// <summary>
    /// Fresh preferences for a pair that has never visited the screen.
    /// Every purpose is <see cref="OptInStatus.NotSet"/>; the default-
    /// table drives the effective answer. Used as the baseline in tests
    /// and in the API's GET handler when no row exists.
    /// </summary>
    public static ParentDigestPreferences Empty(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        DateTimeOffset nowUtc)
        => new(
            parentActorId,
            studentSubjectId,
            instituteId,
            ImmutableDictionary<DigestPurpose, OptInStatus>.Empty,
            nowUtc,
            UnsubscribedAtUtc: null);

    /// <summary>
    /// Resolve the effective opt-in status for a purpose: the stored
    /// status if set; else the ship-wide default. The dispatcher calls
    /// <see cref="ShouldSend"/> instead; this accessor is for the API
    /// response shape.
    /// </summary>
    public OptInStatus EffectiveStatus(DigestPurpose purpose)
    {
        if (PurposeStatuses.TryGetValue(purpose, out var explicitStatus) &&
            explicitStatus != OptInStatus.NotSet)
        {
            return explicitStatus;
        }
        return DigestPurposes.DefaultOptedIn(purpose)
            ? OptInStatus.OptedIn
            : OptInStatus.OptedOut;
    }

    /// <summary>
    /// Pure predicate for the dispatcher: may we send a digest for this
    /// purpose right now? True only when the effective status is
    /// <see cref="OptInStatus.OptedIn"/>. A purpose that resolves to
    /// <see cref="OptInStatus.NotSet"/> (in theory impossible after the
    /// default-table pass) falls through to false — fail-closed.
    /// </summary>
    public bool ShouldSend(DigestPurpose purpose)
        => EffectiveStatus(purpose) == OptInStatus.OptedIn;

    /// <summary>
    /// Produce a new preferences record with the supplied per-purpose
    /// updates applied. Purposes not in the update dictionary are left
    /// unchanged. Unknown purposes are rejected by the API layer; they
    /// never reach this method.
    /// </summary>
    public ParentDigestPreferences WithUpdates(
        IReadOnlyDictionary<DigestPurpose, OptInStatus> updates,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(updates);
        var next = PurposeStatuses;
        foreach (var (purpose, status) in updates)
        {
            if (purpose == DigestPurpose.Unknown) continue;
            next = next.SetItem(purpose, status);
        }
        return this with
        {
            PurposeStatuses = next,
            UpdatedAtUtc = updatedAtUtc,
            // A targeted edit does NOT clear the unsubscribe stamp —
            // the parent may opt back in one purpose at a time after a
            // bulk unsubscribe; the stamp stays for the audit trail.
        };
    }

    /// <summary>
    /// Produce a new preferences record with every known purpose set to
    /// <see cref="OptInStatus.OptedOut"/> — the effect of the one-click
    /// unsubscribe link. Safety alerts are also opted out: the task body
    /// says "Safety-alerts ships defaulted-ON (revocable but noisy)";
    /// the one-click link is exactly that revocation.
    /// </summary>
    public ParentDigestPreferences AsFullyUnsubscribed(DateTimeOffset updatedAtUtc)
    {
        var next = PurposeStatuses;
        foreach (var purpose in DigestPurposes.KnownPurposes)
        {
            next = next.SetItem(purpose, OptInStatus.OptedOut);
        }
        return this with
        {
            PurposeStatuses = next,
            UpdatedAtUtc = updatedAtUtc,
            UnsubscribedAtUtc = updatedAtUtc,
        };
    }
}
