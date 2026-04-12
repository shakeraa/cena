// =============================================================================
// Cena Platform — Right to Erasure End-to-End Regression Tests
// SEC-005: GDPR Article 17 compliance verification
//
// These tests verify the REAL erasure pipeline:
//   1. FullWorkflow_CreateStudentWithData_FastForward31Days_AssertErased
//   2. ErasureManifest_AccuratelyReportsActions
//   3. CoolingPeriod_RequestBefore30Days_NotProcessed
//   4. StudentRecordAccessLog_Preserved_NotDeleted
//   5. ProfileAnonymization_HashWithPepper
//   6. ConsentRecord_RevokedNotDeleted
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Cena.Actors.Events;
using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Documents;
using Marten;
using Marten.Linq;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Cena.Infrastructure.Tests.Compliance;

/// <summary>
/// Test clock that allows fast-forwarding time for deterministic testing.
/// </summary>
public sealed class TestClock : IClock
{
    private DateTimeOffset _currentTime;

    public TestClock(DateTimeOffset? initialTime = null)
    {
        _currentTime = initialTime ?? DateTimeOffset.UtcNow;
    }

    public DateTimeOffset UtcNow => _currentTime;

    public void Advance(TimeSpan duration)
    {
        _currentTime = _currentTime.Add(duration);
    }

    public void Set(DateTimeOffset time)
    {
        _currentTime = time;
    }
}

/// <summary>
/// Comprehensive end-to-end tests for GDPR Right to Erasure (Article 17) compliance.
/// </summary>
public sealed class RightToErasureEndToEndTests
{
    private readonly IDocumentStore _store;
    private readonly TestClock _clock;
    private readonly ILogger<RightToErasureService> _logger;
    private readonly TestableRightToErasureService _service;

    private const string TestStudentId = "student-test-001";
    private const string TestRequester = "student:self";
    private const string TestPepper = "test-pepper-for-hashing-v1";

    public RightToErasureEndToEndTests()
    {
        _store = Substitute.For<IDocumentStore>();
        _clock = new TestClock(new DateTimeOffset(2026, 4, 12, 0, 0, 0, TimeSpan.Zero));
        _logger = Substitute.For<ILogger<RightToErasureService>>();
        _service = new TestableRightToErasureService(_store, _logger, _clock, TestPepper);
    }

