// =============================================================================
// Cena Platform -- CenaHub Unit Tests (PWA-BE-001)
// =============================================================================

using System.Security.Claims;
using System.Text.Json;
using Cena.Actors.Bus;
using Cena.Actors.Serving;
using Cena.Actors.Sessions;
using Cena.Api.Contracts.Hub;
using Cena.Api.Host.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Moq;
using NATS.Client.Core;

namespace Cena.Api.Host.Tests.Hubs;

public sealed class CenaHubTests
{
    private readonly Mock<INatsConnection> _natsMock = new();
    private readonly Mock<SignalRGroupManager> _groupManagerMock = new();
    private readonly Mock<IQuestionBank> _questionBankMock = new();
    private readonly Mock<ILogger<CenaHub>> _loggerMock = new();
    private readonly Mock<IHubCallerClients<ICenaClient>> _clientsMock = new();
    private readonly Mock<ICenaClient> _callerMock = new();
    private readonly Mock<HubCallerContext> _contextMock = new();

    private CenaHub CreateHub(string studentId, string? schoolId = null)
    {
        var hub = new CenaHub(
            _natsMock.Object,
            _groupManagerMock.Object,
            _questionBankMock.Object,
            _loggerMock.Object)
        {
            Clients = _clientsMock.Object,
            Context = _contextMock.Object
        };

        var claims = new List<Claim>
        {
            new("student_id", studentId),
            new("sub", studentId)
        };
        if (schoolId != null)
            claims.Add(new("school_id", schoolId));

        var identity = new ClaimsIdentity(claims, "Test");
        var principal = new ClaimsPrincipal(identity);

        _contextMock.Setup(c => c.User).Returns(principal);
        _contextMock.Setup(c => c.ConnectionId).Returns(Guid.NewGuid().ToString());
        _clientsMock.Setup(c => c.Caller).Returns(_callerMock.Object);

        return hub;
    }

