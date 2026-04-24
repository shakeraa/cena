// =============================================================================
// Cena Platform — Parent-controlled Time Budget (RDY-077 Phase 1A)
//
// Parents can set a weekly time budget, time-of-day restrictions, and
// a topic allow-list for minor-age students. The platform enforces
// these as SOFT CAPS — the student is never locked out; continuing
// past a cap is logged for the parent but never blocked.
//
// Dr. Nadia's SDT concern (Deci & Ryan): external motivation can
// crowd out intrinsic motivation. The domain here treats every control
// as advisory; lockouts + hard-cap scarcity framing + countdown-to-red
// timers are explicitly forbidden by the shipgate scanner (see
// scripts/shipgate/scan.mjs RDY-077 patterns).
//
// Phase 1A scope: domain types only. The Vue gauge + parent-console
// endpoint are Phase 1B, gated on wireframe + dark-pattern review.
// =============================================================================

using System.Collections.Immutable;

namespace Cena.Actors.ParentalControls;

/// <summary>
/// Weekly time budget in minutes. Zero means "no budget configured"
/// (default). A configured budget never blocks — it only informs.
/// </summary>
public sealed record TimeBudget(
    string StudentAnonId,
    int WeeklyMinutes,
    DateTimeOffset ConfiguredAtUtc,
    string ConfiguredBy)
{
    public bool IsConfigured => WeeklyMinutes > 0;

    /// <summary>
    /// True when the student has used MORE than the weekly budget.
    /// Returns false when the budget is not configured; the caller
    /// shows no gauge in that case.
    /// </summary>
    public bool IsOverBudget(int minutesUsedThisWeek)
        => IsConfigured && minutesUsedThisWeek > WeeklyMinutes;

    /// <summary>
    /// Progress as a 0..∞ ratio. Callers render a calm gauge; never
    /// a red countdown or FOMO-framed "X minutes left" string.
    /// </summary>
    public double UsageRatio(int minutesUsedThisWeek)
        => IsConfigured ? (double)minutesUsedThisWeek / WeeklyMinutes : 0.0;
}

/// <summary>
/// A time-of-day restriction window. "No study after 21:00" is the
/// canonical example; represented as a forbidden window start/end in
/// the student's tenant-local timezone.
/// </summary>
public sealed record TimeOfDayRestriction(
    TimeOnly NotBefore,
    TimeOnly NotAfter,
    string Timezone)
{
    /// <summary>
    /// True when <paramref name="localTime"/> falls OUTSIDE the
    /// permitted window. Callers use this to show a gentle "your
    /// family agreed on quiet hours" banner — never a hard block.
    /// </summary>
    public bool IsOutsideWindow(TimeOnly localTime)
        => localTime < NotBefore || localTime > NotAfter;
}

/// <summary>
/// Topic allow-list entry. Allow-lists are additive: when the list is
/// empty, ALL topics are allowed; when non-empty, only the listed
/// topics are permitted during gated sessions.
/// </summary>
public sealed record AllowedTopic(string TopicSlug);

/// <summary>
/// Immutable aggregate of a student's parental-control settings.
/// Zero-arg default means "no constraints configured" — the student
/// experience is unchanged from the no-controls baseline.
/// </summary>
public sealed record ParentalControlSettings(
    string StudentAnonId,
    TimeBudget? WeeklyBudget,
    TimeOfDayRestriction? TimeOfDayRestriction,
    ImmutableArray<AllowedTopic> TopicAllowList,
    DateTimeOffset ConfiguredAtUtc)
{
    public static ParentalControlSettings None(string studentAnonId) =>
        new(
            StudentAnonId: studentAnonId,
            WeeklyBudget: null,
            TimeOfDayRestriction: null,
            TopicAllowList: ImmutableArray<AllowedTopic>.Empty,
            ConfiguredAtUtc: DateTimeOffset.UtcNow);

    public bool IsTopicAllowed(string topicSlug)
        => TopicAllowList.IsDefaultOrEmpty
           || TopicAllowList.Any(t => t.TopicSlug == topicSlug);

    public bool IsAnyControlConfigured
        => WeeklyBudget is not null
           || TimeOfDayRestriction is not null
           || (!TopicAllowList.IsDefaultOrEmpty && TopicAllowList.Length > 0);
}

/// <summary>
/// Event emitted on the student stream when a parent adjusts controls.
/// Carries the consent signature so the audit trail captures which
/// parent identity set the control + when.
/// </summary>
public sealed record ParentalControlsConfiguredV1(
    string StudentAnonId,
    int? WeeklyBudgetMinutes,
    TimeOnly? NotBefore,
    TimeOnly? NotAfter,
    string? Timezone,
    IReadOnlyCollection<string> TopicAllowList,
    string ParentAnonId,
    string ConsentSignature,
    DateTimeOffset ConfiguredAtUtc);

/// <summary>
/// Soft-cap decision returned to the session pipeline. Carries a
/// bucket indicating which advisory banner (if any) to show; never
/// indicates a hard block.
/// </summary>
public sealed record SoftCapDecision(
    SoftCapBanner Banner,
    int? MinutesOverBudget,
    string? RestrictionNote);

/// <summary>
/// Calm advisory buckets. No FOMO, no scarcity, no red timers — the
/// copy rendered for each bucket is in the student's locale and stays
/// neutral.
/// </summary>
public enum SoftCapBanner
{
    /// <summary>No banner. Default state.</summary>
    None = 0,

    /// <summary>
    /// Student has used their weekly budget. Show the gauge + calm
    /// "you and your parent agreed on X hours" copy; offer to stop or
    /// continue. Continuation is logged for the parent, not blocked.
    /// </summary>
    BudgetReached = 1,

    /// <summary>
    /// Current local time is outside the family's agreed-on quiet
    /// window. Show the "your family agreed on quiet hours" banner.
    /// Continuation logged, not blocked.
    /// </summary>
    OutsideQuietHours = 2,

    /// <summary>
    /// Student requested a topic outside the parent's allow-list.
    /// Show a compact "this topic isn't on this week's plan" banner
    /// + provide allowed alternatives. Never frame as punishment.
    /// </summary>
    TopicNotOnPlan = 3
}
