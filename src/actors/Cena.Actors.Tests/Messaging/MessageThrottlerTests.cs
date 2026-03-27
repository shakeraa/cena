using Cena.Actors.Messaging;

namespace Cena.Actors.Tests.Messaging;

public sealed class MessageThrottlerTests
{
    [Fact]
    public void Teacher_BlockedAfterHourlyLimit()
    {
        // Hourly limit (30) is hit before daily (100) in a burst
        var throttler = new MessageThrottler();

        for (int i = 0; i < 30; i++)
        {
            var result = throttler.Check("teacher-1", MessageRole.Teacher);
            Assert.True(result.Allowed, $"Should be allowed at message {i}");
            throttler.RecordSend("teacher-1", MessageRole.Teacher);
        }

        var blocked = throttler.Check("teacher-1", MessageRole.Teacher);
        Assert.False(blocked.Allowed);
        Assert.True(blocked.RetryAfterSeconds > 0);
    }

    [Fact]
    public void Parent_BlockedAfterHourlyLimit()
    {
        // Hourly limit (5) is hit before daily (10) in a burst
        var throttler = new MessageThrottler();

        for (int i = 0; i < 5; i++)
        {
            Assert.True(throttler.Check("parent-1", MessageRole.Parent).Allowed);
            throttler.RecordSend("parent-1", MessageRole.Parent);
        }

        Assert.False(throttler.Check("parent-1", MessageRole.Parent).Allowed);
    }

    [Fact]
    public void Student_AlwaysBlocked()
    {
        var throttler = new MessageThrottler();
        var result = throttler.Check("student-1", MessageRole.Student);
        Assert.False(result.Allowed);
    }

    [Fact]
    public void System_AlwaysAllowed()
    {
        var throttler = new MessageThrottler();

        for (int i = 0; i < 1000; i++)
        {
            Assert.True(throttler.Check("system", MessageRole.System).Allowed);
            throttler.RecordSend("system", MessageRole.System);
        }
    }

    [Fact]
    public void DifferentUsers_IndependentLimits()
    {
        var throttler = new MessageThrottler();

        // Exhaust parent-1
        for (int i = 0; i < 10; i++)
        {
            throttler.RecordSend("parent-1", MessageRole.Parent);
        }

        // parent-2 should still be allowed
        Assert.True(throttler.Check("parent-2", MessageRole.Parent).Allowed);
    }

    [Fact]
    public void Reset_ClearsUserState()
    {
        var throttler = new MessageThrottler();

        for (int i = 0; i < 10; i++)
            throttler.RecordSend("parent-1", MessageRole.Parent);

        Assert.False(throttler.Check("parent-1", MessageRole.Parent).Allowed);

        throttler.Reset("parent-1");

        Assert.True(throttler.Check("parent-1", MessageRole.Parent).Allowed);
    }

    [Fact]
    public void Teacher_HourlyLimit30()
    {
        var throttler = new MessageThrottler();

        for (int i = 0; i < 30; i++)
        {
            Assert.True(throttler.Check("teacher-burst", MessageRole.Teacher).Allowed);
            throttler.RecordSend("teacher-burst", MessageRole.Teacher);
        }

        var blocked = throttler.Check("teacher-burst", MessageRole.Teacher);
        Assert.False(blocked.Allowed);
    }
}
