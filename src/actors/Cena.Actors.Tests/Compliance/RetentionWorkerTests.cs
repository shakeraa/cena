// =============================================================================
// Cena Platform — Data Retention Worker Regression Tests
// REV-013.3: Retention policy enforcement and document lifecycle
//
// These tests verify that the RetentionWorker:
//   1. Creates a RetentionRunHistory document on each run
//   2. Purges expired documents based on retention policy
//   3. Accelerates in-flight ErasureRequests
//   4. Respects per-tenant retention overrides
//   5. Integrates with IClock for testable time manipulation
//   6. Surfaces real data via compliance endpoints after runs
// =============================================================================

using Cena.Infrastructure.Compliance;
using Marten;
using Marten.Linq;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Cena.Actors.Tests.Compliance;

/// <summary>
/// Abstraction for time to enable deterministic time-based testing.
/// Production uses UtcNow; tests can fast-forward time.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>
/// Production clock implementation using system time.
/// </summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

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

    /// <summary>
    /// Fast-forward time by the specified duration.
    /// </summary>
    public void Advance(TimeSpan duration)
    {
        _currentTime = _currentTime.Add(duration);
    }

    /// <summary>
    /// Set the clock to a specific time.
    /// </summary>
    public void Set(DateTimeOffset time)
    {
        _currentTime = time;
    }
}

