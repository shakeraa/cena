// =============================================================================
// Cena Platform — ParentDigestDispatchDecision (prr-051).
//
// Pure function the digest dispatcher consults before sending ANY digest
// email or SMS. Takes a preferences snapshot and a target purpose; returns
// whether to send + the reason when skipping (for the
// `cena_parent_digest_skipped_total{purpose,reason}` metric).
//
// The function is deliberately simple — no I/O, no locale knowledge, no
// template coupling. All complexity lives in the preferences aggregate.
// The NoParentDigestBypassesPreferencesTest architecture ratchet enforces
// that EVERY digest-dispatch code path consults this function (or the
// preferences aggregate directly) before the sender fan-out.
// =============================================================================

namespace Cena.Actors.ParentDigest;

/// <summary>
/// Result of a dispatch decision. <see cref="ShouldDispatch"/> is the only
/// value the sender consumes; <see cref="SkipReason"/> exists so the
/// telemetry label is a closed enum, not a free-form string.
/// </summary>
public sealed record ParentDigestDispatchDecision(
    bool ShouldDispatch,
    DigestPurpose Purpose,
    ParentDigestSkipReason SkipReason);

/// <summary>
/// The reason the dispatcher skipped a send. Encoded as an enum so the
/// metric label cardinality stays tiny (Prometheus memory per label value).
/// </summary>
public enum ParentDigestSkipReason
{
    /// <summary>Did not skip — <see cref="ParentDigestDispatchDecision.ShouldDispatch"/> is true.</summary>
    None = 0,

    /// <summary>Parent explicitly opted out of this purpose.</summary>
    OptedOut = 1,

    /// <summary>
    /// The purpose resolved to the default-opt-out state (parent has
    /// never visited the preferences screen AND default is OFF).
    /// </summary>
    DefaultOptedOut = 2,

    /// <summary>Parent used the one-click unsubscribe link.</summary>
    Unsubscribed = 3,

    /// <summary>
    /// Caller passed <see cref="DigestPurpose.Unknown"/> — a coding error
    /// that the metric will surface without crashing the pipeline.
    /// </summary>
    UnknownPurpose = 4,

    /// <summary>No preferences row AND default resolution said no.</summary>
    NoPreferencesNoDefault = 5,
}

/// <summary>
/// Pure-function seam the dispatcher calls before every fan-out.
/// </summary>
public static class ParentDigestDispatchPolicy
{
    /// <summary>
    /// Decide whether a digest with <paramref name="purpose"/> may be sent
    /// for the pair represented by <paramref name="preferences"/>.
    /// <paramref name="preferences"/> may be null — the caller passes null
    /// when the store had no row for the pair, and the default-table
    /// decides the answer.
    /// </summary>
    public static ParentDigestDispatchDecision Decide(
        ParentDigestPreferences? preferences,
        DigestPurpose purpose)
    {
        if (purpose == DigestPurpose.Unknown)
        {
            return new ParentDigestDispatchDecision(
                ShouldDispatch: false,
                Purpose: purpose,
                SkipReason: ParentDigestSkipReason.UnknownPurpose);
        }

        if (preferences is null)
        {
            // No row yet — ship-wide default decides.
            if (DigestPurposes.DefaultOptedIn(purpose))
            {
                return new ParentDigestDispatchDecision(
                    ShouldDispatch: true,
                    Purpose: purpose,
                    SkipReason: ParentDigestSkipReason.None);
            }
            return new ParentDigestDispatchDecision(
                ShouldDispatch: false,
                Purpose: purpose,
                SkipReason: ParentDigestSkipReason.NoPreferencesNoDefault);
        }

        // A row exists. Unsubscribed stamp takes priority so the reason
        // label is accurate for analytics ("how many sends did
        // unsubscribe block?").
        if (preferences.UnsubscribedAtUtc is not null)
        {
            return new ParentDigestDispatchDecision(
                ShouldDispatch: false,
                Purpose: purpose,
                SkipReason: ParentDigestSkipReason.Unsubscribed);
        }

        // Explicit per-purpose statuses win over the default table.
        if (preferences.PurposeStatuses.TryGetValue(purpose, out var explicitStatus))
        {
            if (explicitStatus == OptInStatus.OptedIn)
            {
                return new ParentDigestDispatchDecision(
                    ShouldDispatch: true,
                    Purpose: purpose,
                    SkipReason: ParentDigestSkipReason.None);
            }
            if (explicitStatus == OptInStatus.OptedOut)
            {
                return new ParentDigestDispatchDecision(
                    ShouldDispatch: false,
                    Purpose: purpose,
                    SkipReason: ParentDigestSkipReason.OptedOut);
            }
            // NotSet falls through to the default-table below.
        }

        var defaulted = DigestPurposes.DefaultOptedIn(purpose);
        return new ParentDigestDispatchDecision(
            ShouldDispatch: defaulted,
            Purpose: purpose,
            SkipReason: defaulted
                ? ParentDigestSkipReason.None
                : ParentDigestSkipReason.DefaultOptedOut);
    }

    /// <summary>
    /// Stable wire-format string for the metric label.
    /// </summary>
    public static string ToMetricLabel(ParentDigestSkipReason reason) => reason switch
    {
        ParentDigestSkipReason.None => "none",
        ParentDigestSkipReason.OptedOut => "opted_out",
        ParentDigestSkipReason.DefaultOptedOut => "default_opted_out",
        ParentDigestSkipReason.Unsubscribed => "unsubscribed",
        ParentDigestSkipReason.UnknownPurpose => "unknown_purpose",
        ParentDigestSkipReason.NoPreferencesNoDefault => "no_preferences_no_default",
        _ => "unknown",
    };
}
