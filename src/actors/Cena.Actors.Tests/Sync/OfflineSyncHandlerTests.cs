using Cena.Actors.Students;
using Cena.Actors.Sync;
using Microsoft.Extensions.Logging;
using NSubstitute;
using StackExchange.Redis;

namespace Cena.Actors.Tests.Sync;

/// <summary>
/// Tests for OfflineSyncHandler three-tier classification.
/// ACT-022: Verifies weight-based acceptance logic.
/// </summary>
public sealed class OfflineSyncHandlerTests
{
    private readonly OfflineSyncHandler _sut;
    private readonly IDatabase _db;

    public OfflineSyncHandlerTests()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        _db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(_db);
        var logger = Substitute.For<ILogger<OfflineSyncHandler>>();
        _sut = new OfflineSyncHandler(redis, logger);
    }

    private void SetupRedisSetNx(bool returnValue)
    {
        // Exact 4-param overload: StringSetAsync(RedisKey, RedisValue, TimeSpan?, When)
        _db.StringSetAsync(
            Arg.Any<RedisKey>(), Arg.Any<RedisValue>(),
            Arg.Any<TimeSpan?>(), Arg.Any<When>())
            .Returns(returnValue);
    }

    [Fact]
    public async Task ProcessAsync_NewEvent_AcceptsWithFullWeight()
    {
        SetupRedisSetNx(true);

        var state = new StudentState { StudentId = "s1" };
        state.MasteryMap["concept-1"] = 0.5;

        var cmd = new SyncOfflineEvents("s1", new List<OfflineEvent>
        {
            new OfflineAttemptEvent(
                DateTimeOffset.UtcNow.AddMinutes(-5), "key-1",
                "session-1", "concept-1", "q1", QuestionType.MultipleChoice,
                "answer", 3000, 0, false, 5, 1)
        });

        var (result, events) = await _sut.ProcessAsync(cmd, state);

        Assert.Equal(1, result.Accepted);
        Assert.Equal(0, result.Rejected);
        Assert.Equal(0, result.Duplicates);
        Assert.Single(events);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateEvent_Skipped()
    {
        SetupRedisSetNx(false);

        var state = new StudentState { StudentId = "s1" };
        var cmd = new SyncOfflineEvents("s1", new List<OfflineEvent>
        {
            new OfflineAttemptEvent(
                DateTimeOffset.UtcNow, "dup-key",
                "s1", "c1", "q1", QuestionType.MultipleChoice,
                "a", 3000, 0, false, 0, 0)
        });

        var (result, events) = await _sut.ProcessAsync(cmd, state);

        Assert.Equal(0, result.Accepted);
        Assert.Equal(1, result.Duplicates);
        Assert.Empty(events);
    }

    [Fact]
    public async Task ProcessAsync_RemovedConcept_RejectedWithZeroWeight()
    {
        SetupRedisSetNx(true);

        var state = new StudentState { StudentId = "s1" };
        state.MasteryMap["other-concept"] = 0.5;

        var cmd = new SyncOfflineEvents("s1", new List<OfflineEvent>
        {
            new OfflineAttemptEvent(
                DateTimeOffset.UtcNow, "key-removed",
                "s1", "removed-concept", "q1", QuestionType.MultipleChoice,
                "a", 3000, 0, false, 0, 0)
        });

        var (result, events) = await _sut.ProcessAsync(cmd, state);

        Assert.Equal(0, result.Accepted);
        Assert.Equal(1, result.Rejected);
        Assert.Empty(events);
    }

    [Fact]
    public async Task ProcessAsync_MethodologyChanged_AcceptedWithReducedWeight()
    {
        SetupRedisSetNx(true);

        var state = new StudentState { StudentId = "s1" };
        state.MasteryMap["c1"] = 0.5;
        state.MethodologyMap["c1"] = Methodology.Feynman;
        state.LastActivityDate = DateTimeOffset.UtcNow;
        state.MethodAttemptHistory["c1"] = new List<MethodologyAttemptRecord>
        {
            new("Feynman", "stagnation", 0.8, DateTimeOffset.UtcNow.AddMinutes(-1))
        };

        var cmd = new SyncOfflineEvents("s1", new List<OfflineEvent>
        {
            new OfflineAttemptEvent(
                DateTimeOffset.UtcNow.AddMinutes(-10), "key-method-change",
                "s1", "c1", "q1", QuestionType.MultipleChoice,
                "a", 3000, 0, false, 0, 0)
        });

        var (result, events) = await _sut.ProcessAsync(cmd, state);

        Assert.Equal(1, result.Accepted);
        Assert.Single(events);
        var detail = result.Details.First(d => !d.WasDuplicate);
        Assert.Equal(0.75, detail.Weight);
    }
}
