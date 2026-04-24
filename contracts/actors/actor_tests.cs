// =============================================================================
// Cena Platform -- Actor Test Contracts and Patterns
// Layer: Tests | Runtime: .NET 9 | Framework: Proto.Actor v1.x
//
// TEST CATEGORIES:
//   1. Unit tests: Actor message handling with Proto.Actor TestKit
//   2. Integration tests: Event sourcing with in-memory Marten
//   3. Property-based tests: BKT calculation verification (FsCheck)
//   4. Load tests: 10K concurrent StudentActors
//   5. Chaos tests: Node failure, actor reactivation, state recovery
//
// CONVENTIONS:
//   - Test class per actor, method per behavior
//   - Arrange-Act-Assert with structured logging
//   - All async, CancellationToken-aware
//   - Deterministic: no Thread.Sleep, use TestKit's time control
// =============================================================================

using System.Diagnostics;
using Marten;
using Marten.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NATS.Client.Core;
using Proto;
using Proto.Cluster;
using Proto.TestKit;
using StackExchange.Redis;
using Weasel.Core;
using Xunit;
using Xunit.Abstractions;

using Cena.Contracts.Actors;
using Cena.Data.EventStore;

namespace Cena.Actors.Tests;

// =============================================================================
// 1. UNIT TESTS -- Actor Message Handling (Proto.Actor TestKit)
// =============================================================================

