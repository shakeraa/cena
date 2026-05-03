// =============================================================================
// Cena Platform — LearningSession shadow-write integration tests (Phase 1)
// EPIC-PRR-A Sprint 1 (ADR-0012 Schedule Lock)
//
// Covers the Phase 1 shadow-write contract for the StudentActor → LearningSession
// bounded-context bridge. Three invariants exercised:
//
//   1. When the feature flag is OFF, the writer is a no-op and never touches
//      Marten.
//   2. When the feature flag is ON, a SessionStarted_V1 triggers an append
//      of SessionStarted_V2 to the `session-{SessionId}` stream; the payload
//      is byte-faithful to V1.
//   3. LearningSessionProjection.Create builds a correct LearningSessionRecord
//      from the SessionStarted_V2 that the shadow writer produces.
//
// These tests use NSubstitute to mock IDocumentStore / IDocumentSession
// because the full Marten test harness (Postgres + migrations + async
// projections) is heavy enough that wiring it in Phase 1 risks the time-
// box. A follow-up integration test with a real Marten harness is the
// right next step — see ADR-0012 Sprint 2-3 scope for the shadow-write
// rollout validation. Until then the projection coverage below exercises
// `Create` directly, not through an end-to-end stream replay.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Sessions;
using Cena.Actors.Sessions.Events;
using Cena.Actors.Sessions.Projections;
using Cena.Actors.Sessions.Shadow;
using Marten;
using Marten.Events;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Cena.Actors.Tests.Sessions;

public sealed class LearningSessionShadowWriteTests : IDisposable
{
    private readonly string? _priorFlagValue;

