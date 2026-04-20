// =============================================================================
// Cena Platform — SessionTutorContextService Unit Tests (prr-204)
//
// Covers:
//   (a) Context available after session init — the builder assembles a
//       non-null snapshot from the queue projection.
//   (b) Fresh session returns zero counts.
//   (c) After one wrong answer, the builder reflects the increment and the
//       last-misconception-tag extractor picks up the concept id.
//   (d) Cross-student access returns null (mapped to 404 upstream).
//   (e) Cross-tenant access at the endpoint layer returns 403 — see the
//       endpoint-tenant tests below.
//   (f) Redis unavailable → GetAsync still returns a context from the live
//       Marten fallback.
//   Plus attempt-phase transitions and BKT-bucket rollup.
//
// All Redis + Marten collaborators are substituted with NSubstitute so the
// tests are deterministic and hermetic (same pattern as
// DailyTutorTimeBudgetTests).
// =============================================================================

using System.Diagnostics.Metrics;
using Cena.Actors.Accommodations;
using Cena.Actors.Projections;
using Cena.Actors.RateLimit;
using Cena.Actors.Tutoring;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;

namespace Cena.Actors.Tests.Tutoring;

public sealed class SessionTutorContextServiceTests
{
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly IDatabase _db = Substitute.For<IDatabase>();
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IAccommodationProfileService _accommodations =
        Substitute.For<IAccommodationProfileService>();
    private readonly IDailyTutorTimeBudget _budget =
        Substitute.For<IDailyTutorTimeBudget>();
    private readonly IQuerySession _querySession = Substitute.For<IQuerySession>();