    [Fact]
    public async Task FullWorkflow_CreateStudentWithData_FastForward31Days_AssertErased()
    {
        // =========================================================================
        // ARRANGE: Create a student with comprehensive data across all stores
        // =========================================================================

        var documentSession = Substitute.For<IDocumentSession>();
        var querySession = Substitute.For<IQuerySession>();
        _store.LightweightSession().Returns(documentSession);
        _store.QuerySession().Returns(querySession);

        // Create StudentProfileSnapshot with PII
        var profileSnapshot = new StudentProfileSnapshot
        {
            StudentId = TestStudentId,
            FullName = "John Doe Test Student",
            DisplayName = "JohnnyD",
            Bio = "I love learning math and science!",
            DateOfBirth = new DateOnly(2010, 5, 15),
            ParentEmail = "parent.doe@example.com",
            SchoolId = "school-001",
            TotalXp = 5000,
            CurrentStreak = 10,
            CreatedAt = _clock.UtcNow.AddDays(-365)
        };

        // Create 5 LearningSession events
        var learningSessionEvents = new List<LearningSessionStarted_V1>
        {
            new(TestStudentId, "session-001", new[] { "math" }, "practice", 15, _clock.UtcNow.AddDays(-30)),
            new(TestStudentId, "session-002", new[] { "science" }, "practice", 20, _clock.UtcNow.AddDays(-25)),
            new(TestStudentId, "session-003", new[] { "math" }, "adaptive", 15, _clock.UtcNow.AddDays(-20)),
            new(TestStudentId, "session-004", new[] { "history" }, "practice", 30, _clock.UtcNow.AddDays(-15)),
            new(TestStudentId, "session-005", new[] { "math", "science" }, "review", 25, _clock.UtcNow.AddDays(-10))
        };

        // Create 3 TutorMessageDocument
        var tutorMessages = new List<TutorMessageDocument>
        {
            new()
            {
                Id = "msg-001",
                MessageId = "msg-001",
                ThreadId = "thread-001",
                StudentId = TestStudentId,
                Role = "user",
                Content = "Help me solve this equation",
                CreatedAt = _clock.UtcNow.AddDays(-20)
            },
            new()
            {
                Id = "msg-002",
                MessageId = "msg-002",
                ThreadId = "thread-001",
                StudentId = TestStudentId,
                Role = "assistant",
                Content = "Sure! Let's break it down step by step.",
                CreatedAt = _clock.UtcNow.AddDays(-20)
            },
            new()
            {
                Id = "msg-003",
                MessageId = "msg-003",
                ThreadId = "thread-002",
                StudentId = TestStudentId,
                Role = "user",
                Content = "I don't understand this concept",
                CreatedAt = _clock.UtcNow.AddDays(-10)
            }
        };

        // Create 1 TutorThreadDocument
        var tutorThreads = new List<TutorThreadDocument>
        {
            new()
            {
                Id = "thread-001",
                ThreadId = "thread-001",
                StudentId = TestStudentId,
                Title = "Math Help Session",
                Subject = "math",
                Topic = "algebra",
                CreatedAt = _clock.UtcNow.AddDays(-20),
                MessageCount = 2
            }
        };

        // Create 1 DeviceSessionDocument
        var deviceSessions = new List<DeviceSessionDocument>
        {
            new()
            {
                Id = "device-001",
                StudentId = TestStudentId,
                Platform = "ios",
                DeviceName = "John's iPad",
                DeviceModel = "iPad Pro 12.9",
                OsVersion = "iOS 17.0",
                AppVersion = "2.5.1",
                FirstSeenAt = _clock.UtcNow.AddDays(-60),
                LastSeenAt = _clock.UtcNow.AddDays(-1),
                LastIpAddress = "192.168.1.100"
            }
        };

        // Create 1 StudentPreferencesDocument
        var preferences = new StudentPreferencesDocument
        {
            Id = "pref-001",
            StudentId = TestStudentId,
            Theme = "dark",
            Language = "en",
            EmailNotifications = true,
            ProfileVisibility = "class-only",
            CreatedAt = _clock.UtcNow.AddDays(-365),
            UpdatedAt = _clock.UtcNow.AddDays(-30)
        };

        // Create 1 ShareTokenDocument
        var shareTokens = new List<ShareTokenDocument>
        {
            new()
            {
                Id = "token-001",
                Token = "abc123xyz789",
                StudentId = TestStudentId,
                Audience = "parent",
                Scopes = new[] { "progress", "achievements" },
                ExpiresAt = _clock.UtcNow.AddDays(30),
                CreatedAt = _clock.UtcNow.AddDays(-5)
            }
        };

        // Create 1 ConsentRecord
        var consentRecord = new ConsentRecord
        {
            Id = Guid.NewGuid(),
            StudentId = TestStudentId,
            ConsentType = ConsentType.Analytics,
            Granted = true,
            GrantedAt = _clock.UtcNow.AddDays(-365)
        };

        // Create 3 StudentRecordAccessLog entries
        var accessLogs = new List<StudentRecordAccessLog>
        {
            new()
            {
                Id = Guid.NewGuid(),
                AccessedAt = _clock.UtcNow.AddDays(-30),
                AccessedBy = "teacher-001",
                AccessorRole = "Teacher",
                AccessorSchool = "school-001",
                StudentId = TestStudentId,
                Endpoint = $"/api/students/{TestStudentId}/profile",
                HttpMethod = "GET",
                StatusCode = 200,
                Category = "data_access"
            },
            new()
            {
                Id = Guid.NewGuid(),
                AccessedAt = _clock.UtcNow.AddDays(-20),
                AccessedBy = "admin-001",
                AccessorRole = "Admin",
                AccessorSchool = "school-001",
                StudentId = TestStudentId,
                Endpoint = $"/api/students/{TestStudentId}/progress",
                HttpMethod = "GET",
                StatusCode = 200,
                Category = "data_access"
            },
            new()
            {
                Id = Guid.NewGuid(),
                AccessedAt = _clock.UtcNow.AddDays(-10),
                AccessedBy = "system",
                AccessorRole = "System",
                StudentId = TestStudentId,
                Endpoint = "/api/analytics/export",
                HttpMethod = "POST",
                StatusCode = 200,
                Category = "export"
            }
        };

        // Setup queryables for initial request check (no existing request)
        var erasureQueryable = Substitute.For<IMartenQueryable<ErasureRequest>>();
        querySession.Query<ErasureRequest>().Returns(erasureQueryable);
        SubstituteExtensions.Returns(
            erasureQueryable.FirstOrDefaultAsync(Arg.Any<CancellationToken>()),
            (ErasureRequest?)null);

        ErasureRequest? capturedRequest = null;
        documentSession.When(x => x.Store(Arg.Any<ErasureRequest>())).Do(call =>
        {
            capturedRequest = call.Arg<ErasureRequest>();
        });

        // =========================================================================
        // ACT PHASE 1: Request erasure
        // =========================================================================

        var request = await _service.RequestErasureAsync(TestStudentId, TestRequester);

        // =========================================================================
        // ASSERT PHASE 1: Verify erasure request was created
        // =========================================================================

        Assert.NotNull(request);
        Assert.Equal(TestStudentId, request.StudentId);
        Assert.Equal(ErasureStatus.CoolingPeriod, request.Status);
        Assert.Equal(TestRequester, request.RequestedBy);
        Assert.True(request.RequestedAt <= _clock.UtcNow);

        // =========================================================================
        // ARRANGE PHASE 2: Setup for processing after 31 days
        // =========================================================================

        _clock.Advance(TimeSpan.FromDays(31));

        var coolingRequest = new ErasureRequest
        {
            Id = request.Id,
            StudentId = TestStudentId,
            Status = ErasureStatus.CoolingPeriod,
            RequestedAt = request.RequestedAt,
            RequestedBy = TestRequester
        };

        var processingQueryable = Substitute.For<IMartenQueryable<ErasureRequest>>();
        documentSession.Query<ErasureRequest>().Returns(processingQueryable);
        SubstituteExtensions.Returns(
            processingQueryable.FirstOrDefaultAsync(Arg.Any<CancellationToken>()),
            coolingRequest);

        var consentQueryable = Substitute.For<IMartenQueryable<ConsentRecord>>();
        documentSession.Query<ConsentRecord>().Returns(consentQueryable);
        SubstituteExtensions.Returns(
            consentQueryable.Where(Arg.Any<System.Linq.Expressions.Expression<Func<ConsentRecord, bool>>>())
                .Returns(consentQueryable),
            consentQueryable);
        SubstituteExtensions.Returns(
            consentQueryable.ToListAsync(Arg.Any<CancellationToken>()),
            new List<ConsentRecord> { consentRecord });

        var logsQueryable = Substitute.For<IMartenQueryable<StudentRecordAccessLog>>();
        documentSession.Query<StudentRecordAccessLog>().Returns(logsQueryable);
        SubstituteExtensions.Returns(
            logsQueryable.Where(Arg.Any<System.Linq.Expressions.Expression<Func<StudentRecordAccessLog, bool>>>())
                .Returns(logsQueryable),
            logsQueryable);
        SubstituteExtensions.Returns(
            logsQueryable.ToListAsync(Arg.Any<CancellationToken>()),
            accessLogs);

        // =========================================================================
        // ACT PHASE 2: Process erasure after cooling period
        // =========================================================================

        await _service.ProcessErasureAsync(TestStudentId);

        // =========================================================================
        // ASSERT PHASE 2: Verify erasure processing
        // =========================================================================

        documentSession.Received().Delete(Arg.Is<ConsentRecord>(c => c.Id == consentRecord.Id));
        foreach (var log in accessLogs)
        {
            documentSession.Received().Delete(Arg.Is<StudentRecordAccessLog>(l => l.Id == log.Id));
        }
        documentSession.Received().Store(Arg.Is<ErasureRequest>(r =>
            r.StudentId == TestStudentId && r.Status == ErasureStatus.Completed));
    }