    public LearningSessionShadowWriteTests()
    {
        // Capture and clear the env var so tests control it explicitly.
        _priorFlagValue = Environment.GetEnvironmentVariable(
            LearningSessionShadowWriteFeatureFlag.EnvVarName);
        Environment.SetEnvironmentVariable(
            LearningSessionShadowWriteFeatureFlag.EnvVarName, null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(
            LearningSessionShadowWriteFeatureFlag.EnvVarName, _priorFlagValue);
    }

    // -------------------------------------------------------------------------
    // Feature flag
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    [InlineData("yes", true)]
    [InlineData("on", true)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("off", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("garbage", false)]
    public void FeatureFlag_IsTruthy_MatchesContract(string raw, bool expected)
    {
        Assert.Equal(expected, LearningSessionShadowWriteFeatureFlag.IsTruthy(raw));
    }

    [Fact]
    public void FeatureFlag_IsTruthy_NullIsFalse()
    {
        Assert.False(LearningSessionShadowWriteFeatureFlag.IsTruthy(null));
    }

    [Fact]
    public void FeatureFlag_IsEnabled_DefaultsFalseWhenEnvVarUnset()
    {
        Environment.SetEnvironmentVariable(
            LearningSessionShadowWriteFeatureFlag.EnvVarName, null);
        Assert.False(LearningSessionShadowWriteFeatureFlag.IsEnabled());
    }

    [Fact]
    public void FeatureFlag_IsEnabled_TrueWhenEnvVarIsTruthy()
    {
        Environment.SetEnvironmentVariable(
            LearningSessionShadowWriteFeatureFlag.EnvVarName, "true");
        Assert.True(LearningSessionShadowWriteFeatureFlag.IsEnabled());
    }

    // -------------------------------------------------------------------------
    // Shadow writer — flag OFF path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AppendSessionStartedAsync_WhenFlagOff_IsNoOp()
    {
        // Arrange: flag off (cleared by ctor)
        var store = Substitute.For<IDocumentStore>();
        var logger = Substitute.For<ILogger<LearningSessionShadowWriter>>();
        var writer = new LearningSessionShadowWriter(store, logger);

        var v1 = SampleV1Event();

        // Act
        await writer.AppendSessionStartedAsync(v1);

        // Assert: no Marten session opened, no writes issued
        store.DidNotReceiveWithAnyArgs().LightweightSession();
    }

    // -------------------------------------------------------------------------
    // Shadow writer — flag ON path (full dual-write contract)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AppendSessionStartedAsync_WhenFlagOn_AppendsV2ToSessionStream()
    {
        // Arrange
        Environment.SetEnvironmentVariable(
            LearningSessionShadowWriteFeatureFlag.EnvVarName, "true");

        var store = Substitute.For<IDocumentStore>();
        var session = Substitute.For<IDocumentSession>();
        var events = Substitute.For<IEventStoreOperations>();
        session.Events.Returns(events);
        store.LightweightSession().Returns(session);

        var logger = Substitute.For<ILogger<LearningSessionShadowWriter>>();
        var writer = new LearningSessionShadowWriter(store, logger);

        var v1 = SampleV1Event();

        // Act
        await writer.AppendSessionStartedAsync(v1);

        // Assert: exactly one Append to the expected session-{id} stream, with a
        // SessionStarted_V2 whose payload mirrors the V1.
        var expectedStream = $"session-{v1.SessionId}";
        events.Received(1).Append(
            Arg.Is<string>(s => s == expectedStream),
            Arg.Is<object[]>(a =>
                a.Length == 1 &&
                a[0] is SessionStarted_V2 &&
                ((SessionStarted_V2)a[0]).StudentId == v1.StudentId &&
                ((SessionStarted_V2)a[0]).SessionId == v1.SessionId &&
                ((SessionStarted_V2)a[0]).DeviceType == v1.DeviceType &&
                ((SessionStarted_V2)a[0]).AppVersion == v1.AppVersion &&
                ((SessionStarted_V2)a[0]).Methodology == v1.Methodology &&
                ((SessionStarted_V2)a[0]).ExperimentCohort == v1.ExperimentCohort &&
                ((SessionStarted_V2)a[0]).IsOffline == v1.IsOffline &&
                ((SessionStarted_V2)a[0]).ClientTimestamp == v1.ClientTimestamp &&
                ((SessionStarted_V2)a[0]).SchoolId == v1.SchoolId));

        await session.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AppendSessionStartedAsync_WhenSaveChangesThrows_DoesNotPropagate()
    {
        // Arrange: flag ON, Marten save throws
        Environment.SetEnvironmentVariable(
            LearningSessionShadowWriteFeatureFlag.EnvVarName, "true");

        var store = Substitute.For<IDocumentStore>();
        var session = Substitute.For<IDocumentSession>();
        var events = Substitute.For<IEventStoreOperations>();
        session.Events.Returns(events);
        store.LightweightSession().Returns(session);
        session.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new InvalidOperationException("marten down")));

        var logger = Substitute.For<ILogger<LearningSessionShadowWriter>>();
        var writer = new LearningSessionShadowWriter(store, logger);

        // Act: must not throw even though the underlying Marten op failed —
        // shadow writer is best-effort, V1 primary write is source of truth.
        var ex = await Record.ExceptionAsync(() => writer.AppendSessionStartedAsync(SampleV1Event()));

        // Assert: swallowed
        Assert.Null(ex);
    }

    [Fact]
    public async Task AppendSessionStartedAsync_NullEvent_Throws()
    {
        // Arg contract: caller bug — fail fast.
        var store = Substitute.For<IDocumentStore>();
        var logger = Substitute.For<ILogger<LearningSessionShadowWriter>>();
        var writer = new LearningSessionShadowWriter(store, logger);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => writer.AppendSessionStartedAsync(null!));
    }

