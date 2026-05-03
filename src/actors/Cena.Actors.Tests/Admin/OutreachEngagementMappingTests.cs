// =============================================================================
// Cena Platform -- OutreachEngagementService mapping tests (ADM-018 hardening)
// Tests for the pure static mapping helpers — no Marten, no DB.
// =============================================================================

using Cena.Admin.Api;
using Cena.Infrastructure.Documents;

namespace Cena.Actors.Tests.Admin;

public sealed class OutreachEngagementMappingTests
{
    private static readonly DateTimeOffset Today = new(new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void BuildSummary_EmptyEvents_ReturnsZeroChannelStats()
    {
        var summary = OutreachEngagementService.BuildSummary(
            Array.Empty<OutreachEventDocument>(), Today, budget: null);
        Assert.Empty(summary.ByChannel);
        Assert.Equal(0f, summary.BudgetExhaustionRate);
        Assert.Equal(0, summary.TotalSentToday);
        Assert.Equal(7, summary.ReEngagementRate.Count);
    }

    [Fact]
    public void BuildSummary_GroupsByChannel_AndComputesRates()
    {
        var events = new[]
        {
            MakeEvent("WhatsApp", Today, delivered: true, opened: true, clicked: true),
            MakeEvent("WhatsApp", Today, delivered: true, opened: true, clicked: false),
            MakeEvent("WhatsApp", Today, delivered: true, opened: false, clicked: false),
            MakeEvent("Push", Today.AddDays(-2), delivered: true, opened: false, clicked: false),
            MakeEvent("Push", Today.AddDays(-2), delivered: false, opened: false, clicked: false, optedOut: true),
        };

        var summary = OutreachEngagementService.BuildSummary(events, Today, budget: null);

        Assert.Equal(2, summary.ByChannel.Count);
        var whatsapp = summary.ByChannel.First(c => c.Channel == "WhatsApp");
        Assert.Equal(3, whatsapp.SentToday);
        Assert.Equal(3, whatsapp.SentThisWeek);
        // 2 opened out of 3 delivered → 0.667
        Assert.InRange(whatsapp.OpenRate, 0.66f, 0.67f);
        // 1 clicked out of 3 delivered → 0.333
        Assert.InRange(whatsapp.ClickRate, 0.33f, 0.34f);

        var push = summary.ByChannel.First(c => c.Channel == "Push");
        Assert.Equal(0, push.SentToday);
        Assert.Equal(2, push.SentThisWeek);
        // 1 opted out of 2 sent → 0.5
        Assert.Equal(0.5f, push.OptOutRate);
    }

    [Fact]
    public void BuildSummary_AppliesBudgetExhaustionRate()
    {
        var budget = new OutreachBudgetDocument
        {
            SchoolId = "dev-school",
            DailyBudget = 100,
            UsedToday = 42,
            BudgetDate = Today,
        };
        var summary = OutreachEngagementService.BuildSummary(
            Array.Empty<OutreachEventDocument>(), Today, budget);
        Assert.Equal(0.42f, summary.BudgetExhaustionRate);
    }

    [Fact]
    public void BuildChannelEffectiveness_GroupsByTrigger_AndBuildsTrend()
    {
        var events = new[]
        {
            MakeEvent("WhatsApp", Today.AddDays(-2), trigger: "disengagement"),
            MakeEvent("Push", Today.AddDays(-1), trigger: "disengagement"),
            MakeEvent("WhatsApp", Today, trigger: "reminder"),
        };

        var response = OutreachEngagementService.BuildChannelEffectiveness(events, Today);

        Assert.Equal(2, response.VolumeByTrigger.Count);
        var dis = response.VolumeByTrigger.First(t => t.TriggerType == "disengagement");
        Assert.Equal(2, dis.Count);
        Assert.Equal(7, dis.Trend.Count);
        Assert.Equal(2, response.ChannelComparison.Count);
    }

    [Fact]
    public void BuildChannelEffectiveness_ComputesReEngagementRate()
    {
        var events = new[]
        {
            MakeEvent("WhatsApp", Today.AddDays(-1), delivered: true, opened: true, clicked: true, reEngaged: true),
            MakeEvent("WhatsApp", Today.AddDays(-1), delivered: true, opened: false, clicked: false),
        };

        var response = OutreachEngagementService.BuildChannelEffectiveness(events, Today);

        var whatsapp = response.ChannelComparison.First();
        Assert.Equal(0.5f, whatsapp.ReEngagementRate);
        Assert.Equal(2, whatsapp.SampleSize);
    }

    [Fact]
    public void BuildStudentHistory_ProjectsEventsInDescendingOrder()
    {
        var events = new[]
        {
            MakeEvent("WhatsApp", Today.AddDays(-5)),
            MakeEvent("Push", Today.AddDays(-1)),
            MakeEvent("Telegram", Today),
        };

        var response = OutreachEngagementService.BuildStudentHistory("stu-a", events, prefs: null);

        Assert.Equal("stu-a", response.StudentId);
        Assert.Equal(3, response.Events.Count);
        Assert.Equal("Telegram", response.Events[0].Channel);
        Assert.Equal("WhatsApp", response.Events[2].Channel);
        Assert.False(response.Preferences.WhatsAppEnabled); // null prefs → defaults false
    }

    [Fact]
    public void BuildStudentHistory_AppliesPrefsDocument()
    {
        var prefs = new StudentNotificationPreferencesDocument
        {
            Id = "notif-prefs:stu-a",
            StudentId = "stu-a",
            WhatsAppEnabled = true,
            TelegramEnabled = false,
            PushEnabled = true,
            VoiceEnabled = true,
            UpdatedAt = Today,
        };

        var response = OutreachEngagementService.BuildStudentHistory(
            "stu-a", new[] { MakeEvent("WhatsApp", Today) }, prefs);

        Assert.True(response.Preferences.WhatsAppEnabled);
        Assert.False(response.Preferences.TelegramEnabled);
        Assert.True(response.Preferences.PushEnabled);
        Assert.True(response.Preferences.VoiceEnabled);
    }

    [Fact]
    public void BuildBudgetAlert_NullBudget_ReturnsNoAlert()
    {
        var alert = OutreachEngagementService.BuildBudgetAlert(null);
        Assert.False(alert.IsApproachingLimit);
        Assert.Equal(0f, alert.CurrentUsagePercent);
        Assert.Null(alert.AlertMessage);
    }

    [Fact]
    public void BuildBudgetAlert_TriggersAlertAboveEightyPercent()
    {
        var budget = new OutreachBudgetDocument
        {
            SchoolId = "dev-school",
            DailyBudget = 1000,
            UsedToday = 850,
        };
        var alert = OutreachEngagementService.BuildBudgetAlert(budget);
        Assert.True(alert.IsApproachingLimit);
        Assert.Equal(0.85f, alert.CurrentUsagePercent);
        Assert.Equal(150, alert.MessagesRemaining);
        Assert.NotNull(alert.AlertMessage);
    }

    [Fact]
    public void BuildBudgetAlert_BelowThreshold_NoAlert()
    {
        var budget = new OutreachBudgetDocument
        {
            SchoolId = "dev-school",
            DailyBudget = 1000,
            UsedToday = 300,
        };
        var alert = OutreachEngagementService.BuildBudgetAlert(budget);
        Assert.False(alert.IsApproachingLimit);
        Assert.Equal(0.3f, alert.CurrentUsagePercent);
        Assert.Null(alert.AlertMessage);
    }

    // ── helpers ──
    private static OutreachEventDocument MakeEvent(
        string channel,
        DateTimeOffset sentAt,
        bool delivered = true,
        bool opened = false,
        bool clicked = false,
        bool reEngaged = false,
        bool optedOut = false,
        bool wasMerged = false,
        string trigger = "disengagement") => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        StudentId = "stu-a",
        SchoolId = "dev-school",
        Channel = channel,
        TriggerReason = trigger,
        MessagePreview = "test",
        SentAt = sentAt,
        Delivered = delivered,
        Opened = opened,
        Clicked = clicked,
        OptedOut = optedOut,
        WasMerged = wasMerged,
        ReEngagedAt = reEngaged ? sentAt.AddHours(1) : null,
    };
}
