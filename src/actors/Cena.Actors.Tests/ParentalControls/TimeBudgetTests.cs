// =============================================================================
// RDY-077 Phase 1A — TimeBudget + ParentalControlSettings tests.
// Proves the soft-cap + no-hard-block invariants.
// =============================================================================

using System.Collections.Immutable;
using Cena.Actors.ParentalControls;
using Xunit;

namespace Cena.Actors.Tests.ParentalControls;

public class TimeBudgetTests
{
    private static TimeBudget Budget(int weekly) => new(
        StudentAnonId: "stu-1",
        WeeklyMinutes: weekly,
        ConfiguredAtUtc: DateTimeOffset.UtcNow,
        ConfiguredBy: "parent-hmac");

    [Fact]
    public void Zero_budget_is_not_configured()
    {
        Assert.False(Budget(0).IsConfigured);
    }

    [Fact]
    public void Positive_budget_is_configured()
    {
        Assert.True(Budget(300).IsConfigured);
    }

    [Fact]
    public void Not_configured_never_reports_over_budget()
    {
        Assert.False(Budget(0).IsOverBudget(9999));
    }

    [Theory]
    [InlineData(300, 301, true)]
    [InlineData(300, 299, false)]
    [InlineData(300, 300, false)]  // at cap is NOT over (boundary)
    public void IsOverBudget_compares_strictly(int weekly, int used, bool expected)
    {
        Assert.Equal(expected, Budget(weekly).IsOverBudget(used));
    }

    [Theory]
    [InlineData(300, 150, 0.5)]
    [InlineData(300, 300, 1.0)]
    [InlineData(300, 450, 1.5)]
    public void Usage_ratio_is_unbounded(int weekly, int used, double expected)
    {
        Assert.Equal(expected, Budget(weekly).UsageRatio(used), precision: 2);
    }
}

public class TimeOfDayRestrictionTests
{
    [Fact]
    public void Inside_window_returns_false()
    {
        var r = new TimeOfDayRestriction(
            NotBefore: new TimeOnly(8, 0),
            NotAfter: new TimeOnly(21, 0),
            Timezone: "Asia/Jerusalem");
        Assert.False(r.IsOutsideWindow(new TimeOnly(15, 0)));
    }

    [Fact]
    public void Before_window_returns_true()
    {
        var r = new TimeOfDayRestriction(
            NotBefore: new TimeOnly(8, 0),
            NotAfter: new TimeOnly(21, 0),
            Timezone: "Asia/Jerusalem");
        Assert.True(r.IsOutsideWindow(new TimeOnly(7, 30)));
    }

    [Fact]
    public void After_window_returns_true()
    {
        var r = new TimeOfDayRestriction(
            NotBefore: new TimeOnly(8, 0),
            NotAfter: new TimeOnly(21, 0),
            Timezone: "Asia/Jerusalem");
        Assert.True(r.IsOutsideWindow(new TimeOnly(21, 30)));
    }
}

public class ParentalControlSettingsTests
{
    [Fact]
    public void None_has_no_configured_controls()
    {
        var s = ParentalControlSettings.None("stu-1");
        Assert.False(s.IsAnyControlConfigured);
    }

    [Fact]
    public void Empty_topic_allowlist_permits_any_topic()
    {
        var s = ParentalControlSettings.None("stu-1");
        Assert.True(s.IsTopicAllowed("derivatives"));
        Assert.True(s.IsTopicAllowed("literally-anything"));
    }

    [Fact]
    public void Non_empty_allowlist_gates_to_listed_topics_only()
    {
        var s = new ParentalControlSettings(
            StudentAnonId: "stu-1",
            WeeklyBudget: null,
            TimeOfDayRestriction: null,
            TopicAllowList: ImmutableArray.Create(
                new AllowedTopic("algebra-review"),
                new AllowedTopic("derivatives")),
            ConfiguredAtUtc: DateTimeOffset.UtcNow);
        Assert.True(s.IsTopicAllowed("derivatives"));
        Assert.True(s.IsTopicAllowed("algebra-review"));
        Assert.False(s.IsTopicAllowed("integrals"));
    }

    [Fact]
    public void IsAnyControlConfigured_true_when_budget_set()
    {
        var s = ParentalControlSettings.None("stu-1") with
        {
            WeeklyBudget = new TimeBudget("stu-1", 300, DateTimeOffset.UtcNow, "parent"),
        };
        Assert.True(s.IsAnyControlConfigured);
    }

    [Fact]
    public void IsAnyControlConfigured_true_when_time_restriction_set()
    {
        var s = ParentalControlSettings.None("stu-1") with
        {
            TimeOfDayRestriction = new TimeOfDayRestriction(
                new TimeOnly(8, 0), new TimeOnly(21, 0), "Asia/Jerusalem"),
        };
        Assert.True(s.IsAnyControlConfigured);
    }
}