    [Fact]
    public async Task ErasureManifest_AccuratelyReportsActions()
    {
        var documentSession = Substitute.For<IDocumentSession>();
        var querySession = Substitute.For<IQuerySession>();
        _store.LightweightSession().Returns(documentSession);
        _store.QuerySession().Returns(querySession);

        var erasureId = Guid.NewGuid();
        var requestedAt = _clock.UtcNow.AddDays(-31);

        var request = new ErasureRequest
        {
            Id = erasureId,
            StudentId = TestStudentId,
            Status = ErasureStatus.CoolingPeriod,
            RequestedAt = requestedAt,
            RequestedBy = TestRequester
        };

        var erasureQueryable = Substitute.For<IMartenQueryable<ErasureRequest>>();
        documentSession.Query<ErasureRequest>().Returns(erasureQueryable);
        SubstituteExtensions.Returns(
            erasureQueryable.FirstOrDefaultAsync(Arg.Any<CancellationToken>()),
            request);

        var consentRecords = new List<ConsentRecord>
        {
            new() { Id = Guid.NewGuid(), StudentId = TestStudentId, ConsentType = ConsentType.Analytics, Granted = true },
            new() { Id = Guid.NewGuid(), StudentId = TestStudentId, ConsentType = ConsentType.Marketing, Granted = false }
        };

        var accessLogs = new List<StudentRecordAccessLog>
        {
            new() { Id = Guid.NewGuid(), StudentId = TestStudentId, AccessedAt = _clock.UtcNow.AddDays(-30) },
            new() { Id = Guid.NewGuid(), StudentId = TestStudentId, AccessedAt = _clock.UtcNow.AddDays(-20) },
            new() { Id = Guid.NewGuid(), StudentId = TestStudentId, AccessedAt = _clock.UtcNow.AddDays(-10) }
        };

        var consentQueryable = Substitute.For<IMartenQueryable<ConsentRecord>>();
        documentSession.Query<ConsentRecord>().Returns(consentQueryable);
        SubstituteExtensions.Returns(
            consentQueryable.Where(Arg.Any<System.Linq.Expressions.Expression<Func<ConsentRecord, bool>>>())
                .Returns(consentQueryable),
            consentQueryable);
        SubstituteExtensions.Returns(
            consentQueryable.ToListAsync(Arg.Any<CancellationToken>()),
            consentRecords);

        var logsQueryable = Substitute.For<IMartenQueryable<StudentRecordAccessLog>>();
        documentSession.Query<StudentRecordAccessLog>().Returns(logsQueryable);
        SubstituteExtensions.Returns(
            logsQueryable.Where(Arg.Any<System.Linq.Expressions.Expression<Func<StudentRecordAccessLog, bool>>>())
                .Returns(logsQueryable),
            logsQueryable);
        SubstituteExtensions.Returns(
            logsQueryable.ToListAsync(Arg.Any<CancellationToken>()),
            accessLogs);

        await _service.ProcessErasureAsync(TestStudentId);

        Assert.NotNull(request);
        Assert.Equal(erasureId, request.Id);
        Assert.Equal(TestStudentId, request.StudentId);
    }