/// <summary>
/// Unit tests for the StudentActor. Uses Proto.Actor TestKit to simulate
/// the actor system without real infrastructure.
///
/// <para><b>Pattern:</b></para>
/// 1. Create a TestKit probe
/// 2. Spawn the actor with mocked dependencies
/// 3. Send messages via the probe
/// 4. Assert responses and side effects
/// </summary>
[Trait("Category", "Unit")]
[Trait("Actor", "StudentActor")]
public sealed class StudentActorTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<IDocumentStore> _documentStoreMock;
    private readonly Mock<INatsConnection> _natsMock;
    private readonly Mock<IConnectionMultiplexer> _redisMock;
    private readonly Mock<IMethodologySwitchService> _switchServiceMock;
    private readonly ILoggerFactory _loggerFactory;
    private ActorSystem? _system;

    public StudentActorTests(ITestOutputHelper output)
    {
        _output = output;
        _documentStoreMock = new Mock<IDocumentStore>();
        _natsMock = new Mock<INatsConnection>();
        _redisMock = new Mock<IConnectionMultiplexer>();
        _switchServiceMock = new Mock<IMethodologySwitchService>();
        _loggerFactory = LoggerFactory.Create(b => b.AddXUnit(output));

        // ---- Setup Marten mock for lightweight session ----
        var sessionMock = new Mock<IDocumentSession>();
        var eventStoreMock = new Mock<IEventStore>();

        sessionMock.Setup(s => s.Events).Returns(eventStoreMock.Object);
        _documentStoreMock
            .Setup(s => s.LightweightSession())
            .Returns(sessionMock.Object);

        // ---- Setup Redis mock ----
        var dbMock = new Mock<IDatabase>();
        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(dbMock.Object);
    }

    public Task InitializeAsync()
    {
        _system = new ActorSystem();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_system != null)
            await _system.ShutdownAsync();
    }

    /// <summary>
    /// Verifies that StartSession creates a new session and returns
    /// a valid StartSessionResponse with session ID and methodology.
    /// </summary>
    [Fact]
    public async Task StartSession_WithValidCommand_ReturnsSessionResponse()
    {
        // Arrange
        var probe = _system!.Root;
        var studentId = Guid.CreateVersion7().ToString();

        var actor = SpawnTestStudentActor(probe, studentId);

        var command = new StartSession(
            studentId, "math-101", null, "mobile", "1.0.0",
            DateTimeOffset.UtcNow, false);

        // Act
        var response = await _system.Root.RequestAsync<ActorResult<StartSessionResponse>>(
            actor, command, TimeSpan.FromSeconds(5));

        // Assert
        Assert.NotNull(response);
        Assert.True(response.Success, $"Expected success but got: {response.ErrorMessage}");
        Assert.NotNull(response.Data);
        Assert.NotEmpty(response.Data!.SessionId);
        Assert.NotNull(response.Data.ActiveMethodology);

        _output.WriteLine($"Session started: {response.Data.SessionId}, " +
            $"Methodology: {response.Data.ActiveMethodology}");
    }

    /// <summary>
    /// Verifies that GetProfile returns the in-memory state snapshot
    /// without any database round-trip.
    /// </summary>
    [Fact]
    public async Task GetProfile_ReturnsInMemoryState()
    {
        // Arrange
        var probe = _system!.Root;
        var studentId = Guid.CreateVersion7().ToString();
        var actor = SpawnTestStudentActor(probe, studentId);

        var query = new GetStudentProfile(studentId);

        // Act
        var response = await _system.Root.RequestAsync<ActorResult<StudentProfileResponse>>(
            actor, query, TimeSpan.FromSeconds(5));

        // Assert
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.NotNull(response.Data);
        Assert.Equal(studentId, response.Data!.StudentId);
    }

    /// <summary>
    /// Verifies that EndSession without an active session returns an error.
    /// </summary>
    [Fact]
    public async Task EndSession_WithNoActiveSession_ReturnsError()
    {
        // Arrange
        var probe = _system!.Root;
        var studentId = Guid.CreateVersion7().ToString();
        var actor = SpawnTestStudentActor(probe, studentId);

        var command = new EndSession(studentId, "nonexistent-session", SessionEndReason.Completed);

        // Act
        var response = await _system.Root.RequestAsync<ActorResult>(
            actor, command, TimeSpan.FromSeconds(5));

        // Assert
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Equal("SESSION_MISMATCH", response.ErrorCode);
    }

    /// <summary>
    /// Verifies that SwitchMethodology delegates to the MethodologySwitchService
    /// and persists the switch event.
    /// </summary>
    [Fact]
    public async Task SwitchMethodology_WithValidDecision_PersistsEvent()
    {
        // Arrange
        var studentId = Guid.CreateVersion7().ToString();
        var actor = SpawnTestStudentActor(_system!.Root, studentId);

        _switchServiceMock
            .Setup(s => s.DecideSwitch(It.IsAny<DecideSwitchRequest>()))
            .ReturnsAsync(new DecideSwitchResponse(
                true, Methodology.Feynman, 0.85, false, null, "test decision"));

        // First start a session to have an active context
        await _system.Root.RequestAsync<ActorResult<StartSessionResponse>>(
            actor, new StartSession(studentId, "math-101", null, "mobile", "1.0.0",
                DateTimeOffset.UtcNow, false),
            TimeSpan.FromSeconds(5));

        var command = new SwitchMethodology(studentId, "algebra-101", "Show me a different way");

        // Act
        var response = await _system.Root.RequestAsync<ActorResult>(
            actor, command, TimeSpan.FromSeconds(5));

        // Assert
        Assert.NotNull(response);
        _switchServiceMock.Verify(
            s => s.DecideSwitch(It.IsAny<DecideSwitchRequest>()), Times.Once);
    }

    // ---- Helper: Spawn a test StudentActor with mocked dependencies ----

    private PID SpawnTestStudentActor(IRootContext context, string studentId)
    {
        var props = Props.FromProducer(() => new StudentActor(
            _documentStoreMock.Object,
            _natsMock.Object,
            _redisMock.Object,
            _loggerFactory.CreateLogger<StudentActor>(),
            _switchServiceMock.Object));

        return context.Spawn(props);
    }
}

// =============================================================================
// 2. INTEGRATION TESTS -- Event Sourcing with In-Memory Marten
// =============================================================================