    private static byte[] SerializeEnvelope<T>(T payload, string subject)
    {
        var envelope = BusEnvelope<T>.Create(subject, payload, "actor-host");
        return JsonSerializer.SerializeToUtf8Bytes(envelope, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    [Fact]
    public async Task RequestSessionSnapshot_ActiveSession_ReturnsSnapshot()
    {
        // Arrange
        var hub = CreateHub("student-123", "school-1");
        var sessionId = "session-abc";
        var responsePayload = new SessionSnapshotResponse(
            sessionId,
            3,
            "question-1",
            new Dictionary<string, SkillMasteryDto>
            {
                ["concept-a"] = new("concept-a", 0.75, 5, 4)
            },
            "partial",
            new List<StepResultDto>
            {
                new("1", "q-1", "concept-a", true, 5000, DateTimeOffset.UtcNow.AddMinutes(-5))
            },
            DateTimeOffset.UtcNow.AddMinutes(-10),
            600);

        var replyBytes = SerializeEnvelope(responsePayload, "_INBOX.123");
        var natsMsg = new NatsMsg<byte[]>(
            subject: NatsSubjects.SessionSnapshotRequest,
            replyTo: "_INBOX.123",
            headers: null,
            data: replyBytes,
            connection: _natsMock.Object);

        _natsMock
            .Setup(n => n.RequestAsync<byte[], byte[]>(
                NatsSubjects.SessionSnapshotRequest,
                It.IsAny<byte[]>(),
                It.IsAny<NatsHeaders?>(),
                It.IsAny<INatsSerialize<byte[]>?>(),
                It.IsAny<INatsDeserialize<byte[]>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(natsMsg);

        _questionBankMock
            .Setup(q => q.GetQuestionAsync("question-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => null);

        // Act
        var result = await hub.RequestSessionSnapshot(sessionId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(sessionId, result.SessionId);
        Assert.Equal(3, result.CurrentStepNumber);
        Assert.Equal("partial", result.ScaffoldingLevel);
        Assert.Equal(600, result.SessionDurationSeconds);
        Assert.Single(result.BktSnapshot);
        Assert.Single(result.CompletedSteps);
        _callerMock.Verify(c => c.Error(It.IsAny<HubErrorEvent>()), Times.Never);
    }

    [Fact]
    public async Task RequestSessionSnapshot_ExpiredSession_ReturnsNullAndSendsError()
    {
        // Arrange
        var hub = CreateHub("student-123");
        var sessionId = "session-expired";
        var responsePayload = new SessionSnapshotResponse(
            sessionId, 0, null, new(), "full", new(),
            DateTimeOffset.UtcNow, 0, Error: "session_expired");

        var replyBytes = SerializeEnvelope(responsePayload, "_INBOX.456");
        var natsMsg = new NatsMsg<byte[]>(
            NatsSubjects.SessionSnapshotRequest,
            "_INBOX.456",
            null,
            replyBytes,
            _natsMock.Object);

        _natsMock
            .Setup(n => n.RequestAsync<byte[], byte[]>(
                NatsSubjects.SessionSnapshotRequest,
                It.IsAny<byte[]>(),
                It.IsAny<NatsHeaders?>(),
                It.IsAny<INatsSerialize<byte[]>?>(),
                It.IsAny<INatsDeserialize<byte[]>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(natsMsg);

        HubErrorEvent? capturedError = null;
        _callerMock
            .Setup(c => c.Error(It.IsAny<HubErrorEvent>()))
            .Callback<HubErrorEvent>(e => capturedError = e);

        // Act
        var result = await hub.RequestSessionSnapshot(sessionId);

        // Assert
        Assert.Null(result);
        Assert.NotNull(capturedError);
        Assert.Equal("SESSION_EXPIRED", capturedError!.ErrorCode);
    }

    [Fact]
    public async Task RequestSessionSnapshot_MissingSession_ReturnsNullAndSendsError()
    {
        // Arrange
        var hub = CreateHub("student-123");
        var sessionId = "session-missing";
        var responsePayload = new SessionSnapshotResponse(
            sessionId, 0, null, new(), "full", new(),
            DateTimeOffset.UtcNow, 0, Error: "session_not_found");

        var replyBytes = SerializeEnvelope(responsePayload, "_INBOX.789");
        var natsMsg = new NatsMsg<byte[]>(
            NatsSubjects.SessionSnapshotRequest,
            "_INBOX.789",
            null,
            replyBytes,
            _natsMock.Object);

        _natsMock
            .Setup(n => n.RequestAsync<byte[], byte[]>(
                NatsSubjects.SessionSnapshotRequest,
                It.IsAny<byte[]>(),
                It.IsAny<NatsHeaders?>(),
                It.IsAny<INatsSerialize<byte[]>?>(),
                It.IsAny<INatsDeserialize<byte[]>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(natsMsg);

        HubErrorEvent? capturedError = null;
        _callerMock
            .Setup(c => c.Error(It.IsAny<HubErrorEvent>()))
            .Callback<HubErrorEvent>(e => capturedError = e);

        // Act
        var result = await hub.RequestSessionSnapshot(sessionId);

        // Assert
        Assert.Null(result);
        Assert.NotNull(capturedError);
        Assert.Equal("SESSION_NOT_FOUND", capturedError!.ErrorCode);
    }

    [Fact]
    public void SessionSnapshotDto_MapsAllFields()
    {
        // Arrange
        var startedAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        var steps = new List<StepResultDto>
        {
            new("1", "q-1", "c-1", true, 4000, startedAt.AddMinutes(5)),
            new("2", "q-2", "c-2", false, 6000, startedAt.AddMinutes(10))
        };
        var bkt = new Dictionary<string, SkillMasteryDto>
        {
            ["c-1"] = new("c-1", 0.8, 3, 3),
            ["c-2"] = new("c-2", 0.4, 2, 1)
        };

        // Act
        var dto = new Cena.Api.Contracts.Sessions.SessionSnapshotDto(
            "session-xyz",
            null,
            3,
            bkt,
            "minimal",
            steps,
            startedAt,
            900);

        // Assert
        Assert.Equal("session-xyz", dto.SessionId);
        Assert.Null(dto.CurrentQuestion);
        Assert.Equal(3, dto.CurrentStepNumber);
        Assert.Equal("minimal", dto.ScaffoldingLevel);
        Assert.Equal(startedAt, dto.SessionStartedAt);
        Assert.Equal(900, dto.SessionDurationSeconds);
        Assert.Equal(2, dto.BktSnapshot.Count);
        Assert.Equal(0.8, dto.BktSnapshot["c-1"].MasteryProbability);
        Assert.Equal(3, dto.BktSnapshot["c-1"].AttemptCount);
        Assert.Equal(3, dto.BktSnapshot["c-1"].CorrectCount);
        Assert.Equal(2, dto.CompletedSteps.Count);
        Assert.Equal("q-1", dto.CompletedSteps[0].QuestionId);
        Assert.Equal("c-1", dto.CompletedSteps[0].ConceptId);
        Assert.True(dto.CompletedSteps[0].IsCorrect);
        Assert.Equal(4000, dto.CompletedSteps[0].ResponseTimeMs);
    }
}