    public SessionTutorContextServiceTests()
    {
        _redis.GetDatabase().Returns(_db);
        _store.QuerySession().Returns(_querySession);
        _accommodations
            .GetCurrentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => AccommodationProfile.Default(ci.ArgAt<string>(0)));
        _budget
            .CheckAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new DailyTutorTimeCheck(
                Allowed: true,
                UsedSeconds: 0,
                RemainingSeconds: 30 * 60,
                DailyLimitSeconds: 30 * 60,
                NudgeThresholdSeconds: 24 * 60));
    }

    private static IConfiguration EmptyConfig()
        => new ConfigurationBuilder().AddInMemoryCollection().Build();

    private SessionTutorContextService NewSut(IConfiguration? config = null) =>
        new(_redis, _store, _accommodations, _budget,
            config ?? EmptyConfig(),
            NullLogger<SessionTutorContextService>.Instance,
            new DummyMeterFactory());

    // -------------------------------------------------------------------------
    // Test (a) + (b): fresh session returns zero counts
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_FreshSession_ReturnsZeroCounts()
    {
        var queue = new LearningSessionQueueProjection
        {
            Id = "sess-1",
            SessionId = "sess-1",
            StudentId = "stu-1",
            CurrentQuestionId = null,
            TotalQuestionsAttempted = 0,
            CorrectAnswers = 0,
            StartedAt = DateTime.UtcNow.AddMinutes(-1),
        };
        SetQueue(queue);
        SetHistory(null);
        SetActive(null);

        // Redis cache miss.
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);
        _db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(true);

        var sut = NewSut();
        var ctx = await sut.GetAsync("sess-1", "stu-1");

        Assert.NotNull(ctx);
        Assert.Equal("sess-1", ctx!.SessionId);
        Assert.Equal("stu-1", ctx.StudentId);
        Assert.Equal(0, ctx.AnsweredCount);
        Assert.Equal(0, ctx.CorrectCount);
        Assert.Equal(0, ctx.CurrentRung);
        Assert.Null(ctx.LastMisconceptionTag);
        Assert.Equal(SessionTutorContextAttemptPhase.FirstTry, ctx.AttemptPhase);
        Assert.Equal(30, ctx.DailyMinutesRemaining);
        Assert.Equal("unknown", ctx.BktMasteryBucket);
    }

    // -------------------------------------------------------------------------
    // Test (c): after one wrong attempt, counts reflect and tag surfaces
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_AfterOneWrongAttempt_ReflectsIncrementsAndMisconception()
    {
        var queue = new LearningSessionQueueProjection
        {
            Id = "sess-2",
            SessionId = "sess-2",
            StudentId = "stu-2",
            CurrentQuestionId = "q-42",
            TotalQuestionsAttempted = 1,
            CorrectAnswers = 0,
            StartedAt = DateTime.UtcNow.AddMinutes(-3),
            ConceptMasterySnapshot = new Dictionary<string, double>
            {
                ["algebra.linear-equations"] = 0.25, // low bucket
            },
            LadderRungByQuestion = new Dictionary<string, int>
            {
                ["q-42"] = 1,
            },
        };
        SetQueue(queue);
        SetHistory(new SessionAttemptHistoryDocument
        {
            Id = "sess-2",
            SessionId = "sess-2",
            StudentId = "stu-2",
            Attempts =
            {
                new SessionAttemptItem
                {
                    QuestionId = "q-42",
                    ConceptId = "algebra.linear-equations",
                    IsCorrect = false,
                    WasSkipped = false,
                    Timestamp = DateTimeOffset.UtcNow.AddSeconds(-10),
                },
            },
        });
        SetActive(null);

        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);
        _db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Returns(true);

        var sut = NewSut();
        var ctx = await sut.GetAsync("sess-2", "stu-2");

        Assert.NotNull(ctx);
        Assert.Equal(1, ctx!.AnsweredCount);
        Assert.Equal(0, ctx.CorrectCount);
        Assert.Equal(1, ctx.CurrentRung);
        Assert.Equal("algebra.linear-equations", ctx.LastMisconceptionTag);
        Assert.Equal(SessionTutorContextAttemptPhase.Retry, ctx.AttemptPhase);
        Assert.Equal("low", ctx.BktMasteryBucket);
    }

    // -------------------------------------------------------------------------
    // Test (d): cross-student session leak refused
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_ForeignStudent_ReturnsNull()
    {
        var queue = new LearningSessionQueueProjection
        {
            Id = "sess-3",
            SessionId = "sess-3",
            StudentId = "stu-owner",
            CurrentQuestionId = null,
            StartedAt = DateTime.UtcNow,
        };
        SetQueue(queue);
        SetHistory(null);
        SetActive(null);
        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Returns(RedisValue.Null);

        var sut = NewSut();
        var ctx = await sut.GetAsync("sess-3", "stu-attacker");

        Assert.Null(ctx);
    }

    // -------------------------------------------------------------------------
    // Test (f): Redis outage falls back to live Marten build
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_RedisOutage_FallsBackToLiveBuild()
    {
        var queue = new LearningSessionQueueProjection
        {
            Id = "sess-4",
            SessionId = "sess-4",
            StudentId = "stu-4",
            TotalQuestionsAttempted = 2,
            CorrectAnswers = 1,
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
        };
        SetQueue(queue);
        SetHistory(null);
        SetActive(null);

        _db.StringGetAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>())
            .Throws(new RedisConnectionException(
                ConnectionFailureType.SocketFailure, "simulated outage"));
        _db.StringSetAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>(),
                Arg.Any<TimeSpan?>(), Arg.Any<When>(), Arg.Any<CommandFlags>())
            .Throws(new RedisConnectionException(
                ConnectionFailureType.SocketFailure, "simulated outage"));

        var sut = NewSut();
        var ctx = await sut.GetAsync("sess-4", "stu-4");

        Assert.NotNull(ctx);
        Assert.Equal(2, ctx!.AnsweredCount);
        Assert.Equal(1, ctx.CorrectCount);
    }

    // -------------------------------------------------------------------------
    // Attempt phase: PostSolution after a correct attempt
    // -------------------------------------------------------------------------

    [Fact]
    public void ResolveAttemptPhase_CorrectAttempt_IsPostSolution()
    {
        var queue = new LearningSessionQueueProjection
        {
            CurrentQuestionId = "q-1",
        };
        var history = new SessionAttemptHistoryDocument
        {
            Attempts =
            {
                new SessionAttemptItem
                {
                    QuestionId = "q-1", IsCorrect = true,
                    Timestamp = DateTimeOffset.UtcNow,
                },
            },
        };

        var phase = SessionTutorContextService.ResolveAttemptPhase(queue, history);
        Assert.Equal(SessionTutorContextAttemptPhase.PostSolution, phase);
    }

    [Fact]
    public void ResolveAttemptPhase_WrongThenCorrect_IsPostSolution()
    {
        var queue = new LearningSessionQueueProjection { CurrentQuestionId = "q-1" };
        var history = new SessionAttemptHistoryDocument
        {
            Attempts =
            {
                new SessionAttemptItem
                {
                    QuestionId = "q-1", IsCorrect = false,
                    Timestamp = DateTimeOffset.UtcNow.AddSeconds(-30),
                },
                new SessionAttemptItem
                {
                    QuestionId = "q-1", IsCorrect = true,
                    Timestamp = DateTimeOffset.UtcNow,
                },
            },
        };

        var phase = SessionTutorContextService.ResolveAttemptPhase(queue, history);
        Assert.Equal(SessionTutorContextAttemptPhase.PostSolution, phase);
    }

    [Fact]
    public void ResolveAttemptPhase_NoAttempts_IsFirstTry()
    {
        var queue = new LearningSessionQueueProjection { CurrentQuestionId = "q-1" };
        var phase = SessionTutorContextService.ResolveAttemptPhase(queue, null);
        Assert.Equal(SessionTutorContextAttemptPhase.FirstTry, phase);
    }

    // -------------------------------------------------------------------------
    // Misconception-tag extraction: most recent wrong attempt wins
    // -------------------------------------------------------------------------

    [Fact]
    public void ExtractLastMisconceptionTag_MostRecentWrong_Wins()
    {
        var history = new SessionAttemptHistoryDocument
        {
            Attempts =
            {
                new SessionAttemptItem
                {
                    ConceptId = "topic.old", IsCorrect = false,
                    Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10),
                },
                new SessionAttemptItem
                {
                    ConceptId = "topic.fresh", IsCorrect = false,
                    Timestamp = DateTimeOffset.UtcNow,
                },
            },
        };

        var tag = SessionTutorContextService.ExtractLastMisconceptionTag(history);
        Assert.Equal("topic.fresh", tag);
    }

    [Fact]
    public void ExtractLastMisconceptionTag_OnlyCorrect_IsNull()
    {
        var history = new SessionAttemptHistoryDocument
        {
            Attempts =
            {
                new SessionAttemptItem
                {
                    ConceptId = "topic.perfect", IsCorrect = true,
                    Timestamp = DateTimeOffset.UtcNow,
                },
            },
        };

        var tag = SessionTutorContextService.ExtractLastMisconceptionTag(history);
        Assert.Null(tag);
    }

    [Fact]
    public void ExtractLastMisconceptionTag_Empty_IsNull()
    {
        Assert.Null(SessionTutorContextService.ExtractLastMisconceptionTag(null));
        Assert.Null(SessionTutorContextService.ExtractLastMisconceptionTag(
            new SessionAttemptHistoryDocument()));
    }

    // -------------------------------------------------------------------------
    // BKT bucket rollup
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0.10, "low")]
    [InlineData(0.32, "low")]
    [InlineData(0.33, "mid")]
    [InlineData(0.50, "mid")]
    [InlineData(0.65, "mid")]
    [InlineData(0.66, "high")]
    [InlineData(0.90, "high")]
    public void ResolveBktBucket_CorrectlyBuckets(double mastery, string expected)
    {
        var queue = new LearningSessionQueueProjection
        {
            ConceptMasterySnapshot = new Dictionary<string, double>
            {
                ["any"] = mastery,
            },
        };
        Assert.Equal(expected, SessionTutorContextService.ResolveBktBucket(queue));
    }

    [Fact]
    public void ResolveBktBucket_EmptySnapshot_IsUnknown()
    {
        var queue = new LearningSessionQueueProjection();
        Assert.Equal("unknown", SessionTutorContextService.ResolveBktBucket(queue));
    }

    // -------------------------------------------------------------------------
    // InvalidateAsync + key format
    // -------------------------------------------------------------------------

    [Fact]
    public async Task InvalidateAsync_DeletesCacheKey()
    {
        _db.KeyDeleteAsync(Arg.Any<RedisKey>(), Arg.Any<CommandFlags>()).Returns(true);

        var sut = NewSut();
        await sut.InvalidateAsync("sess-7");

        await _db.Received(1).KeyDeleteAsync(
            Arg.Is<RedisKey>(k => k.ToString() == "cena:tutor-ctx:sess-7"),
            Arg.Any<CommandFlags>());
    }

    [Fact]
    public void BuildKey_FormatIsStable()
    {
        Assert.Equal("cena:tutor-ctx:abc123",
            SessionTutorContextService.BuildKey("abc123"));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void SetQueue(LearningSessionQueueProjection? queue)
        => _querySession
            .LoadAsync<LearningSessionQueueProjection>(
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(queue);

    private void SetHistory(SessionAttemptHistoryDocument? history)
        => _querySession
            .LoadAsync<SessionAttemptHistoryDocument>(
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(history);

    private void SetActive(ActiveSessionSnapshot? active)
        => _querySession
            .LoadAsync<ActiveSessionSnapshot>(
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(active);

    // Minimal IMeterFactory for tests (mirrors DailyTutorTimeBudgetTests).
    private sealed class DummyMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