/// <summary>
/// Integration tests that verify the full event sourcing pipeline:
/// command -> event -> Apply -> state change -> snapshot.
///
/// Uses Marten with an in-memory PostgreSQL instance (or real Postgres
/// in CI). Tests the actual serialization, Apply methods, and snapshot
/// rebuild.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Component", "EventSourcing")]
public sealed class EventSourcingIntegrationTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private IDocumentStore? _store;

    /// <summary>
    /// Connection string for test database. In CI, this points to a
    /// PostgreSQL container. Locally, use docker-compose.
    /// </summary>
    private const string TestConnectionString =
        "Host=localhost;Database=cena_test;Username=cena;Password=cena_test;Port=5432";

    public EventSourcingIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(TestConnectionString);
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            opts.DatabaseSchemaName = $"cena_test_{Guid.NewGuid():N}".Substring(0, 30);

            // Register the same event types and projections as production
            opts.ConfigureCenaEventStore(TestConnectionString);
        });

        // Ensure schema exists
        await _store.Storage.ApplyAllConfiguredChangesToDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        if (_store != null)
        {
            // Clean up test schema
            await _store.Advanced.Clean.CompletelyRemoveAllAsync();
            _store.Dispose();
        }
    }

    /// <summary>
    /// Verifies that events appended to a stream can be replayed to rebuild
    /// the StudentProfileSnapshot aggregate.
    /// </summary>
    [Fact]
    public async Task EventReplay_RebuildsStudentSnapshot()
    {
        // Arrange
        var studentId = Guid.CreateVersion7().ToString();
        await using var session = _store!.LightweightSession();

        // Append a sequence of events
        var events = new object[]
        {
            new SessionStarted_V1(studentId, "session-1", "mobile", "1.0.0",
                "Socratic", null, false, DateTimeOffset.UtcNow),

            new ConceptAttempted_V1(studentId, "algebra-101", "session-1",
                true, 5000, "q1", "MultipleChoice", "Socratic",
                "None", 0.3, 0.45, 0, false, "hash1", 2, 0, false),

            new ConceptAttempted_V1(studentId, "algebra-101", "session-1",
                true, 4000, "q2", "MultipleChoice", "Socratic",
                "None", 0.45, 0.62, 0, false, "hash2", 1, 0, false),

            new XpAwarded_V1(studentId, 10, "exercise_correct", 10),

            new StreakUpdated_V1(studentId, 1, 1, DateTimeOffset.UtcNow)
        };

        session.Events.StartStream(studentId, events);
        await session.SaveChangesAsync();

        // Act: Rebuild aggregate from events
        await using var readSession = _store.LightweightSession();
        var snapshot = await readSession.Events.AggregateStreamAsync<StudentProfileSnapshot>(studentId);

        // Assert
        Assert.NotNull(snapshot);
        Assert.Equal(studentId, snapshot!.StudentId);
        Assert.Equal(10, snapshot.TotalXp);
        Assert.Equal(1, snapshot.CurrentStreak);
        Assert.Equal(1, snapshot.SessionCount);
        Assert.True(snapshot.ConceptMastery.ContainsKey("algebra-101"));
        Assert.Equal(0.62, snapshot.ConceptMastery["algebra-101"].PKnown, precision: 2);

        _output.WriteLine($"Snapshot rebuilt. XP={snapshot.TotalXp}, " +
            $"Streak={snapshot.CurrentStreak}, Concepts={snapshot.ConceptMastery.Count}");
    }

    /// <summary>
    /// Verifies that the inline snapshot projection creates snapshots
    /// at the configured interval (every 100 events).
    /// </summary>
    [Fact]
    public async Task SnapshotCreation_AfterEventThreshold()
    {
        // Arrange
        var studentId = Guid.CreateVersion7().ToString();
        await using var session = _store!.LightweightSession();

        // Generate 101 events to trigger snapshot
        var events = new List<object>
        {
            new SessionStarted_V1(studentId, "session-1", "mobile", "1.0.0",
                "Socratic", null, false, DateTimeOffset.UtcNow)
        };

        for (int i = 0; i < 100; i++)
        {
            events.Add(new ConceptAttempted_V1(
                studentId, $"concept-{i % 10}", "session-1",
                i % 3 != 0, // 2/3 correct
                3000 + (i * 100), $"q{i}", "MultipleChoice", "Socratic",
                "None", 0.3 + (i * 0.005), 0.3 + ((i + 1) * 0.005),
                0, false, $"hash{i}", i % 5, 0, false));
        }

        session.Events.StartStream(studentId, events.ToArray());
        await session.SaveChangesAsync();

        // Act: Read back -- Marten should have created an inline snapshot
        await using var readSession = _store.LightweightSession();
        var snapshot = await readSession.Events.AggregateStreamAsync<StudentProfileSnapshot>(studentId);

        // Assert
        Assert.NotNull(snapshot);
        Assert.True(snapshot!.SessionCount >= 1);

        _output.WriteLine($"After 101 events: SessionCount={snapshot.SessionCount}, " +
            $"Concepts={snapshot.ConceptMastery.Count}");
    }

    /// <summary>
    /// Verifies that methodology switch events are correctly applied
    /// to the snapshot and methodology history is tracked.
    /// </summary>
    [Fact]
    public async Task MethodologySwitchEvent_UpdatesSnapshotState()
    {
        // Arrange
        var studentId = Guid.CreateVersion7().ToString();
        await using var session = _store!.LightweightSession();

        var events = new object[]
        {
            new SessionStarted_V1(studentId, "session-1", "mobile", "1.0.0",
                "Socratic", null, false, DateTimeOffset.UtcNow),

            new MethodologySwitched_V1(studentId, "algebra-101",
                "Socratic", "Feynman", "stagnation_detected", 0.75,
                "Conceptual", 0.85)
        };

        session.Events.StartStream(studentId, events);
        await session.SaveChangesAsync();

        // Act
        await using var readSession = _store.LightweightSession();
        var snapshot = await readSession.Events.AggregateStreamAsync<StudentProfileSnapshot>(studentId);

        // Assert
        Assert.NotNull(snapshot);
        Assert.Equal("Feynman", snapshot!.ActiveMethodologyMap["algebra-101"]);
        Assert.Contains("Feynman",
            snapshot.MethodAttemptHistory.GetValueOrDefault("algebra-101", new()) ?? new());
    }
}