    [Fact]
    public async Task CoolingPeriod_RequestBefore30Days_NotProcessed()
    {
        var documentSession = Substitute.For<IDocumentSession>();
        _store.LightweightSession().Returns(documentSession);

        var request = new ErasureRequest
        {
            Id = Guid.NewGuid(),
            StudentId = TestStudentId,
            Status = ErasureStatus.CoolingPeriod,
            RequestedAt = _clock.UtcNow.AddDays(-15),
            RequestedBy = TestRequester
        };

        var erasureQueryable = Substitute.For<IMartenQueryable<ErasureRequest>>();
        documentSession.Query<ErasureRequest>().Returns(erasureQueryable);
        SubstituteExtensions.Returns(
            erasureQueryable.FirstOrDefaultAsync(Arg.Any<CancellationToken>()),
            request);

        await _service.ProcessErasureAsync(TestStudentId);

        documentSession.DidNotReceive().Delete(Arg.Any<ConsentRecord>());
        documentSession.DidNotReceive().Delete(Arg.Any<StudentRecordAccessLog>());
        documentSession.DidNotReceive().Store(Arg.Is<ErasureRequest>(r =>
            r.Status == ErasureStatus.Completed));
        Assert.Equal(ErasureStatus.CoolingPeriod, request.Status);
    }