    // -------------------------------------------------------------------------
    // Null writer — always safe, never touches anything
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NullLearningSessionShadowWriter_AppendSessionStartedAsync_IsAlwaysNoOp()
    {
        // Even with the flag turned on, the null writer never writes.
        Environment.SetEnvironmentVariable(
            LearningSessionShadowWriteFeatureFlag.EnvVarName, "true");

        var writer = NullLearningSessionShadowWriter.Instance;
        var ex = await Record.ExceptionAsync(() => writer.AppendSessionStartedAsync(SampleV1Event()));

        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // Aggregate stream-key contract
    // -------------------------------------------------------------------------

    [Fact]
    public void LearningSessionAggregate_StreamKey_MatchesContract()
    {
        Assert.Equal("session-abc123", LearningSessionAggregate.StreamKey("abc123"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void LearningSessionAggregate_StreamKey_EmptySessionIdThrows(string? sessionId)
    {
        Assert.Throws<ArgumentException>(() =>
            LearningSessionAggregate.StreamKey(sessionId!));
    }

    // -------------------------------------------------------------------------
    // Aggregate state
    // -------------------------------------------------------------------------

    [Fact]
    public void LearningSessionAggregate_Apply_SessionStartedV2_PopulatesState()
    {
        var agg = new LearningSessionAggregate();
        var evt = SampleV2Event();

        agg.Apply(evt);

        Assert.True(agg.State.IsStarted);
        Assert.Equal(evt.SessionId, agg.State.SessionId);
        Assert.Equal(evt.StudentId, agg.State.StudentId);
        Assert.Equal(evt.ClientTimestamp, agg.State.StartedAt);
        Assert.Equal(evt.Methodology, agg.State.Methodology);
        Assert.Equal(evt.SchoolId, agg.State.SchoolId);
    }

    [Fact]
    public void LearningSessionAggregate_Apply_UnknownEvent_IsSilentlyIgnored()
    {
        // Sprint 2+ events arrive before their handlers are wired —
        // the aggregate must tolerate forward migration.
        var agg = new LearningSessionAggregate();
        agg.Apply(new object()); // anonymous unknown payload

        Assert.False(agg.State.IsStarted);
    }

    // -------------------------------------------------------------------------
    // Projection — Create builds a correct LearningSessionRecord
    // -------------------------------------------------------------------------

    [Fact]
    public void LearningSessionProjection_Create_BuildsRecordFromSessionStartedV2()
    {
        var projection = new LearningSessionProjection();
        var evt = SampleV2Event();

        var record = projection.Create(evt);

        Assert.Equal(evt.SessionId, record.Id);
        Assert.Equal(evt.StudentId, record.StudentId);
        Assert.Equal(evt.ClientTimestamp, record.StartedAt);
        Assert.Equal(evt.Methodology, record.Methodology);
        Assert.Equal(evt.DeviceType, record.DeviceType);
        Assert.Equal(evt.AppVersion, record.AppVersion);
        Assert.Equal(evt.IsOffline, record.IsOffline);
        Assert.Equal(evt.ExperimentCohort, record.ExperimentCohort);
        Assert.Equal(evt.SchoolId, record.SchoolId);
    }

    // -------------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------------

    private static SessionStarted_V1 SampleV1Event() => new(
        StudentId: "stu-alice",
        SessionId: "ses-01HX0ABCDE",
        DeviceType: "mobile-pwa",
        AppVersion: "1.12.0",
        Methodology: "Socratic",
        ExperimentCohort: "cohort-control",
        IsOffline: false,
        ClientTimestamp: new DateTimeOffset(2026, 4, 20, 12, 0, 0, TimeSpan.Zero),
        SchoolId: "school-IL-001");

    private static SessionStarted_V2 SampleV2Event() => new(
        StudentId: "stu-bob",
        SessionId: "ses-01HY9ZYXWV",
        DeviceType: "web",
        AppVersion: "1.12.1",
        Methodology: "Direct",
        ExperimentCohort: "cohort-treatment",
        IsOffline: true,
        ClientTimestamp: new DateTimeOffset(2026, 4, 20, 14, 30, 0, TimeSpan.Zero),
        SchoolId: "school-IL-002");
}
