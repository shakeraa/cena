// =============================================================================
// Cena Platform -- Outreach & Engagement DTOs
// ADM-014: Re-engagement monitoring and analytics
// =============================================================================

namespace Cena.Api.Contracts.Admin.Outreach;

// Outreach Overview Dashboard
public sealed record OutreachSummaryResponse(
    IReadOnlyList<ChannelStats> ByChannel,
    float BudgetExhaustionRate,
    IReadOnlyList<EngagementMetric> ReEngagementRate,
    int TotalSentToday);

public sealed record ChannelStats(
    string Channel,  // WhatsApp, Telegram, Push, Voice
    int SentToday,
    int SentThisWeek,
    float OpenRate,
    float ClickRate,
    float OptOutRate);

public sealed record EngagementMetric(
    string Period,
    float Rate,
    int SampleSize);

// Channel Effectiveness Analysis
public sealed record ChannelEffectivenessResponse(
    IReadOnlyList<TriggerVolume> VolumeByTrigger,
    IReadOnlyList<ChannelComparison> ChannelComparison,
    IReadOnlyList<MergeEffectiveness> MergeStats,
    IReadOnlyList<OptimalSendTime> SendTimeHeatmap);

public sealed record TriggerVolume(
    string TriggerType,  // disengagement, stagnation, reminder
    int Count,
    IReadOnlyList<VolumePoint> Trend);

public sealed record VolumePoint(
    string Date,
    int Count);

public sealed record ChannelComparison(
    string Channel,
    float ReEngagementRate,
    float AvgTimeToReEngagementMinutes,
    int SampleSize);

public sealed record MergeEffectiveness(
    bool WasMerged,
    float OpenRate,
    float ClickRate);

public sealed record OptimalSendTime(
    string DayOfWeek,
    int Hour,
    float ResponseRate,
    int SampleSize);

// Student Outreach History
public sealed record StudentOutreachHistoryResponse(
    string StudentId,
    IReadOnlyList<OutreachEvent> Events,
    NotificationPreferences Preferences);

public sealed record OutreachEvent(
    string EventId,
    DateTimeOffset SentAt,
    string Channel,
    string TriggerReason,
    string MessagePreview,
    bool Delivered,
    bool Opened,
    bool Clicked,
    DateTimeOffset? ReEngagedAt);

public sealed record NotificationPreferences(
    bool WhatsAppEnabled,
    bool TelegramEnabled,
    bool PushEnabled,
    bool VoiceEnabled,
    DateTimeOffset? UpdatedAt);

public sealed record UpdatePreferencesRequest(
    bool? WhatsAppEnabled,
    bool? TelegramEnabled,
    bool? PushEnabled,
    bool? VoiceEnabled);

// Budget Alerts
public sealed record BudgetAlertResponse(
    bool IsApproachingLimit,
    float CurrentUsagePercent,
    int MessagesRemaining,
    string? AlertMessage);