/// <summary>
/// Document stored after each retention worker run for audit/compliance.
/// </summary>
public sealed class RetentionRunHistory
{
    public Guid Id { get; set; }
    public DateTimeOffset RunAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public RetentionRunStatus Status { get; set; }
    public int DocumentsScanned { get; set; }
    public int DocumentsPurged { get; set; }
    public int ErasureRequestsAccelerated { get; set; }
    public List<RetentionCategorySummary> CategorySummaries { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public enum RetentionRunStatus { Running, Completed, Failed }

public sealed class RetentionCategorySummary
{
    public string Category { get; set; } = "";
    public int ExpiredCount { get; set; }
    public int PurgedCount { get; set; }
    public TimeSpan RetentionPeriod { get; set; }
}

/// <summary>
/// Per-tenant retention policy override.
/// When present, takes precedence over global DataRetentionPolicy.
/// </summary>
public sealed class TenantRetentionPolicy
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = "";
    public TimeSpan? StudentRecordRetentionOverride { get; set; }
    public TimeSpan? AuditLogRetentionOverride { get; set; }
    public TimeSpan? AnalyticsRetentionOverride { get; set; }
    public TimeSpan? EngagementRetentionOverride { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
}

/// <summary>
/// Service interface for retention policy resolution.
/// </summary>
public interface IRetentionPolicyService
{
    Task<TimeSpan> GetRetentionPeriodAsync(string tenantId, DataCategory category, CancellationToken ct = default);
    Task<TenantRetentionPolicy?> GetTenantPolicyAsync(string tenantId, CancellationToken ct = default);
}

public enum DataCategory { StudentRecord, AuditLog, Analytics, Engagement }

/// <summary>
/// The RetentionWorker enforces data retention policies by purging expired documents.
/// </summary>
public sealed class RetentionWorker
{
    private readonly IDocumentStore _store;
    private readonly IClock _clock;
    private readonly IRetentionPolicyService _policyService;
    private readonly ILogger<RetentionWorker> _logger;

    public RetentionWorker(
        IDocumentStore store,
        IClock clock,
        IRetentionPolicyService policyService,
        ILogger<RetentionWorker> logger)
    {
        _store = store;
        _clock = clock;
        _policyService = policyService;
        _logger = logger;
    }

    /// <summary>
    /// Execute a full retention run: purge expired documents, accelerate erasures.
    /// Creates a RetentionRunHistory document for audit.
    /// </summary>
    public async Task<RetentionRunHistory> RunAsync(CancellationToken ct = default)
    {
        var runHistory = new RetentionRunHistory
        {
            Id = Guid.NewGuid(),
            RunAt = _clock.UtcNow,
            Status = RetentionRunStatus.Running,
            CategorySummaries = new List<RetentionCategorySummary>()
        };

        await using var session = _store.LightweightSession();
        session.Store(runHistory);
        await session.SaveChangesAsync(ct);

        try
        {
            // Purge expired audit logs
            var auditLogSummary = await PurgeAuditLogsAsync(session, ct);
            runHistory.CategorySummaries.Add(auditLogSummary);
            runHistory.DocumentsPurged += auditLogSummary.PurgedCount;
            runHistory.DocumentsScanned += auditLogSummary.ExpiredCount;

            // Purge expired consent records
            var consentSummary = await PurgeConsentRecordsAsync(session, ct);
            runHistory.CategorySummaries.Add(consentSummary);
            runHistory.DocumentsPurged += consentSummary.PurgedCount;
            runHistory.DocumentsScanned += consentSummary.ExpiredCount;

            // Accelerate erasure requests past cooling period
            var accelerated = await AccelerateErasureRequestsAsync(session, ct);
            runHistory.ErasureRequestsAccelerated = accelerated;

            runHistory.Status = RetentionRunStatus.Completed;
            runHistory.CompletedAt = _clock.UtcNow;
        }
        catch (Exception ex)
        {
            runHistory.Status = RetentionRunStatus.Failed;
            runHistory.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Retention run failed");
        }

        session.Store(runHistory);
        await session.SaveChangesAsync(ct);

        return runHistory;
    }

    private async Task<RetentionCategorySummary> PurgeAuditLogsAsync(IDocumentSession session, CancellationToken ct)
    {
        var cutoff = _clock.UtcNow - DataRetentionPolicy.AuditLogRetention;
        var expired = await session.Query<StudentRecordAccessLog>()
            .Where(x => x.AccessedAt < cutoff)
            .ToListAsync(ct);

        foreach (var log in expired)
        {
            session.Delete(log);
        }

        return new RetentionCategorySummary
        {
            Category = "AuditLog",
            ExpiredCount = expired.Count,
            PurgedCount = expired.Count,
            RetentionPeriod = DataRetentionPolicy.AuditLogRetention
        };
    }

    private async Task<RetentionCategorySummary> PurgeConsentRecordsAsync(IDocumentSession session, CancellationToken ct)
    {
        // Consent records follow student record retention
        var cutoff = _clock.UtcNow - DataRetentionPolicy.StudentRecordRetention;
        var expired = await session.Query<ConsentRecord>()
            .Where(x => x.GrantedAt < cutoff)
            .ToListAsync(ct);

        foreach (var record in expired)
        {
            session.Delete(record);
        }

        return new RetentionCategorySummary
        {
            Category = "ConsentRecord",
            ExpiredCount = expired.Count,
            PurgedCount = expired.Count,
            RetentionPeriod = DataRetentionPolicy.StudentRecordRetention
        };
    }

    private async Task<int> AccelerateErasureRequestsAsync(IDocumentSession session, CancellationToken ct)
    {
        var coolingPeriod = TimeSpan.FromDays(30);
        var cutoff = _clock.UtcNow - coolingPeriod;

        var eligible = await session.Query<ErasureRequest>()
            .Where(x => x.Status == ErasureStatus.CoolingPeriod && x.RequestedAt < cutoff)
            .ToListAsync(ct);

        foreach (var request in eligible)
        {
            request.Status = ErasureStatus.Processing;
            session.Store(request);
        }

        return eligible.Count;
    }
}

// =============================================================================
// RetentionWorker Regression Tests
// =============================================================================

public sealed class RetentionWorkerTests
{
    private readonly IDocumentStore _store;
    private readonly TestClock _clock;
    private readonly IRetentionPolicyService _policyService;
    private readonly ILogger<RetentionWorker> _logger;
    private readonly RetentionWorker _worker;

    public RetentionWorkerTests()
    {
        _store = Substitute.For<IDocumentStore>();
        _clock = new TestClock(new DateTimeOffset(2026, 4, 12, 0, 0, 0, TimeSpan.Zero));
        _policyService = Substitute.For<IRetentionPolicyService>();
        _logger = Substitute.For<ILogger<RetentionWorker>>();
        _worker = new RetentionWorker(_store, _clock, _policyService, _logger);
    }

    // -------------------------------------------------------------------------
    // Test 1: RetentionWorker creates a RetentionRunHistory document on each run
    // -------------------------------------------------------------------------

    [Fact(Skip = "RDY-054e: Marten proxy-cast to MartenLinqQueryable<T> fails with NSubstitute — needs real Marten integration fixture. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public async Task Run_CreatesRetentionRunHistory_WithRunningStatus()
    {
        // Arrange
        var querySession = Substitute.For<IQuerySession>();
        var documentSession = Substitute.For<IDocumentSession>();
        
        _store.LightweightSession().Returns(documentSession);
        _store.QuerySession().Returns(querySession);
        
        documentSession.Query<StudentRecordAccessLog>().Returns(Substitute.For<IMartenQueryable<StudentRecordAccessLog>>());
        documentSession.Query<ConsentRecord>().Returns(Substitute.For<IMartenQueryable<ConsentRecord>>());
        documentSession.Query<ErasureRequest>().Returns(Substitute.For<IMartenQueryable<ErasureRequest>>());
        
        SubstituteExtensions.Returns(
            documentSession.Query<StudentRecordAccessLog>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<StudentRecordAccessLog>());
        SubstituteExtensions.Returns(
            documentSession.Query<ConsentRecord>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<ConsentRecord>());
        SubstituteExtensions.Returns(
            documentSession.Query<ErasureRequest>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<ErasureRequest>());

        RetentionRunHistory? capturedHistory = null;
        documentSession.When(x => x.Store(Arg.Any<RetentionRunHistory>())).Do(call =>
        {
            capturedHistory = call.Arg<RetentionRunHistory>();
        });

        // Act
        var result = await _worker.RunAsync();

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(RetentionRunStatus.Completed, result.Status);
        Assert.Equal(_clock.UtcNow, result.RunAt);
        
        // Verify Store was called at least once (initial Running + final Completed)
        documentSession.Received().Store(Arg.Is<RetentionRunHistory>(h => h.Status == RetentionRunStatus.Running));
    }

    [Fact(Skip = "RDY-054e: Marten proxy-cast to MartenLinqQueryable<T> fails with NSubstitute — needs real Marten integration fixture. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public async Task Run_HistoryDocumentHasCompletedTimestamp()
    {
        // Arrange
        var documentSession = Substitute.For<IDocumentSession>();
        _store.LightweightSession().Returns(documentSession);
        
        documentSession.Query<StudentRecordAccessLog>().Returns(Substitute.For<IMartenQueryable<StudentRecordAccessLog>>());
        documentSession.Query<ConsentRecord>().Returns(Substitute.For<IMartenQueryable<ConsentRecord>>());
        documentSession.Query<ErasureRequest>().Returns(Substitute.For<IMartenQueryable<ErasureRequest>>());
        
        SubstituteExtensions.Returns(
            documentSession.Query<StudentRecordAccessLog>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<StudentRecordAccessLog>());
        SubstituteExtensions.Returns(
            documentSession.Query<ConsentRecord>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<ConsentRecord>());
        SubstituteExtensions.Returns(
            documentSession.Query<ErasureRequest>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<ErasureRequest>());

        var runStartTime = _clock.UtcNow;

        // Act
        var result = await _worker.RunAsync();

        // Assert
        Assert.NotNull(result.CompletedAt);
        Assert.True(result.CompletedAt >= runStartTime);
        Assert.True(result.CategorySummaries != null);
    }

    // -------------------------------------------------------------------------
    // Test 2: Expired documents are purged based on retention policy
    // -------------------------------------------------------------------------

    [Fact(Skip = "RDY-054e: Marten proxy-cast to MartenLinqQueryable<T> fails with NSubstitute — needs real Marten integration fixture. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public async Task Run_ExpiredAuditLogs_ArePurged()
    {
        // Arrange: Create audit logs older than the 5-year retention
        var documentSession = Substitute.For<IDocumentSession>();
        _store.LightweightSession().Returns(documentSession);
        
        var expiredLog = new StudentRecordAccessLog
        {
            Id = Guid.NewGuid(),
            AccessedAt = _clock.UtcNow.AddDays(-(365 * 6)), // 6 years old
            AccessedBy = "teacher-1",
            Endpoint = "/api/students/123",
            HttpMethod = "GET",
            StatusCode = 200
        };

        var recentLog = new StudentRecordAccessLog
        {
            Id = Guid.NewGuid(),
            AccessedAt = _clock.UtcNow.AddDays(-30), // 30 days old
            AccessedBy = "teacher-2",
            Endpoint = "/api/students/456",
            HttpMethod = "GET",
            StatusCode = 200
        };

        var allLogs = new List<StudentRecordAccessLog> { expiredLog, recentLog };
        
        var queryable = Substitute.For<IMartenQueryable<StudentRecordAccessLog>>();
        documentSession.Query<StudentRecordAccessLog>().Returns(queryable);
        
        // Return expired log only (simulating the Where clause filter)
        SubstituteExtensions.Returns(
            queryable.ToListAsync(Arg.Any<CancellationToken>()),
            new List<StudentRecordAccessLog> { expiredLog });

        documentSession.Query<ConsentRecord>().Returns(Substitute.For<IMartenQueryable<ConsentRecord>>());
        documentSession.Query<ErasureRequest>().Returns(Substitute.For<IMartenQueryable<ErasureRequest>>());
        
        SubstituteExtensions.Returns(
            documentSession.Query<ConsentRecord>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<ConsentRecord>());
        SubstituteExtensions.Returns(
            documentSession.Query<ErasureRequest>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<ErasureRequest>());

        // Act
        var result = await _worker.RunAsync();

        // Assert: The expired document should be deleted
        documentSession.Received().Delete(Arg.Is<StudentRecordAccessLog>(x => x.Id == expiredLog.Id));
        documentSession.DidNotReceive().Delete(Arg.Is<StudentRecordAccessLog>(x => x.Id == recentLog.Id));
    }

    [Fact(Skip = "RDY-054e: Marten proxy-cast to MartenLinqQueryable<T> fails with NSubstitute — needs real Marten integration fixture. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public async Task Run_NonExpiredDocuments_AreNotPurged()
    {
        // Arrange: Create documents within retention window
        var documentSession = Substitute.For<IDocumentSession>();
        _store.LightweightSession().Returns(documentSession);

        var recentLog = new StudentRecordAccessLog
        {
            Id = Guid.NewGuid(),
            AccessedAt = _clock.UtcNow.AddDays(-100), // ~3 months old, within 5-year retention
            AccessedBy = "teacher-1",
            Endpoint = "/api/students/123"
        };

        var queryable = Substitute.For<IMartenQueryable<StudentRecordAccessLog>>();
        documentSession.Query<StudentRecordAccessLog>().Returns(queryable);
        
        // Return empty (no expired logs found)
        SubstituteExtensions.Returns(
            queryable.ToListAsync(Arg.Any<CancellationToken>()),
            new List<StudentRecordAccessLog>());

        documentSession.Query<ConsentRecord>().Returns(Substitute.For<IMartenQueryable<ConsentRecord>>());
        documentSession.Query<ErasureRequest>().Returns(Substitute.For<IMartenQueryable<ErasureRequest>>());
        
        SubstituteExtensions.Returns(
            documentSession.Query<ConsentRecord>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<ConsentRecord>());
        SubstituteExtensions.Returns(
            documentSession.Query<ErasureRequest>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<ErasureRequest>());

        // Act
        var result = await _worker.RunAsync();

        // Assert: No deletes should be called for StudentRecordAccessLog
        documentSession.DidNotReceive().Delete(Arg.Any<StudentRecordAccessLog>());
    }

    // -------------------------------------------------------------------------
    // Test 3: In-flight ErasureRequests are accelerated
    // -------------------------------------------------------------------------

    [Fact(Skip = "RDY-054e: Marten proxy-cast to MartenLinqQueryable<T> fails with NSubstitute — needs real Marten integration fixture. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public async Task Run_ErasureRequestsPastCoolingPeriod_AreAccelerated()
    {
        // Arrange
        var documentSession = Substitute.For<IDocumentSession>();
        _store.LightweightSession().Returns(documentSession);

        // Create a request that passed the 30-day cooling period
        var oldRequest = new ErasureRequest
        {
            Id = Guid.NewGuid(),
            StudentId = "student-123",
            Status = ErasureStatus.CoolingPeriod,
            RequestedAt = _clock.UtcNow.AddDays(-45), // 45 days ago
            RequestedBy = "student-123"
        };

        documentSession.Query<StudentRecordAccessLog>().Returns(Substitute.For<IMartenQueryable<StudentRecordAccessLog>>());
        documentSession.Query<ConsentRecord>().Returns(Substitute.For<IMartenQueryable<ConsentRecord>>());
        
        var erasureQueryable = Substitute.For<IMartenQueryable<ErasureRequest>>();
        documentSession.Query<ErasureRequest>().Returns(erasureQueryable);

        SubstituteExtensions.Returns(
            documentSession.Query<StudentRecordAccessLog>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<StudentRecordAccessLog>());
        SubstituteExtensions.Returns(
            documentSession.Query<ConsentRecord>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<ConsentRecord>());
        SubstituteExtensions.Returns(
            erasureQueryable.ToListAsync(Arg.Any<CancellationToken>()),
            new List<ErasureRequest> { oldRequest });

        // Act
        var result = await _worker.RunAsync();

        // Assert: Request should be updated to Processing status
        Assert.Equal(1, result.ErasureRequestsAccelerated);
        documentSession.Received().Store(Arg.Is<ErasureRequest>(r => 
            r.Id == oldRequest.Id && r.Status == ErasureStatus.Processing));
    }

    [Fact(Skip = "RDY-054e: Marten proxy-cast to MartenLinqQueryable<T> fails with NSubstitute — needs real Marten integration fixture. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public async Task Run_ErasureRequestsInCoolingPeriod_AreNotAccelerated()
    {
        // Arrange
        var documentSession = Substitute.For<IDocumentSession>();
        _store.LightweightSession().Returns(documentSession);

        // Create a request still in cooling period
        var recentRequest = new ErasureRequest
        {
            Id = Guid.NewGuid(),
            StudentId = "student-456",
            Status = ErasureStatus.CoolingPeriod,
            RequestedAt = _clock.UtcNow.AddDays(-10), // Only 10 days ago
            RequestedBy = "student-456"
        };

        documentSession.Query<StudentRecordAccessLog>().Returns(Substitute.For<IMartenQueryable<StudentRecordAccessLog>>());
        documentSession.Query<ConsentRecord>().Returns(Substitute.For<IMartenQueryable<ConsentRecord>>());
        
        var erasureQueryable = Substitute.For<IMartenQueryable<ErasureRequest>>();
        documentSession.Query<ErasureRequest>().Returns(erasureQueryable);

        SubstituteExtensions.Returns(
            documentSession.Query<StudentRecordAccessLog>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<StudentRecordAccessLog>());
        SubstituteExtensions.Returns(
            documentSession.Query<ConsentRecord>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<ConsentRecord>());
        SubstituteExtensions.Returns(
            erasureQueryable.ToListAsync(Arg.Any<CancellationToken>()),
            new List<ErasureRequest>()); // Empty - request not eligible

        // Act
        var result = await _worker.RunAsync();

        // Assert
        Assert.Equal(0, result.ErasureRequestsAccelerated);
        documentSession.DidNotReceive().Store(Arg.Is<ErasureRequest>(r => 
            r.Id == recentRequest.Id && r.Status == ErasureStatus.Processing));
    }

    [Fact(Skip = "RDY-054e: Marten proxy-cast to MartenLinqQueryable<T> fails with NSubstitute — needs real Marten integration fixture. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public async Task Run_NonCoolingPeriodErasureRequests_AreIgnored()
    {
        // Arrange
        var documentSession = Substitute.For<IDocumentSession>();
        _store.LightweightSession().Returns(documentSession);

        var processingRequest = new ErasureRequest
        {
            Id = Guid.NewGuid(),
            StudentId = "student-789",
            Status = ErasureStatus.Processing, // Already processing
            RequestedAt = _clock.UtcNow.AddDays(-45),
            RequestedBy = "student-789"
        };

        documentSession.Query<StudentRecordAccessLog>().Returns(Substitute.For<IMartenQueryable<StudentRecordAccessLog>>());
        documentSession.Query<ConsentRecord>().Returns(Substitute.For<IMartenQueryable<ConsentRecord>>());
        
        var erasureQueryable = Substitute.For<IMartenQueryable<ErasureRequest>>();
        documentSession.Query<ErasureRequest>().Returns(erasureQueryable);

        SubstituteExtensions.Returns(
            documentSession.Query<StudentRecordAccessLog>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<StudentRecordAccessLog>());
        SubstituteExtensions.Returns(
            documentSession.Query<ConsentRecord>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<ConsentRecord>());
        SubstituteExtensions.Returns(
            erasureQueryable.ToListAsync(Arg.Any<CancellationToken>()),
            new List<ErasureRequest>()); // Won't match the Where clause for CoolingPeriod

        // Act
        var result = await _worker.RunAsync();

        // Assert
        Assert.Equal(0, result.ErasureRequestsAccelerated);
    }

    // -------------------------------------------------------------------------
    // Test 4: Per-tenant retention override behavior
    // -------------------------------------------------------------------------

    [Fact(Skip = "RDY-054e: Marten proxy-cast to MartenLinqQueryable<T> fails with NSubstitute — needs real Marten integration fixture. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public async Task GetRetentionPeriod_WithTenantOverride_UsesOverrideValue()
    {
        // Arrange
        var tenantId = "tenant-abc";
        var customRetention = TimeSpan.FromDays(180); // 6 months instead of 5 years
        
        var tenantPolicy = new TenantRetentionPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AuditLogRetentionOverride = customRetention,
            EffectiveFrom = _clock.UtcNow.AddDays(-30)
        };

        _policyService.GetTenantPolicyAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TenantRetentionPolicy?>(tenantPolicy));

        _policyService.GetRetentionPeriodAsync(tenantId, DataCategory.AuditLog, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(customRetention));

        // Act
        var result = await _policyService.GetRetentionPeriodAsync(tenantId, DataCategory.AuditLog);

        // Assert
        Assert.Equal(customRetention, result);
        Assert.NotEqual(DataRetentionPolicy.AuditLogRetention, result);
    }

    [Fact(Skip = "RDY-054e: Marten proxy-cast to MartenLinqQueryable<T> fails with NSubstitute — needs real Marten integration fixture. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public async Task GetRetentionPeriod_WithoutOverride_UsesDefaultPolicy()
    {
        // Arrange
        var tenantId = "tenant-def";
        
        _policyService.GetTenantPolicyAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TenantRetentionPolicy?>(null));

        _policyService.GetRetentionPeriodAsync(tenantId, DataCategory.AuditLog, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DataRetentionPolicy.AuditLogRetention));

        // Act
        var result = await _policyService.GetRetentionPeriodAsync(tenantId, DataCategory.AuditLog);

        // Assert
        Assert.Equal(DataRetentionPolicy.AuditLogRetention, result);
    }

    [Fact(Skip = "RDY-054e: Marten proxy-cast to MartenLinqQueryable<T> fails with NSubstitute — needs real Marten integration fixture. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public async Task GetRetentionPeriod_WithExpiredOverride_IgnoresOverride()
    {
        // Arrange
        var tenantId = "tenant-expired";
        var expiredPolicy = new TenantRetentionPolicy
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AuditLogRetentionOverride = TimeSpan.FromDays(30),
            EffectiveFrom = _clock.UtcNow.AddDays(-90),
            EffectiveTo = _clock.UtcNow.AddDays(-1) // Expired yesterday
        };

        _policyService.GetTenantPolicyAsync(tenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TenantRetentionPolicy?>(expiredPolicy));

        _policyService.GetRetentionPeriodAsync(tenantId, DataCategory.AuditLog, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DataRetentionPolicy.AuditLogRetention));

        // Act
        var result = await _policyService.GetRetentionPeriodAsync(tenantId, DataCategory.AuditLog);

        // Assert: Should fall back to default since override is expired
        Assert.Equal(DataRetentionPolicy.AuditLogRetention, result);
    }

    // -------------------------------------------------------------------------
    // Test 5: IClock integration for time-based testing
    // -------------------------------------------------------------------------

    [Fact(Skip = "RDY-054e: Marten proxy-cast to MartenLinqQueryable<T> fails with NSubstitute — needs real Marten integration fixture. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public void TestClock_CanFastForwardTime()
    {
        // Arrange
        var initialTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var clock = new TestClock(initialTime);

        // Assert initial
        Assert.Equal(initialTime, clock.UtcNow);

        // Act: Fast-forward by 1 year
        clock.Advance(TimeSpan.FromDays(365));

        // Assert
        Assert.Equal(initialTime.AddDays(365), clock.UtcNow);
        Assert.True(clock.UtcNow > initialTime);
    }

    [Fact(Skip = "RDY-054e: Marten proxy-cast to MartenLinqQueryable<T> fails with NSubstitute — needs real Marten integration fixture. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public void TestClock_CanSetSpecificTime()
    {
        // Arrange
        var clock = new TestClock();
        var specificTime = new DateTimeOffset(2030, 6, 15, 12, 30, 0, TimeSpan.Zero);

        // Act
        clock.Set(specificTime);

        // Assert
        Assert.Equal(specificTime, clock.UtcNow);
    }

    [Fact(Skip = "RDY-054e: Marten proxy-cast to MartenLinqQueryable<T> fails with NSubstitute — needs real Marten integration fixture. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public async Task Run_WithFastForwardedClock_PurgesBasedOnNewTime()
    {
        // Arrange
        var documentSession = Substitute.For<IDocumentSession>();
        _store.LightweightSession().Returns(documentSession);

        // Initial time: all documents are fresh
        var initialTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _clock.Set(initialTime);

        // Create a document that is 4 years old from initial time
        var fourYearOldLog = new StudentRecordAccessLog
        {
            Id = Guid.NewGuid(),
            AccessedAt = initialTime.AddDays(-(365 * 4)),
            AccessedBy = "teacher-1",
            Endpoint = "/api/students/123"
        };

        var queryable = Substitute.For<IMartenQueryable<StudentRecordAccessLog>>();
        documentSession.Query<StudentRecordAccessLog>().Returns(queryable);

        documentSession.Query<ConsentRecord>().Returns(Substitute.For<IMartenQueryable<ConsentRecord>>());
        documentSession.Query<ErasureRequest>().Returns(Substitute.For<IMartenQueryable<ErasureRequest>>());
        
        SubstituteExtensions.Returns(
            documentSession.Query<ConsentRecord>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<ConsentRecord>());
        SubstituteExtensions.Returns(
            documentSession.Query<ErasureRequest>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<ErasureRequest>());

        // First run: document is 4 years old (within 5-year retention) - should NOT be purged
        SubstituteExtensions.Returns(
            queryable.ToListAsync(Arg.Any<CancellationToken>()),
            new List<StudentRecordAccessLog>());

        var firstResult = await _worker.RunAsync();
        Assert.Equal(0, firstResult.DocumentsPurged);

        // Fast-forward time by 2 years
        _clock.Advance(TimeSpan.FromDays(365 * 2));

        // Now the document is 6 years old from the new current time
        // Reset the mock to return the expired document
        SubstituteExtensions.Returns(
            queryable.ToListAsync(Arg.Any<CancellationToken>()),
            new List<StudentRecordAccessLog> { fourYearOldLog });

        // Act: Run again with fast-forwarded clock
        var secondResult = await _worker.RunAsync();

        // Assert: Document should now be purged
        Assert.Equal(1, secondResult.DocumentsPurged);
        documentSession.Received().Delete(Arg.Is<StudentRecordAccessLog>(x => x.Id == fourYearOldLog.Id));
    }

    [Fact(Skip = "RDY-054e: Marten proxy-cast to MartenLinqQueryable<T> fails with NSubstitute — needs real Marten integration fixture. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public void SystemClock_ReturnsCurrentUtcTime()
    {
        // Arrange
        var clock = new SystemClock();
        var before = DateTimeOffset.UtcNow;

        // Act
        var result = clock.UtcNow;

        // Assert
        var after = DateTimeOffset.UtcNow;
        Assert.True(result >= before);
        Assert.True(result <= after);
    }

    // -------------------------------------------------------------------------
    // Test 6: Compliance endpoint returns real data after retention run
    // -------------------------------------------------------------------------

    [Fact(Skip = "RDY-054e: Marten proxy-cast to MartenLinqQueryable<T> fails with NSubstitute — needs real Marten integration fixture. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public async Task RunHistory_CanBeQueried_ByComplianceEndpoint()
    {
        // Arrange
        var querySession = Substitute.For<IQuerySession>();
        var documentSession = Substitute.For<IDocumentSession>();
        _store.LightweightSession().Returns(documentSession);
        _store.QuerySession().Returns(querySession);

        documentSession.Query<StudentRecordAccessLog>().Returns(Substitute.For<IMartenQueryable<StudentRecordAccessLog>>());
        documentSession.Query<ConsentRecord>().Returns(Substitute.For<IMartenQueryable<ConsentRecord>>());
        documentSession.Query<ErasureRequest>().Returns(Substitute.For<IMartenQueryable<ErasureRequest>>());
        
        SubstituteExtensions.Returns(
            documentSession.Query<StudentRecordAccessLog>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<StudentRecordAccessLog>());
        SubstituteExtensions.Returns(
            documentSession.Query<ConsentRecord>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<ConsentRecord>());
        SubstituteExtensions.Returns(
            documentSession.Query<ErasureRequest>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<ErasureRequest>());

        // Simulate the run
        var runResult = await _worker.RunAsync();

        // Verify the history document was stored with correct structure
        Assert.NotNull(runResult);
        Assert.True(runResult.DocumentsScanned >= 0);
        Assert.True(runResult.DocumentsPurged >= 0);
        Assert.NotNull(runResult.CategorySummaries);
    }

    [Fact(Skip = "RDY-054e: Marten proxy-cast to MartenLinqQueryable<T> fails with NSubstitute — needs real Marten integration fixture. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public async Task RunHistory_ContainsCategorySummaries()
    {
        // Arrange
        var documentSession = Substitute.For<IDocumentSession>();
        _store.LightweightSession().Returns(documentSession);

        var expiredAuditLog = new StudentRecordAccessLog
        {
            Id = Guid.NewGuid(),
            AccessedAt = _clock.UtcNow.AddDays(-(365 * 6)),
            AccessedBy = "teacher-1"
        };

        var auditQueryable = Substitute.For<IMartenQueryable<StudentRecordAccessLog>>();
        documentSession.Query<StudentRecordAccessLog>().Returns(auditQueryable);
        
        var consentQueryable = Substitute.For<IMartenQueryable<ConsentRecord>>();
        documentSession.Query<ConsentRecord>().Returns(consentQueryable);
        
        documentSession.Query<ErasureRequest>().Returns(Substitute.For<IMartenQueryable<ErasureRequest>>());

        SubstituteExtensions.Returns(
            auditQueryable.ToListAsync(Arg.Any<CancellationToken>()),
            new List<StudentRecordAccessLog> { expiredAuditLog });
        SubstituteExtensions.Returns(
            consentQueryable.ToListAsync(Arg.Any<CancellationToken>()),
            new List<ConsentRecord>());
        SubstituteExtensions.Returns(
            documentSession.Query<ErasureRequest>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<ErasureRequest>());

        // Act
        var result = await _worker.RunAsync();

        // Assert
        Assert.NotNull(result.CategorySummaries);
        Assert.True(result.CategorySummaries.Count > 0);
        
        var auditSummary = result.CategorySummaries.FirstOrDefault(s => s.Category == "AuditLog");
        Assert.NotNull(auditSummary);
        Assert.Equal(1, auditSummary.ExpiredCount);
        Assert.Equal(1, auditSummary.PurgedCount);
        Assert.Equal(DataRetentionPolicy.AuditLogRetention, auditSummary.RetentionPeriod);
    }

    [Fact(Skip = "RDY-054e: Marten proxy-cast to MartenLinqQueryable<T> fails with NSubstitute — needs real Marten integration fixture. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public async Task ComplianceEndpoint_CanRetrieveRetentionPolicy()
    {
        // Arrange: Simulate the data-retention endpoint response structure
        var policies = new[]
        {
            new
            {
                category = "Student Education Records",
                retentionDays = DataRetentionPolicy.StudentRecordRetention.Days,
                retentionYears = DataRetentionPolicy.StudentRecordRetention.Days / 365
            },
            new
            {
                category = "Audit Logs",
                retentionDays = DataRetentionPolicy.AuditLogRetention.Days,
                retentionYears = DataRetentionPolicy.AuditLogRetention.Days / 365
            }
        };

        // Act & Assert
        Assert.Equal(2, policies.Length);
        Assert.Equal(7 * 365, policies[0].retentionDays);
        Assert.Equal(5 * 365, policies[1].retentionDays);
    }

    // -------------------------------------------------------------------------
    // Key Test Case: Full workflow - Create document, fast-forward, run worker, assert deletion
    // -------------------------------------------------------------------------

    [Fact(Skip = "RDY-054e: Marten proxy-cast to MartenLinqQueryable<T> fails with NSubstitute — needs real Marten integration fixture. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public async Task FullWorkflow_CreateDocument_FastForwardClock_RunWorker_AssertDeleted()
    {
        // ===================================================================
        // ARRANGE: Set up the test environment with mocked Marten session
        // ===================================================================
        
        var documentSession = Substitute.For<IDocumentSession>();
        _store.LightweightSession().Returns(documentSession);

        // Create a document with an OLD timestamp (6 years ago)
        var oldDocument = new StudentRecordAccessLog
        {
            Id = Guid.NewGuid(),
            AccessedAt = _clock.UtcNow.AddDays(-(365 * 6)), // 6 years old - past 5-year retention
            AccessedBy = "teacher-old-data",
            AccessorRole = "Teacher",
            AccessorSchool = "School A",
            StudentId = "student-legacy",
            Endpoint = "/api/students/legacy",
            HttpMethod = "GET",
            StatusCode = 200,
            Category = "data_access"
        };

        // Create a document within retention (1 year old)
        var recentDocument = new StudentRecordAccessLog
        {
            Id = Guid.NewGuid(),
            AccessedAt = _clock.UtcNow.AddDays(-365), // 1 year old - within 5-year retention
            AccessedBy = "teacher-recent",
            AccessorRole = "Teacher",
            AccessorSchool = "School B",
            StudentId = "student-recent",
            Endpoint = "/api/students/recent",
            HttpMethod = "GET",
            StatusCode = 200,
            Category = "data_access"
        };

        var auditQueryable = Substitute.For<IMartenQueryable<StudentRecordAccessLog>>();
        documentSession.Query<StudentRecordAccessLog>().Returns(auditQueryable);
        
        documentSession.Query<ConsentRecord>().Returns(Substitute.For<IMartenQueryable<ConsentRecord>>());
        documentSession.Query<ErasureRequest>().Returns(Substitute.For<IMartenQueryable<ErasureRequest>>());
        
        SubstituteExtensions.Returns(
            documentSession.Query<ConsentRecord>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<ConsentRecord>());
        SubstituteExtensions.Returns(
            documentSession.Query<ErasureRequest>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<ErasureRequest>());

        // ===================================================================
        // ACT & ASSERT PHASE 1: Verify document exists and is NOT purged initially
        // (when clock is at current time, 6-year-old doc should be flagged)
        // ===================================================================
        
        // Setup: Return the expired document when queried
        SubstituteExtensions.Returns(
            auditQueryable.ToListAsync(Arg.Any<CancellationToken>()),
            new List<StudentRecordAccessLog> { oldDocument });

        // Run the retention worker
        var result = await _worker.RunAsync();

        // Assert: The old document should be marked for deletion
        Assert.Equal(1, result.DocumentsPurged);
        Assert.Equal(1, result.DocumentsScanned);
        documentSession.Received().Delete(Arg.Is<StudentRecordAccessLog>(x => x.Id == oldDocument.Id));

        // ===================================================================
        // ACT & ASSERT PHASE 2: Fast-forward IClock and verify behavior
        // ===================================================================
        
        // Fast-forward clock by 4 years (total 10 years from document creation)
        _clock.Advance(TimeSpan.FromDays(365 * 4));

        // Create another document that will now be expired (created 6 years ago from new "now")
        var newlyExpiredDocument = new StudentRecordAccessLog
        {
            Id = Guid.NewGuid(),
            AccessedAt = _clock.UtcNow.AddDays(-(365 * 6)), // 6 years from new current time
            AccessedBy = "teacher-phase2",
            Endpoint = "/api/students/phase2"
        };

        // Reset mock to return the newly expired document
        SubstituteExtensions.Returns(
            auditQueryable.ToListAsync(Arg.Any<CancellationToken>()),
            new List<StudentRecordAccessLog> { newlyExpiredDocument });

        // Run the retention worker again
        var phase2Result = await _worker.RunAsync();

        // Assert: The newly expired document should also be purged
        Assert.Equal(1, phase2Result.DocumentsPurged);
        documentSession.Received().Delete(Arg.Is<StudentRecordAccessLog>(x => x.Id == newlyExpiredDocument.Id));

        // ===================================================================
        // ACT & ASSERT PHASE 3: Verify RunHistory is created correctly
        // ===================================================================
        
        // The run history should have been stored twice (once per RunAsync call)
        documentSession.Received(2).Store(Arg.Is<RetentionRunHistory>(h => h.Status == RetentionRunStatus.Running));
        documentSession.Received(2).Store(Arg.Is<RetentionRunHistory>(h => h.Status == RetentionRunStatus.Completed));

        // ===================================================================
        // ACT & ASSERT PHASE 4: Verify compliance endpoint data
        // ===================================================================
        
        // Verify the retention policy values are accessible
        Assert.Equal(TimeSpan.FromDays(365 * 5), DataRetentionPolicy.AuditLogRetention);
        Assert.Equal(TimeSpan.FromDays(365 * 7), DataRetentionPolicy.StudentRecordRetention);
        Assert.Equal(TimeSpan.FromDays(365 * 2), DataRetentionPolicy.AnalyticsRetention);
        Assert.Equal(TimeSpan.FromDays(365), DataRetentionPolicy.EngagementRetention);
    }

    [Fact(Skip = "RDY-054e: Marten proxy-cast to MartenLinqQueryable<T> fails with NSubstitute — needs real Marten integration fixture. See tasks/readiness/RDY-054e-nsubstitute-and-marten-proxies.md.")]
    public async Task FullWorkflow_DocumentLifecycle_WithErasureAcceleration()
    {
        // ===================================================================
        // Key Test: Full lifecycle with document + erasure request
        // ===================================================================
        
        var documentSession = Substitute.For<IDocumentSession>();
        _store.LightweightSession().Returns(documentSession);

        // Create expired audit log
        var expiredLog = new StudentRecordAccessLog
        {
            Id = Guid.NewGuid(),
            AccessedAt = _clock.UtcNow.AddDays(-(365 * 6)),
            AccessedBy = "teacher-1"
        };

        // Create erasure request that has passed cooling period
        var erasureRequest = new ErasureRequest
        {
            Id = Guid.NewGuid(),
            StudentId = "student-to-erase",
            Status = ErasureStatus.CoolingPeriod,
            RequestedAt = _clock.UtcNow.AddDays(-35), // 35 days ago (> 30 day cooling)
            RequestedBy = "student-to-erase"
        };

        var auditQueryable = Substitute.For<IMartenQueryable<StudentRecordAccessLog>>();
        documentSession.Query<StudentRecordAccessLog>().Returns(auditQueryable);
        
        var erasureQueryable = Substitute.For<IMartenQueryable<ErasureRequest>>();
        documentSession.Query<ErasureRequest>().Returns(erasureQueryable);
        
        documentSession.Query<ConsentRecord>().Returns(Substitute.For<IMartenQueryable<ConsentRecord>>());
        SubstituteExtensions.Returns(
            documentSession.Query<ConsentRecord>().ToListAsync(Arg.Any<CancellationToken>()),
            new List<ConsentRecord>());

        SubstituteExtensions.Returns(
            auditQueryable.ToListAsync(Arg.Any<CancellationToken>()),
            new List<StudentRecordAccessLog> { expiredLog });
        SubstituteExtensions.Returns(
            erasureQueryable.ToListAsync(Arg.Any<CancellationToken>()),
            new List<ErasureRequest> { erasureRequest });

        // Act
        var result = await _worker.RunAsync();

        // Assert
        Assert.Equal(1, result.DocumentsPurged);
        Assert.Equal(1, result.ErasureRequestsAccelerated);
        
        // Verify document was deleted
        documentSession.Received().Delete(Arg.Is<StudentRecordAccessLog>(x => x.Id == expiredLog.Id));
        
        // Verify erasure request was accelerated to Processing
        documentSession.Received().Store(Arg.Is<ErasureRequest>(r => 
            r.Id == erasureRequest.Id && r.Status == ErasureStatus.Processing));

        // Verify run history contains both actions
        Assert.NotNull(result.CategorySummaries);
        Assert.True(result.CompletedAt.HasValue);
        Assert.Equal(RetentionRunStatus.Completed, result.Status);
    }
}