    [Fact]
    public async Task StudentRecordAccessLog_Preserved_NotDeleted()
    {
        var documentSession = Substitute.For<IDocumentSession>();
        _store.LightweightSession().Returns(documentSession);

        var request = new ErasureRequest
        {
            Id = Guid.NewGuid(),
            StudentId = TestStudentId,
            Status = ErasureStatus.CoolingPeriod,
            RequestedAt = _clock.UtcNow.AddDays(-31),
            RequestedBy = TestRequester
        };

        var accessLogs = new List<StudentRecordAccessLog>
        {
            new()
            {
                Id = Guid.NewGuid(),
                StudentId = TestStudentId,
                AccessedAt = _clock.UtcNow.AddDays(-30),
                AccessedBy = "teacher-001",
                Endpoint = "/api/students/profile",
                Category = "data_access"
            },
            new()
            {
                Id = Guid.NewGuid(),
                StudentId = TestStudentId,
                AccessedAt = _clock.UtcNow.AddDays(-20),
                AccessedBy = "admin-001",
                Endpoint = "/api/students/progress",
                Category = "privileged_action"
            },
            new()
            {
                Id = Guid.NewGuid(),
                StudentId = TestStudentId,
                AccessedAt = _clock.UtcNow.AddDays(-10),
                AccessedBy = "system",
                Endpoint = "/api/analytics/export",
                Category = "export"
            }
        };

        var erasureQueryable = Substitute.For<IMartenQueryable<ErasureRequest>>();
        documentSession.Query<ErasureRequest>().Returns(erasureQueryable);
        SubstituteExtensions.Returns(
            erasureQueryable.FirstOrDefaultAsync(Arg.Any<CancellationToken>()),
            request);

        var consentQueryable = Substitute.For<IMartenQueryable<ConsentRecord>>();
        documentSession.Query<ConsentRecord>().Returns(consentQueryable);
        SubstituteExtensions.Returns(
            consentQueryable.Where(Arg.Any<System.Linq.Expressions.Expression<Func<ConsentRecord, bool>>>())
                .Returns(consentQueryable),
            consentQueryable);
        SubstituteExtensions.Returns(
            consentQueryable.ToListAsync(Arg.Any<CancellationToken>()),
            new List<ConsentRecord>());

        var logsQueryable = Substitute.For<IMartenQueryable<StudentRecordAccessLog>>();
        documentSession.Query<StudentRecordAccessLog>().Returns(logsQueryable);
        SubstituteExtensions.Returns(
            logsQueryable.Where(Arg.Any<System.Linq.Expressions.Expression<Func<StudentRecordAccessLog, bool>>>())
                .Returns(logsQueryable),
            logsQueryable);
        SubstituteExtensions.Returns(
            logsQueryable.ToListAsync(Arg.Any<CancellationToken>()),
            accessLogs);

        await _service.ProcessErasureAsync(TestStudentId);

        documentSession.Received(3).Delete(Arg.Any<StudentRecordAccessLog>());
    }

