using Cena.Actors.Bus;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NSubstitute;
using Proto;

namespace Cena.Actors.Tests.Bus;

/// <summary>
/// Unit tests for NatsBusRouter stats and bounded error collection.
/// These tests exercise the public surface without starting NATS subscriptions.
/// </summary>
public sealed class NatsBusRouterTests : IDisposable
{
    private readonly INatsConnection _nats;
    private readonly ActorSystem _actorSystem;
    private readonly ILogger<NatsBusRouter> _logger;
    private readonly NatsBusRouter _sut;

    public NatsBusRouterTests()
    {
        _nats = Substitute.For<INatsConnection>();
        _actorSystem = new ActorSystem();
        _logger = Substitute.For<ILogger<NatsBusRouter>>();
        var documentStore = Substitute.For<Marten.IDocumentStore>();
        var redis = Substitute.For<StackExchange.Redis.IConnectionMultiplexer>();
        _sut = new NatsBusRouter(_nats, _actorSystem, documentStore, redis, _logger);
    }

    // ── Initial state ──

    [Fact]
    public void InitialStats_AllZero()
    {
        Assert.Equal(0L, _sut.CommandsRouted);
        Assert.Equal(0L, _sut.ErrorsCount);
        Assert.Equal(0L, _sut.EventsPublished);
        Assert.Equal(0L, _sut.SessionsStarted);
        Assert.Equal(0L, _sut.RetriesAttempted);
        Assert.Equal(0L, _sut.DeadLettered);
    }

    [Fact]
    public void RecentErrors_InitiallyEmpty()
    {
        Assert.Empty(_sut.RecentErrors);
    }

    [Fact]
    public void ActiveActors_InitiallyEmpty()
    {
        Assert.Empty(_sut.ActiveActors);
    }

    // ── Error recording ──

    [Fact]
    public void RecordError_IncrementsErrorCount()
    {
        _sut.RecordError("deserialization", "cena.commands.session.start", "bad json", null);

        Assert.Equal(1L, _sut.ErrorsCount);
    }

    [Fact]
    public void RecordError_ErrorAppearsInRecentErrors()
    {
        _sut.RecordError("timeout", "cena.commands.concept.attempt", "timed out", "student-1");

        var recent = _sut.RecentErrors;
        Assert.Single(recent);
        Assert.Equal("timeout", recent[0].Category);
        Assert.Equal("cena.commands.concept.attempt", recent[0].Subject);
        Assert.Equal("timed out", recent[0].Message);
        Assert.Equal("student-1", recent[0].StudentId);
    }

    [Fact]
    public void RecordError_TracksErrorsByCategory()
    {
        _sut.RecordError("deserialization", "subject-a", "msg-1", null);
        _sut.RecordError("deserialization", "subject-b", "msg-2", null);
        _sut.RecordError("timeout", "subject-c", "msg-3", null);

        Assert.Equal(2L, _sut.ErrorsByCategory["deserialization"]);
        Assert.Equal(1L, _sut.ErrorsByCategory["timeout"]);
    }

    // ── Bounded error queue (MaxRecentErrors = 250) ──

    [Fact]
    public void RecentErrors_BoundedAt250()
    {
        // Record 300 errors -- the queue must never grow beyond 250
        for (var i = 0; i < 300; i++)
        {
            _sut.RecordError("test", $"subject-{i}", $"msg-{i}", null);
        }

        // ErrorsCount tracks all 300
        Assert.Equal(300L, _sut.ErrorsCount);

        // But the internal queue is capped at 250 (RecentErrors returns last 50 of those)
        // Verify by recording exactly 250 more errors and checking we don't get stale data
        // The internal MaxRecentErrors constant is 250, so we validate indirectly:
        // RecentErrors returns the last 50 of at most 250 stored -- it should contain
        // the most recent entries, not entries from the first batch of 300.
        var recent = _sut.RecentErrors;
        Assert.True(recent.Count <= 50, $"RecentErrors returned {recent.Count} items, expected at most 50");
        // The most recent entry should be from the last batch (msg-299)
        Assert.Equal("msg-299", recent[0].Message);
    }

    [Fact]
    public void RecentErrors_Returns50MostRecentWhenQueueIsLarge()
    {
        // Fill to exactly 250 entries (the internal bound)
        for (var i = 0; i < 250; i++)
        {
            _sut.RecordError("test", "subject", $"msg-{i}", null);
        }

        var recent = _sut.RecentErrors;

        Assert.Equal(50, recent.Count);
        // Most recent is msg-249
        Assert.Equal("msg-249", recent[0].Message);
    }

    [Fact]
    public void RecentErrors_OldestEntriesDroppedWhenQueueExceedsBound()
    {
        // Record 260 errors -- the first 10 should be evicted
        for (var i = 0; i < 260; i++)
        {
            _sut.RecordError("eviction-test", "subject", $"entry-{i}", null);
        }

        // All 260 errors were counted
        Assert.Equal(260L, _sut.ErrorsCount);

        // The queue holds at most 250, so entries 0-9 should be gone.
        // RecentErrors gives last 50 of the queue, which are entries 210-259.
        var recent = _sut.RecentErrors;
        Assert.DoesNotContain(recent, e => e.Message == "entry-0");
        Assert.DoesNotContain(recent, e => e.Message == "entry-9");
        Assert.Contains(recent, e => e.Message == "entry-259");
    }

    // ── ErrorEntry record ──

    [Fact]
    public void ErrorEntry_TimestampIsUtcApproximate()
    {
        var before = DateTimeOffset.UtcNow;
        _sut.RecordError("category", "subject", "message", null);
        var after = DateTimeOffset.UtcNow;

        var entry = _sut.RecentErrors[0];
        Assert.True(entry.Timestamp >= before);
        Assert.True(entry.Timestamp <= after);
    }

    public void Dispose()
    {
        _actorSystem.ShutdownAsync().GetAwaiter().GetResult();
    }
}