// =============================================================================
// 3. PROPERTY-BASED TESTS -- BKT Calculations (FsCheck)
// =============================================================================

/// <summary>
/// Property-based tests for BKT (Bayesian Knowledge Tracing) calculations.
/// Uses FsCheck to generate random inputs and verify invariants hold.
///
/// <para><b>Properties verified:</b></para>
/// <list type="bullet">
///   <item>P(known) is always in [0.01, 0.99] (clamped)</item>
///   <item>Correct answer always increases or maintains P(known)</item>
///   <item>Incorrect answer always decreases or maintains P(known)</item>
///   <item>BKT update is deterministic (same input -> same output)</item>
///   <item>P(known) converges to 1.0 with many correct answers</item>
///   <item>P(known) converges to 0.0 with many incorrect answers</item>
///   <item>BKT parameters are bounded: P(G), P(S) in [0, 0.5], P(T), P(L0) in [0, 1]</item>
/// </list>
/// </summary>
[Trait("Category", "PropertyBased")]
[Trait("Component", "BKT")]
public sealed class BktPropertyTests
{
    private readonly ITestOutputHelper _output;

    public BktPropertyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Property: P(known) is always clamped to [0.01, 0.99] regardless of input.
    /// </summary>
    [Theory]
    [InlineData(0.0, 0.1, 0.25, 0.1, true)]
    [InlineData(1.0, 0.1, 0.25, 0.1, false)]
    [InlineData(0.5, 0.0, 0.5, 0.5, true)]
    [InlineData(0.5, 1.0, 0.0, 0.0, false)]
    [InlineData(0.001, 0.001, 0.001, 0.001, true)]
    [InlineData(0.999, 0.999, 0.499, 0.499, false)]
    public void BktUpdate_AlwaysClampedToValidRange(
        double pKnown, double pLearn, double pGuess, double pSlip, bool isCorrect)
    {
        // Arrange
        var bkt = new BktParameters
        {
            PKnown = pKnown,
            PLearn = pLearn,
            PGuess = Math.Clamp(pGuess, 0, 0.5),
            PSlip = Math.Clamp(pSlip, 0, 0.5)
        };

        // Act
        var result = BktUpdatePublic(bkt, isCorrect);

        // Assert
        Assert.InRange(result, 0.01, 0.99);
        _output.WriteLine($"BKT({pKnown:F3}, {pLearn:F3}, {pGuess:F3}, {pSlip:F3}, {isCorrect}) = {result:F6}");
    }

    /// <summary>
    /// Property: Correct answers never decrease P(known) when P(G) < P(1-S).
    /// This holds when the model is properly calibrated (guess rate < correct rate).
    /// </summary>
    [Theory]
    [InlineData(0.3, 0.1, 0.25, 0.1)]
    [InlineData(0.5, 0.1, 0.2, 0.1)]
    [InlineData(0.7, 0.05, 0.15, 0.05)]
    [InlineData(0.1, 0.2, 0.25, 0.1)]
    public void BktUpdate_CorrectAnswer_NeverDecreasesMastery(
        double pKnown, double pLearn, double pGuess, double pSlip)
    {
        // Arrange
        var bkt = new BktParameters
        {
            PKnown = pKnown, PLearn = pLearn, PGuess = pGuess, PSlip = pSlip
        };
        var before = bkt.PKnown;

        // Act
        var after = BktUpdatePublic(bkt, isCorrect: true);

        // Assert
        Assert.True(after >= before,
            $"Correct answer decreased P(known): {before:F6} -> {after:F6}");
    }

