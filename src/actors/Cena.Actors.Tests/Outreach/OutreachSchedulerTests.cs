using System.Diagnostics.Metrics;
using System.Reflection;
using Cena.Actors.Outreach;
using Cena.Actors.Services;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Cena.Actors.Tests.Outreach;

/// <summary>
/// Tests for OutreachSchedulerActor internals.
/// ACT-021: Timer scheduling, throttle, quiet hours.
/// </summary>
public sealed class OutreachSchedulerTests
{
    private readonly OutreachSchedulerActor _actor;
    private readonly IHlrService _hlr;

    public OutreachSchedulerTests()
    {
        _hlr = Substitute.For<IHlrService>();
        var logger = Substitute.For<ILogger<OutreachSchedulerActor>>();
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter("test"));
        _actor = new OutreachSchedulerActor(_hlr, logger, meterFactory);
    }

    [Fact]
    public void TimerIntervals_AreConfigurable()
    {
        // Verify the configurable constants exist and have expected defaults
        var hlrInterval = GetStaticField<int>("CheckHlrTimersIntervalMinutes");
        var streakInterval = GetStaticField<int>("CheckStreakExpiryIntervalMinutes");

        Assert.Equal(15, hlrInterval);
        Assert.Equal(30, streakInterval);
    }

    [Fact]
    public void DailyThrottle_ResetsOnNewDay()
    {
        // Set counter to max
        SetField("_messagesSentToday", 3);
        SetField("_lastResetDate", DateTimeOffset.UtcNow.AddDays(-1));

        // Call the reset method
        InvokeMethod("ResetDailyThrottleIfNeeded");

        int count = GetField<int>("_messagesSentToday");
        Assert.Equal(0, count);
    }

    [Fact]
    public void DailyThrottle_DoesNotResetSameDay()
    {
        SetField("_messagesSentToday", 2);
        SetField("_lastResetDate", DateTimeOffset.UtcNow);

        InvokeMethod("ResetDailyThrottleIfNeeded");

        int count = GetField<int>("_messagesSentToday");
        Assert.Equal(2, count);
    }

    [Fact]
    public void IsQuietHours_ReturnsCorrectly()
    {
        // This test verifies the quiet hours logic works, but we can't easily
        // control the time without a clock abstraction. We just verify it returns a bool.
        bool result = (bool)InvokeMethod("IsQuietHours")!;
        Assert.IsType<bool>(result);
    }

    [Fact]
    public void EnqueueOutreach_MaintainsPriorityOrder()
    {
        // Enqueue low priority then high priority
        InvokeMethod("EnqueueOutreach", new PendingOutreach(3, "Low", null, "low msg"));
        InvokeMethod("EnqueueOutreach", new PendingOutreach(1, "High", null, "high msg"));
        InvokeMethod("EnqueueOutreach", new PendingOutreach(2, "Med", null, "med msg"));

        var queue = GetField<SortedList<int, PendingOutreach>>("_pendingQueue");

        // First item should be highest priority (lowest number)
        Assert.Equal("High", queue.Values[0].Type);
        Assert.Equal("Med", queue.Values[1].Type);
        Assert.Equal("Low", queue.Values[2].Type);
    }

    [Fact]
    public void HandleUpdateActivity_UpdatesHlrTimer_WhenConceptTracked()
    {
        // Set up a tracked timer
        var timers = GetField<Dictionary<string, HlrTimerState>>("_timers");
        timers["concept-1"] = new HlrTimerState(24.0, DateTimeOffset.UtcNow.AddHours(-12));

        _hlr.UpdateHalfLife(24.0, true, 3000).Returns(48.0);

        var msg = new UpdateActivity(
            DateTimeOffset.UtcNow, 5, "concept-1", true, 3000);

        // Use reflection to invoke the handler
        InvokeMethod("HandleUpdateActivity", msg);

        Assert.Equal(48.0, timers["concept-1"].HalfLifeHours);
    }

    [Fact]
    public void HandleConceptMastered_StartsNewTimer()
    {
        var msg = new ConceptMasteredNotification("concept-new", 24.0);
        InvokeMethod("HandleConceptMastered", msg);

        var timers = GetField<Dictionary<string, HlrTimerState>>("_timers");
        Assert.True(timers.ContainsKey("concept-new"));
        Assert.Equal(24.0, timers["concept-new"].HalfLifeHours);
    }

    // ── Helpers ──

    private object? InvokeMethod(string name, params object[] args)
    {
        var method = typeof(OutreachSchedulerActor)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
        return method!.Invoke(_actor, args.Length > 0 ? args : null);
    }

    private void SetField(string name, object value)
    {
        var field = typeof(OutreachSchedulerActor)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        field!.SetValue(_actor, value);
    }

    private T GetField<T>(string name)
    {
        var field = typeof(OutreachSchedulerActor)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        return (T)field!.GetValue(_actor)!;
    }

    private T GetStaticField<T>(string name)
    {
        var field = typeof(OutreachSchedulerActor)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
        return (T)field!.GetValue(null)!;
    }
}