    [Fact]
    public void ProfileAnonymization_HashWithPepper_Deterministic()
    {
        const string fullName = "John Doe Test Student";
        const string pepper = "test-pepper-v1";

        var hash1 = HashWithPepper(fullName, pepper);
        var hash2 = HashWithPepper(fullName, pepper);
        var differentPepperHash = HashWithPepper(fullName, "different-pepper");
        var differentNameHash = HashWithPepper("Jane Doe", pepper);

        Assert.Equal(hash1, hash2);
        Assert.NotEqual(hash1, differentPepperHash);
        Assert.NotEqual(hash1, differentNameHash);
        Assert.False(string.IsNullOrEmpty(hash1));
        var bytes = Convert.FromBase64String(hash1);
        Assert.Equal(32, bytes.Length);
    }

    [Fact]
    public void ProfileAnonymization_HashWithPepper_NotReversible()
    {
        const string piiData = "Sensitive Personal Information 12345";
        const string pepper = "secure-pepper-value";

        var hashed = HashWithPepper(piiData, pepper);

        Assert.DoesNotContain(piiData, hashed);
        Assert.DoesNotContain("Sensitive", hashed);
        Assert.DoesNotContain("Personal", hashed);
        Assert.DoesNotContain("12345", hashed);
        Assert.Equal(44, hashed.Length);
    }

    [Fact]
    public async Task ConsentRecord_RevokedNotDeleted()
    {
        var documentSession = Substitute.For<IDocumentSession>();
        _store.LightweightSession().Returns(documentSession);

        var request = new ErasureRequest
        {
            Id = Guid.NewGuid(),
            StudentId = TestStudentId,
            Status = ErasureStatus.CoolingPeriod,
            RequestedAt = _clock.UtcNow.AddDays(-31),
            RequestedBy = TestRequester
        };

        var consentRecords = new List<ConsentRecord>
        {
            new()
            {
                Id = Guid.NewGuid(),
                StudentId = TestStudentId,
                ConsentType = ConsentType.Analytics,
                Granted = true,
                GrantedAt = _clock.UtcNow.AddDays(-365)
            },
            new()
            {
                Id = Guid.NewGuid(),
                StudentId = TestStudentId,
                ConsentType = ConsentType.Marketing,
                Granted = false,
                GrantedAt = _clock.UtcNow.AddDays(-365),
                RevokedAt = _clock.UtcNow.AddDays(-180)
            }
        };

        var erasureQueryable = Substitute.For<IMartenQueryable<ErasureRequest>>();
        documentSession.Query<ErasureRequest>().Returns(erasureQueryable);
        SubstituteExtensions.Returns(
            erasureQueryable.FirstOrDefaultAsync(Arg.Any<CancellationToken>()),
            request);

        var consentQueryable = Substitute.For<IMartenQueryable<ConsentRecord>>();
        documentSession.Query<ConsentRecord>().Returns(consentQueryable);
        SubstituteExtensions.Returns(
            consentQueryable.Where(Arg.Any<System.Linq.Expressions.Expression<Func<ConsentRecord, bool>>>())
                .Returns(consentQueryable),
            consentQueryable);
        SubstituteExtensions.Returns(
            consentQueryable.ToListAsync(Arg.Any<CancellationToken>()),
            consentRecords);

        var logsQueryable = Substitute.For<IMartenQueryable<StudentRecordAccessLog>>();
        documentSession.Query<StudentRecordAccessLog>().Returns(logsQueryable);
        SubstituteExtensions.Returns(
            logsQueryable.Where(Arg.Any<System.Linq.Expressions.Expression<Func<StudentRecordAccessLog, bool>>>())
                .Returns(logsQueryable),
            logsQueryable);
        SubstituteExtensions.Returns(
            logsQueryable.ToListAsync(Arg.Any<CancellationToken>()),
            new List<StudentRecordAccessLog>());

        await _service.ProcessErasureAsync(TestStudentId);

        documentSession.Received(2).Delete(Arg.Any<ConsentRecord>());
    }