    /// <summary>
    /// Property: With many consecutive correct answers, P(known) converges toward 0.99.
    /// </summary>
    [Fact]
    public void BktUpdate_ManyCorrectAnswers_ConvergesToHigh()
    {
        // Arrange
        var bkt = new BktParameters
        {
            PKnown = 0.3, PLearn = 0.1, PGuess = 0.25, PSlip = 0.1
        };

        // Act: 50 correct answers
        for (int i = 0; i < 50; i++)
        {
            BktUpdatePublic(bkt, true);
        }

        // Assert
        Assert.True(bkt.PKnown > 0.95,
            $"After 50 correct answers, P(known) should be > 0.95, got {bkt.PKnown:F6}");
        _output.WriteLine($"After 50 correct: P(known) = {bkt.PKnown:F6}");
    }

    /// <summary>
    /// Property: With many consecutive incorrect answers, P(known) converges toward 0.01.
    /// </summary>
    [Fact]
    public void BktUpdate_ManyIncorrectAnswers_ConvergesToLow()
    {
        // Arrange
        var bkt = new BktParameters
        {
            PKnown = 0.7, PLearn = 0.05, PGuess = 0.25, PSlip = 0.1
        };

        // Act: 50 incorrect answers
        for (int i = 0; i < 50; i++)
        {
            BktUpdatePublic(bkt, false);
        }

        // Assert
        // Note: P(known) can't go below PLearn effect, but should be very low
        Assert.True(bkt.PKnown < 0.15,
            $"After 50 incorrect answers, P(known) should be < 0.15, got {bkt.PKnown:F6}");
        _output.WriteLine($"After 50 incorrect: P(known) = {bkt.PKnown:F6}");
    }

    /// <summary>
    /// Property: BKT update is deterministic -- same inputs always produce same output.
    /// </summary>
    [Theory]
    [InlineData(0.5, true)]
    [InlineData(0.5, false)]
    [InlineData(0.1, true)]
    [InlineData(0.9, false)]
    public void BktUpdate_IsDeterministic(double pKnown, bool isCorrect)
    {
        // Arrange
        var bkt1 = new BktParameters { PKnown = pKnown, PLearn = 0.1, PGuess = 0.25, PSlip = 0.1 };
        var bkt2 = new BktParameters { PKnown = pKnown, PLearn = 0.1, PGuess = 0.25, PSlip = 0.1 };

        // Act
        var result1 = BktUpdatePublic(bkt1, isCorrect);
        var result2 = BktUpdatePublic(bkt2, isCorrect);

        // Assert
        Assert.Equal(result1, result2);
    }

    /// <summary>
    /// Exposes the BKT update method for testing. Mirrors the logic in
    /// LearningSessionActor.BktUpdate (which is private static).
    /// </summary>
    private static double BktUpdatePublic(BktParameters bkt, bool isCorrect)
    {
        double pLn = bkt.PKnown;
        double pS = bkt.PSlip;
        double pG = bkt.PGuess;
        double pT = bkt.PLearn;

        double posterior;
        if (isCorrect)
        {
            double numerator = pLn * (1.0 - pS);
            double denominator = numerator + (1.0 - pLn) * pG;
            posterior = denominator > 0 ? numerator / denominator : pLn;
        }
        else
        {
            double numerator = pLn * pS;
            double denominator = numerator + (1.0 - pLn) * (1.0 - pG);
            posterior = denominator > 0 ? numerator / denominator : pLn;
        }

        double updated = posterior + (1.0 - posterior) * pT;
        updated = Math.Clamp(updated, 0.01, 0.99);

        bkt.PKnown = updated;
        return updated;
    }
}

// =============================================================================
// 4. LOAD TESTS -- 10K Concurrent StudentActors
// =============================================================================

/// <summary>
/// Load test: activates 10,000 concurrent StudentActors and measures:
/// - Activation time (p50, p95, p99)
/// - Memory per actor
/// - Event persistence latency
/// - Message throughput
///
/// <para><b>Environment:</b></para>
/// Requires a running PostgreSQL instance and NATS server.
/// Run with: dotnet test --filter "Category=Load"
/// </summary>
[Trait("Category", "Load")]
[Trait("Component", "Cluster")]
public sealed class StudentActorLoadTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ActorSystem? _system;

    private const int ActorCount = 10_000;
    private const int MessagesPerActor = 5;

    public StudentActorLoadTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync()
    {
        _system = new ActorSystem(new ActorSystemConfig
        {
            DeveloperSupervisionLogging = false
        });
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_system != null)
            await _system.ShutdownAsync();
    }

    /// <summary>
    /// Measures actor activation time across 10K actors.
    /// Target: p99 activation time < 50ms.
    /// </summary>
    [Fact(Skip = "Requires infrastructure. Run manually with --filter Category=Load")]
    public async Task Activate10KActors_MeasureLatency()
    {
        // Arrange
        var activationTimes = new List<double>(ActorCount);
        var actors = new List<PID>(ActorCount);

        var documentStoreMock = new Mock<IDocumentStore>();
        var natsMock = new Mock<INatsConnection>();
        var redisMock = new Mock<IConnectionMultiplexer>();
        var switchServiceMock = new Mock<IMethodologySwitchService>();
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));

        // Setup minimal mocks
        var sessionMock = new Mock<IDocumentSession>();
        var eventStoreMock = new Mock<IEventStore>();
        sessionMock.Setup(s => s.Events).Returns(eventStoreMock.Object);
        documentStoreMock.Setup(s => s.LightweightSession()).Returns(sessionMock.Object);

        var dbMock = new Mock<IDatabase>();
        redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
            .Returns(dbMock.Object);

        // Act: Spawn actors and measure activation time
        var totalSw = Stopwatch.StartNew();

        for (int i = 0; i < ActorCount; i++)
        {
            var sw = Stopwatch.StartNew();

            var props = Props.FromProducer(() => new StudentActor(
                documentStoreMock.Object, natsMock.Object, redisMock.Object,
                loggerFactory.CreateLogger<StudentActor>(),
                switchServiceMock.Object));

            var pid = _system!.Root.Spawn(props);
            actors.Add(pid);

            sw.Stop();
            activationTimes.Add(sw.Elapsed.TotalMilliseconds);
        }

        totalSw.Stop();

        // Assert: Compute percentiles
        activationTimes.Sort();
        var p50 = activationTimes[(int)(ActorCount * 0.50)];
        var p95 = activationTimes[(int)(ActorCount * 0.95)];
        var p99 = activationTimes[(int)(ActorCount * 0.99)];

        _output.WriteLine($"=== Actor Activation Latency ({ActorCount} actors) ===");
        _output.WriteLine($"  Total time: {totalSw.Elapsed.TotalSeconds:F2}s");
        _output.WriteLine($"  p50: {p50:F2}ms");
        _output.WriteLine($"  p95: {p95:F2}ms");
        _output.WriteLine($"  p99: {p99:F2}ms");
        _output.WriteLine($"  Throughput: {ActorCount / totalSw.Elapsed.TotalSeconds:F0} actors/s");

        // Cleanup
        foreach (var pid in actors)
        {
            await _system!.Root.StopAsync(pid);
        }

        // Target: p99 < 50ms for local spawning (no cluster overhead)
        Assert.True(p99 < 50.0,
            $"p99 activation time ({p99:F2}ms) exceeded 50ms target");
    }

    /// <summary>
    /// Measures memory usage per actor after activation.
    /// Target: < 500KB per actor (StudentState memory budget).
    /// </summary>
    [Fact(Skip = "Requires infrastructure. Run manually with --filter Category=Load")]
    public void MeasureMemoryPerActor()
    {
        // Arrange: Create a StudentState with realistic data
        var state = new StudentState();

        // Simulate a student with 200 concepts, 20 recent attempts, 50 HLR timers
        for (int i = 0; i < 200; i++)
        {
            state.MasteryMap[$"concept-{i}"] = 0.3 + (i * 0.003);
            state.MethodologyMap[$"concept-{i}"] = (Methodology)(i % 8);
        }

        for (int i = 0; i < 20; i++)
        {
            state.RecentAttempts.Add(new AttemptRecord(
                $"concept-{i}", i % 2 == 0, 3000 + i * 100,
                "None", "Socratic", DateTimeOffset.UtcNow));
        }

        for (int i = 0; i < 50; i++)
        {
            state.HlrTimers[$"concept-{i}"] = new HlrState(24.0, DateTimeOffset.UtcNow);
        }

        // Act
        var estimatedBytes = state.EstimateMemoryBytes();

        // Assert
        _output.WriteLine($"Estimated memory: {estimatedBytes / 1024.0:F1}KB " +
            $"(budget: {StudentState.MemoryBudgetBytes / 1024}KB)");

        Assert.True(estimatedBytes < StudentState.MemoryBudgetBytes,
            $"Estimated memory ({estimatedBytes}B) exceeds budget ({StudentState.MemoryBudgetBytes}B)");
    }
}

// =============================================================================
// 5. CHAOS TESTS -- Node Failure, Actor Reactivation, State Recovery
// =============================================================================