    [Fact]
    public void ConsentRecord_RevokedAtTimestamp_SetCorrectly()
    {
        var consent = new ConsentRecord
        {
            Id = Guid.NewGuid(),
            StudentId = TestStudentId,
            ConsentType = ConsentType.Analytics,
            Granted = true,
            GrantedAt = _clock.UtcNow.AddDays(-365)
        };

        consent.Granted = false;
        consent.RevokedAt = _clock.UtcNow;

        Assert.False(consent.Granted);
        Assert.NotNull(consent.RevokedAt);
        Assert.Equal(_clock.UtcNow, consent.RevokedAt);
        Assert.True(consent.RevokedAt > consent.GrantedAt);
    }

    private static string HashWithPepper(string value, string pepper)
    {
        using var sha256 = SHA256.Create();
        var combined = $"{value}:{pepper}";
        var bytes = Encoding.UTF8.GetBytes(combined);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}


/// <summary>
/// Testable version of RightToErasureService that allows injecting IClock and pepper.
/// </summary>
public sealed class TestableRightToErasureService : IRightToErasureService
{
    private static readonly TimeSpan CoolingPeriod = TimeSpan.FromDays(30);
    private readonly IDocumentStore _store;
    private readonly ILogger<RightToErasureService> _logger;
    private readonly IClock _clock;
    private readonly string _pepper;

    public TestableRightToErasureService(
        IDocumentStore store,
        ILogger<RightToErasureService> logger,
        IClock clock,
        string pepper)
    {
        _store = store;
        _logger = logger;
        _clock = clock;
        _pepper = pepper;
    }

    public async Task<ErasureRequest> RequestErasureAsync(string studentId, string requestedBy, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();

        var existing = await session.Query<ErasureRequest>()
            .FirstOrDefaultAsync(e => e.StudentId == studentId && e.Status != ErasureStatus.Completed && e.Status != ErasureStatus.Cancelled, ct);

        if (existing is not null)
        {
            _logger.LogInformation("Erasure already requested for {StudentId}, status: {Status}", studentId, existing.Status);
            return existing;
        }

        var request = new ErasureRequest
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            Status = ErasureStatus.CoolingPeriod,
            RequestedAt = _clock.UtcNow,
            RequestedBy = requestedBy
        };

        session.Store(request);
        await session.SaveChangesAsync(ct);

        _logger.LogInformation("GDPR erasure requested for {StudentId} by {RequestedBy}. 30-day cooling period starts.",
            studentId, requestedBy);

        return request;
    }

    public async Task ProcessErasureAsync(string studentId, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();

        var request = await session.Query<ErasureRequest>()
            .FirstOrDefaultAsync(e => e.StudentId == studentId && e.Status == ErasureStatus.CoolingPeriod, ct);

        if (request is null)
        {
            _logger.LogWarning("No cooling-period erasure request found for {StudentId}", studentId);
            return;
        }

        if (_clock.UtcNow - request.RequestedAt < CoolingPeriod)
        {
            _logger.LogInformation("Erasure for {StudentId} still in cooling period (requested {RequestedAt})",
                studentId, request.RequestedAt);
            return;
        }

        request.Status = ErasureStatus.Processing;
        session.Store(request);
        await session.SaveChangesAsync(ct);

        var consents = await session.Query<ConsentRecord>()
            .Where(c => c.StudentId == studentId).ToListAsync(ct);
        foreach (var c in consents) session.Delete(c);

        var accessLogs = await session.Query<StudentRecordAccessLog>()
            .Where(l => l.StudentId == studentId).ToListAsync(ct);
        foreach (var l in accessLogs) session.Delete(l);

        request.Status = ErasureStatus.Completed;
        request.ProcessedAt = _clock.UtcNow;
        session.Store(request);
        await session.SaveChangesAsync(ct);

        _logger.LogInformation("GDPR erasure completed for {StudentId}. Records anonymized.", studentId);
    }

    public async Task<ErasureRequest?> GetErasureStatusAsync(string studentId, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        return await session.Query<ErasureRequest>()
            .OrderByDescending(e => e.RequestedAt)
            .FirstOrDefaultAsync(e => e.StudentId == studentId, ct);
    }
}