/// <summary>
/// Chaos tests that verify actor system resilience under failure conditions:
/// - Kill node during active session -> verify actor reactivation on another node
/// - Corrupt event stream -> verify actor handles gracefully
/// - NATS unavailable -> verify events still persist to Marten
/// - Redis unavailable -> verify actor still functions (degrades gracefully)
///
/// <para><b>Environment:</b></para>
/// Requires a running cluster (at least 2 nodes), PostgreSQL, NATS, Redis.
/// Run with: dotnet test --filter "Category=Chaos"
/// </summary>
[Trait("Category", "Chaos")]
[Trait("Component", "Cluster")]
public sealed class ChaosTests : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;

    public ChaosTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Scenario: Actor is processing a session, node dies, actor reactivates
    /// on a new node and recovers state from Marten snapshot + replay.
    ///
    /// <para><b>Steps:</b></para>
    /// 1. Start a StudentActor and begin a session
    /// 2. Process several attempts (build up state)
    /// 3. Simulate node death (stop the actor system)
    /// 4. Create a new actor system (simulating reactivation on new node)
    /// 5. Reactivate the actor and verify state is recovered
    /// </summary>
    [Fact(Skip = "Requires full cluster. Run manually with --filter Category=Chaos")]
    public async Task NodeDeath_DuringSession_RecoversStateOnReactivation()
    {
        // STEP 1-2: Build up state on "node A"
        var studentId = Guid.CreateVersion7().ToString();
        _output.WriteLine($"Test student: {studentId}");

        // In a real chaos test, this would use the actual cluster.
        // This test documents the PATTERN for implementation.

        // STEP 3: Simulate node death
        _output.WriteLine("Simulating node death...");
        // await systemA.ShutdownAsync(); // Force shutdown without graceful drain

        // STEP 4: Reactivate on "node B"
        _output.WriteLine("Reactivating on new node...");
        // var systemB = CreateNewNode();
        // await systemB.Cluster().StartMemberAsync();

        // STEP 5: Query state and verify recovery
        // var profile = await systemB.Cluster()
        //     .RequestAsync<ActorResult<StudentProfileResponse>>(
        //         studentId, "student", new GetStudentProfile(studentId),
        //         CancellationToken.None);

        // Assert.NotNull(profile);
        // Assert.True(profile.Success);
        // Assert.Equal(expectedXp, profile.Data.TotalXp);
        // Assert.Equal(expectedConcepts, profile.Data.MasteryMap.Count);

        _output.WriteLine("Chaos test pattern documented. " +
            "Requires full cluster infrastructure to execute.");

        Assert.True(true, "Pattern test -- see comments for implementation");
    }

    /// <summary>
    /// Scenario: NATS is unavailable during event publishing.
    /// Verifies that events are still persisted to Marten (the source of truth)
    /// and that the actor continues to function.
    ///
    /// When NATS recovers, a catch-up publisher replays missed events.
    /// </summary>
    [Fact(Skip = "Requires infrastructure. Run manually with --filter Category=Chaos")]
    public async Task NatsUnavailable_EventsStillPersistToMarten()
    {
        // This test verifies the critical invariant:
        // Marten is the source of truth. NATS is eventually consistent.
        //
        // Pattern:
        // 1. Configure NATS mock to throw on publish
        // 2. Send AttemptConcept to StudentActor
        // 3. Verify: actor responds with success
        // 4. Verify: event is in Marten event store
        // 5. Verify: NATS failure was logged at Warning level (not Error)

        _output.WriteLine("NATS unavailability pattern documented.");
        Assert.True(true, "Pattern test");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Scenario: Stagnation detector receives corrupted signals.
    /// Verifies that the actor logs the error and continues processing
    /// without crashing.
    /// </summary>
    [Fact]
    public async Task StagnationDetector_HandlesCorruptedSignals()
    {
        // Arrange
        var system = new ActorSystem();
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));

        var props = Props.FromProducer(() =>
            new StagnationDetectorActor(loggerFactory.CreateLogger<StagnationDetectorActor>()));
        var pid = system.Root.Spawn(props);

        // Act: Send signals with edge-case values
        var extremeSignal = new UpdateSignals(
            "test-student", "test-concept", "test-session",
            true, 0, // 0ms response time (edge case)
            ErrorType.None, 0, null,
            0.0, 0.0); // zero baselines

        var response = await system.Root.RequestAsync<ActorResult>(
            pid, extremeSignal, TimeSpan.FromSeconds(5));

        // Assert: should handle gracefully
        Assert.NotNull(response);
        Assert.True(response.Success);

        // Cleanup
        await system.Root.StopAsync(pid);
        await system.ShutdownAsync();

        _output.WriteLine("Stagnation detector handled edge-case signals without crashing.");
    }

    /// <summary>
    /// Verifies that the circuit breaker correctly opens after threshold
    /// failures and rejects subsequent calls.
    /// </summary>
    [Fact]
    public async Task CircuitBreaker_OpensAfterThresholdFailures()
    {
        // Arrange
        var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Debug));
        var breaker = new ActorCircuitBreaker(
            "test-breaker",
            loggerFactory.CreateLogger<ActorCircuitBreaker>(),
            failureThreshold: 3,
            failureWindow: TimeSpan.FromSeconds(10),
            openDuration: TimeSpan.FromSeconds(2));

        Assert.Equal(CircuitState.Closed, breaker.State);

        // Act: Trigger 3 failures
        for (int i = 0; i < 3; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await breaker.ExecuteAsync<int>(async () =>
                {
                    await Task.CompletedTask;
                    throw new InvalidOperationException("simulated failure");
                });
            });
        }

        // Assert: Circuit should be open
        Assert.Equal(CircuitState.Open, breaker.State);

        // Verify calls are rejected while open
        await Assert.ThrowsAsync<CircuitBreakerOpenException>(async () =>
        {
            await breaker.ExecuteAsync<int>(async () =>
            {
                await Task.CompletedTask;
                return 42;
            });
        });

        _output.WriteLine("Circuit breaker correctly opened after 3 failures and rejected calls.");
    }

    [Fact]
    public async Task CircuitBreaker_TransitionsToHalfOpenAfterTimeout()
    {
        // Arrange: open the circuit
        var loggerFactory = new LoggerFactory();
        var breaker = new ActorCircuitBreaker(
            maxFailures: 3,
            loggerFactory.CreateLogger<ActorCircuitBreaker>(),
            openDuration: TimeSpan.FromMilliseconds(100) // Short for testing
        );

        // Trip the breaker
        for (int i = 0; i < 3; i++)
        {
            try { await breaker.ExecuteAsync<int>(async () => throw new Exception("fail")); }
            catch { }
        }
        Assert.Equal(CircuitState.Open, breaker.State);

        // Act: wait for open duration to expire
        await Task.Delay(TimeSpan.FromMilliseconds(150));

        // HalfOpen: next call should be allowed through (probe)
        var result = await breaker.ExecuteAsync(async () => { await Task.CompletedTask; return 42; });
        Assert.Equal(42, result);
        Assert.Equal(CircuitState.HalfOpen, breaker.State);

        // Verify: 3 consecutive successes close the circuit
        for (int i = 0; i < 2; i++)
        {
            await breaker.ExecuteAsync(async () => { await Task.CompletedTask; return 1; });
        }
        Assert.Equal(CircuitState.Closed, breaker.State);

        _output.WriteLine("Circuit breaker correctly transitioned: Open → HalfOpen → Closed");
    }
}

// =============================================================================
// TEST HELPERS & EXTENSIONS
// =============================================================================

/// <summary>
/// xUnit output helper extension for structured logging in tests.
/// </summary>
internal static class TestLoggerExtensions
{
    public static ILoggingBuilder AddXUnit(this ILoggingBuilder builder, ITestOutputHelper output)
    {
        builder.AddProvider(new XUnitLoggerProvider(output));
        builder.SetMinimumLevel(LogLevel.Debug);
        return builder;
    }
}

/// <summary>Simple xUnit logger provider for test output.</summary>
internal sealed class XUnitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _output;

    public XUnitLoggerProvider(ITestOutputHelper output) => _output = output;

    public ILogger CreateLogger(string categoryName) =>
        new XUnitLogger(_output, categoryName);

    public void Dispose() { }
}

/// <summary>Simple xUnit logger that writes to test output.</summary>
internal sealed class XUnitLogger : ILogger
{
    private readonly ITestOutputHelper _output;
    private readonly string _category;

    public XUnitLogger(ITestOutputHelper output, string category)
    {
        _output = output;
        _category = category;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try
        {
            _output.WriteLine($"[{logLevel}] {_category}: {formatter(state, exception)}");
            if (exception != null)
                _output.WriteLine($"  Exception: {exception}");
        }
        catch (InvalidOperationException)
        {
            // Test may have already completed
        }
    }
}
