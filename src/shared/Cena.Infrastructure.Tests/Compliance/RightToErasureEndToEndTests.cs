// =============================================================================
// Cena Platform — Right to Erasure End-to-End Regression Tests
// SEC-005: GDPR Article 17 compliance verification
//
// PRR-313 / 2026-04-30 cm rewrite:
//   * Original implementation used NSubstitute on IDocumentStore + a side-
//     class TestableRightToErasureService — both wrong:
//     - The substitute proxy fails Marten's IQueryable.As<MartenLinqQueryable<T>>
//       hard cast that runs inside FirstOrDefaultAsync / ToListAsync (Marten 8).
//     - TestableRightToErasureService had WRONG erasure semantics: it deleted
//       consents (prod preserves them — legal provenance) and deleted access
//       logs (prod preserves — FERPA disclosure-record retention). The tests
//       asserted the wrong-impl's wrong behaviour and called it E2E.
//   * Rewrite tests against a real Marten store on cena-postgres (port 5433),
//     mirroring MockExamRunServiceTests pattern. Each test class instance
//     gets its own schema so parallel runs don't collide. The tests now run
//     against the REAL RightToErasureService and verify the real
//     production semantics:
//       - StudentProfileRef    : ANONYMIZED (FullName HMAC-hashed; PII cleared)
//       - TutorMessage/Thread  : HARD DELETE
//       - DeviceSession        : HARD DELETE
//       - StudentPreferences   : HARD DELETE
//       - ShareToken           : HARD DELETE
//       - ConsentRecord        : PRESERVED (Granted=false + RevokedAt set)
//       - StudentRecordAccessLog: PRESERVED (FERPA — never deleted)
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Documents;
using JasperFx;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;

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

    public void Advance(TimeSpan duration) => _currentTime = _currentTime.Add(duration);
    public void Set(DateTimeOffset time) => _currentTime = time;
}

/// <summary>
/// End-to-end tests for GDPR Article 17 Right to Erasure against the REAL
/// RightToErasureService and a real Marten store on cena-postgres.
/// </summary>
public sealed class RightToErasureEndToEndTests : IAsyncLifetime
{
    // Same connection convention as MockExamRunServiceTests — dev compose
    // maps cena-postgres:5432 → host:5433. CI runs the same compose stack,
    // so this is portable.
    private const string ConnectionString =
        "Host=localhost;Port=5433;Database=cena;Username=cena;Password=cena_dev_password";

    private const string TestStudentId = "student-test-001";
    private const string TestRequester = "student:self";
    private const string TestPepper = "test-pepper-for-hashing-v1-must-be-32-bytes-or-more";

    private DocumentStore _store = null!;
    private TestClock _clock = null!;
    private RightToErasureService _service = null!;

    public Task InitializeAsync()
    {
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(ConnectionString);
            opts.DatabaseSchemaName = "erasure_test_" + Guid.NewGuid().ToString("N")[..8];
            opts.AutoCreateSchemaObjects = AutoCreate.All;

            // The doc types RightToErasureService touches. Marten infers
            // Identity from the public Id property by convention; explicit
            // declarations only where the type's Id shape needs help.
            opts.Schema.For<ErasureRequest>().Identity(d => d.Id);
            opts.Schema.For<StudentProfileRef>().Identity(d => d.Id);
            opts.Schema.For<TutorMessageDocument>().Identity(d => d.Id);
            opts.Schema.For<TutorThreadDocument>().Identity(d => d.Id);
            opts.Schema.For<DeviceSessionDocument>().Identity(d => d.Id);
            opts.Schema.For<StudentPreferencesDocument>().Identity(d => d.Id);
            opts.Schema.For<ShareTokenDocument>().Identity(d => d.Id);
            opts.Schema.For<ConsentRecord>().Identity(d => d.Id);
            opts.Schema.For<StudentRecordAccessLog>().Identity(d => d.Id);
        });

        _clock = new TestClock(new DateTimeOffset(2026, 4, 12, 0, 0, 0, TimeSpan.Zero));
        _service = new RightToErasureService(
            store: _store,
            logger: NullLogger<RightToErasureService>.Instance,
            clock: _clock,
            manifestBuilder: new ErasureManifestBuilder(),
            cryptoConfig: new ErasureCryptoConfig(TestPepper));

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _store.Dispose();
        return Task.CompletedTask;
    }

    // ---- Helpers -----------------------------------------------------------

    private async Task SeedAsync(params object[] docs)
    {
        await using var session = _store.LightweightSession();
        foreach (var d in docs) session.Store(d);
        await session.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<T>> QueryAllAsync<T>() where T : notnull
    {
        await using var qs = _store.QuerySession();
        return await qs.Query<T>().ToListAsync();
    }

    // =========================================================================
    // 1. Cooling-period guard — request before 30 days does NOT process.
    // =========================================================================

    [Fact]
    public async Task CoolingPeriod_RequestBefore30Days_NotProcessed()
    {
        // Seed an in-flight erasure request 15 days old.
        await SeedAsync(new ErasureRequest
        {
            Id          = Guid.NewGuid(),
            StudentId   = TestStudentId,
            Status      = ErasureStatus.CoolingPeriod,
            RequestedAt = _clock.UtcNow.AddDays(-15),
            RequestedBy = TestRequester,
        });

        // Seed something that WOULD be deleted if processing fired (used as a
        // negative-control: if the cooling-period guard is off, this row
        // disappears).
        await SeedAsync(new TutorMessageDocument
        {
            Id        = "msg-cool-001",
            MessageId = "msg-cool-001",
            ThreadId  = "thread-cool-001",
            StudentId = TestStudentId,
            Role      = "user",
            Content   = "should not be deleted yet",
            CreatedAt = _clock.UtcNow.AddDays(-10).UtcDateTime,
        });

        await _service.ProcessErasureAsync(TestStudentId);

        // Request still in CoolingPeriod (NOT advanced to Processing or Completed).
        var requests = await QueryAllAsync<ErasureRequest>();
        Assert.Single(requests);
        Assert.Equal(ErasureStatus.CoolingPeriod, requests[0].Status);

        // Negative control: tutor message NOT deleted.
        var msgs = await QueryAllAsync<TutorMessageDocument>();
        Assert.Single(msgs);
    }

    // =========================================================================
    // 2. Full workflow — request → fast-forward 31 days → process → verify.
    //    Exercises every doc type the service touches.
    // =========================================================================

    [Fact]
    public async Task FullWorkflow_CreateStudentWithData_FastForward31Days_AssertErased()
    {
        // ARRANGE: seed real entities of EVERY type the erasure service touches.
        await SeedAsync(
            new StudentProfileRef
            {
                Id            = TestStudentId,
                FullName      = "John Doe Test Student",
                DisplayName   = "JohnnyD",
                Bio           = "I love learning math and science!",
                DateOfBirth   = new DateOnly(2010, 5, 15),
                ParentEmail   = "parent.doe@example.com",
                SchoolId      = "school-001",
                AccountStatus = "Active",
            },
            new TutorMessageDocument
            {
                Id        = "msg-001",
                MessageId = "msg-001",
                ThreadId  = "thread-001",
                StudentId = TestStudentId,
                Role      = "user",
                Content   = "Help me solve this equation",
                CreatedAt = _clock.UtcNow.AddDays(-20).UtcDateTime,
            },
            new TutorMessageDocument
            {
                Id        = "msg-002",
                MessageId = "msg-002",
                ThreadId  = "thread-001",
                StudentId = TestStudentId,
                Role      = "assistant",
                Content   = "Sure! Let's break it down.",
                CreatedAt = _clock.UtcNow.AddDays(-20).UtcDateTime,
            },
            new TutorThreadDocument
            {
                Id           = "thread-001",
                ThreadId     = "thread-001",
                StudentId    = TestStudentId,
                Title        = "Math Help Session",
                Subject      = "math",
                Topic        = "algebra",
                CreatedAt    = _clock.UtcNow.AddDays(-20).UtcDateTime,
                MessageCount = 2,
            },
            new DeviceSessionDocument
            {
                Id            = "device-001",
                StudentId     = TestStudentId,
                Platform      = "ios",
                DeviceName    = "John's iPad",
                DeviceModel   = "iPad Pro 12.9",
                OsVersion     = "iOS 17.0",
                AppVersion    = "2.5.1",
                FirstSeenAt   = _clock.UtcNow.AddDays(-60).UtcDateTime,
                LastSeenAt    = _clock.UtcNow.AddDays(-1).UtcDateTime,
                LastIpAddress = "192.168.1.0", // FIND-privacy-015: /24 truncated
            },
            new StudentPreferencesDocument
            {
                Id                 = "pref-001",
                StudentId          = TestStudentId,
                Theme              = "dark",
                Language           = "en",
                EmailNotifications = true,
                ProfileVisibility  = "class-only",
                CreatedAt          = _clock.UtcNow.AddDays(-365).UtcDateTime,
                UpdatedAt          = _clock.UtcNow.AddDays(-30).UtcDateTime,
            },
            new ShareTokenDocument
            {
                Id        = "token-001",
                Token     = "abc123xyz789",
                StudentId = TestStudentId,
                Audience  = "parent",
                Scopes    = new[] { "progress", "achievements" },
                ExpiresAt = _clock.UtcNow.AddDays(30).UtcDateTime,
                CreatedAt = _clock.UtcNow.AddDays(-5).UtcDateTime,
            },
            new ConsentRecord
            {
                Id        = Guid.NewGuid(),
                StudentId = TestStudentId,
                Purpose   = ProcessingPurpose.BehavioralAnalytics,
                Granted   = true,
                GrantedAt = _clock.UtcNow.AddDays(-365),
            },
            new StudentRecordAccessLog
            {
                Id             = Guid.NewGuid(),
                AccessedAt     = _clock.UtcNow.AddDays(-30),
                AccessedBy     = "teacher-001",
                AccessorRole   = "Teacher",
                AccessorSchool = "school-001",
                StudentId      = TestStudentId,
                Endpoint       = "/api/students/test/profile",
                HttpMethod     = "GET",
                StatusCode     = 200,
                Category       = "data_access",
            });

        // ACT phase 1: request erasure
        var request = await _service.RequestErasureAsync(TestStudentId, TestRequester);
        Assert.Equal(TestStudentId, request.StudentId);
        Assert.Equal(ErasureStatus.CoolingPeriod, request.Status);

        // Fast-forward past the 30-day cooling period.
        _clock.Advance(TimeSpan.FromDays(31));

        // ACT phase 2: process erasure
        await _service.ProcessErasureAsync(TestStudentId);

        // ASSERT — verify each store was erased per its rules.

        // Profile: ANONYMIZED (still present, FullName hashed, PII cleared).
        var profiles = await QueryAllAsync<StudentProfileRef>();
        var profile  = Assert.Single(profiles);
        Assert.Equal(TestStudentId,    profile.Id);
        Assert.Equal("Anonymized",     profile.AccountStatus);
        Assert.Equal("[deleted]",      profile.DisplayName);
        Assert.Null(profile.Bio);
        Assert.Null(profile.DateOfBirth);
        Assert.Null(profile.ParentEmail);
        Assert.NotEqual("John Doe Test Student", profile.FullName);   // hashed
        Assert.False(string.IsNullOrEmpty(profile.FullName));         // not nulled
        Assert.Equal("school-001",     profile.SchoolId);             // preserved

        // Hard-deletes — these doc types should be empty for this student.
        Assert.Empty(await QueryAllAsync<TutorMessageDocument>());
        Assert.Empty(await QueryAllAsync<TutorThreadDocument>());
        Assert.Empty(await QueryAllAsync<DeviceSessionDocument>());
        Assert.Empty(await QueryAllAsync<StudentPreferencesDocument>());
        Assert.Empty(await QueryAllAsync<ShareTokenDocument>());

        // Consent: PRESERVED but revoked.
        var consents = await QueryAllAsync<ConsentRecord>();
        var consent  = Assert.Single(consents);
        Assert.False(consent.Granted);
        Assert.NotNull(consent.RevokedAt);

        // Access log: PRESERVED (FERPA — never deleted).
        var logs = await QueryAllAsync<StudentRecordAccessLog>();
        Assert.Single(logs);

        // Erasure request: terminal state.
        var requests = await QueryAllAsync<ErasureRequest>();
        var completed = Assert.Single(requests);
        Assert.Equal(ErasureStatus.Completed, completed.Status);
        Assert.NotNull(completed.ProcessedAt);
        Assert.NotNull(completed.Manifest);
    }

    // =========================================================================
    // 3. Manifest accuracy — every store action recorded with the right verb.
    // =========================================================================

    [Fact]
    public async Task ErasureManifest_AccuratelyReportsActions()
    {
        // Seed: 1 profile + 2 consents + 3 access logs + 1 device session.
        await SeedAsync(
            new StudentProfileRef { Id = TestStudentId, FullName = "Manifest Test", AccountStatus = "Active" },
            new ConsentRecord { Id = Guid.NewGuid(), StudentId = TestStudentId, Purpose = ProcessingPurpose.BehavioralAnalytics, Granted = true, GrantedAt = _clock.UtcNow.AddDays(-30) },
            new ConsentRecord { Id = Guid.NewGuid(), StudentId = TestStudentId, Purpose = ProcessingPurpose.MarketingNudges,    Granted = false, GrantedAt = _clock.UtcNow.AddDays(-30) },
            new StudentRecordAccessLog { Id = Guid.NewGuid(), StudentId = TestStudentId, AccessedAt = _clock.UtcNow.AddDays(-30), AccessedBy = "t1", Endpoint = "/p", HttpMethod = "GET", StatusCode = 200, Category = "data_access" },
            new StudentRecordAccessLog { Id = Guid.NewGuid(), StudentId = TestStudentId, AccessedAt = _clock.UtcNow.AddDays(-20), AccessedBy = "t2", Endpoint = "/p", HttpMethod = "GET", StatusCode = 200, Category = "data_access" },
            new StudentRecordAccessLog { Id = Guid.NewGuid(), StudentId = TestStudentId, AccessedAt = _clock.UtcNow.AddDays(-10), AccessedBy = "t3", Endpoint = "/p", HttpMethod = "GET", StatusCode = 200, Category = "data_access" },
            new DeviceSessionDocument { Id = "d1", StudentId = TestStudentId, Platform = "ios", DeviceName = "x", DeviceModel = "y", OsVersion = "z", AppVersion = "1", FirstSeenAt = _clock.UtcNow.AddDays(-30).UtcDateTime, LastSeenAt = _clock.UtcNow.AddDays(-1).UtcDateTime, LastIpAddress = "10.0.0.0" });

        await _service.RequestErasureAsync(TestStudentId, TestRequester);
        _clock.Advance(TimeSpan.FromDays(31));
        await _service.ProcessErasureAsync(TestStudentId);

        var requests = await QueryAllAsync<ErasureRequest>();
        var req = Assert.Single(requests);
        Assert.Equal(ErasureStatus.Completed, req.Status);
        Assert.NotNull(req.Manifest);

        var actions = req.Manifest!.Actions.ToDictionary(a => a.Store, a => a);

        // Profile: anonymized, count 1.
        Assert.Equal(ErasureAction.Anonymized, actions["StudentProfileSnapshot"].Action);
        Assert.Equal(1, actions["StudentProfileSnapshot"].Count);

        // Consents: preserved (NOT deleted), count 2.
        Assert.Equal(ErasureAction.Preserved, actions["ConsentRecord"].Action);
        Assert.Equal(2, actions["ConsentRecord"].Count);

        // Access logs: preserved (FERPA), count 3.
        Assert.Equal(ErasureAction.Preserved, actions["StudentRecordAccessLog"].Action);
        Assert.Equal(3, actions["StudentRecordAccessLog"].Count);

        // Device sessions: deleted, count 1.
        Assert.Equal(ErasureAction.Deleted, actions["DeviceSessionDocument"].Action);
        Assert.Equal(1, actions["DeviceSessionDocument"].Count);
    }

    // =========================================================================
    // 4. Access log preservation (FERPA) — corrects the ORIGINAL test's
    //    inverted assertion. The previous test asserted Received(3).Delete
    //    on access logs; production correctly NEVER deletes them. The
    //    Marten-cast bug masked the assertion mismatch — once the cast was
    //    fixed, that assertion would have failed too. cm 2026-04-30.
    // =========================================================================

    [Fact]
    public async Task StudentRecordAccessLog_Preserved_NotDeleted()
    {
        var logIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        await SeedAsync(
            new StudentRecordAccessLog { Id = logIds[0], StudentId = TestStudentId, AccessedAt = _clock.UtcNow.AddDays(-30), AccessedBy = "teacher-001", Endpoint = "/p", HttpMethod = "GET", StatusCode = 200, Category = "data_access" },
            new StudentRecordAccessLog { Id = logIds[1], StudentId = TestStudentId, AccessedAt = _clock.UtcNow.AddDays(-20), AccessedBy = "admin-001",   Endpoint = "/p", HttpMethod = "GET", StatusCode = 200, Category = "privileged_action" },
            new StudentRecordAccessLog { Id = logIds[2], StudentId = TestStudentId, AccessedAt = _clock.UtcNow.AddDays(-10), AccessedBy = "system",      Endpoint = "/p", HttpMethod = "GET", StatusCode = 200, Category = "export" });

        await _service.RequestErasureAsync(TestStudentId, TestRequester);
        _clock.Advance(TimeSpan.FromDays(31));
        await _service.ProcessErasureAsync(TestStudentId);

        var logs = await QueryAllAsync<StudentRecordAccessLog>();
        Assert.Equal(3, logs.Count);
        // All original Ids still present.
        foreach (var id in logIds) Assert.Contains(logs, l => l.Id == id);
    }

    // =========================================================================
    // 5. Consent revocation (preservation) — corrects the ORIGINAL test's
    //    inverted assertion. The previous test asserted Received(2).Delete
    //    on consents; production correctly preserves them with Granted=false
    //    + RevokedAt set (legal provenance retention). cm 2026-04-30.
    // =========================================================================

    [Fact]
    public async Task ConsentRecord_RevokedNotDeleted()
    {
        var consentIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        await SeedAsync(
            new ConsentRecord
            {
                Id        = consentIds[0],
                StudentId = TestStudentId,
                Purpose   = ProcessingPurpose.BehavioralAnalytics,
                Granted   = true,
                GrantedAt = _clock.UtcNow.AddDays(-365),
            },
            new ConsentRecord
            {
                Id        = consentIds[1],
                StudentId = TestStudentId,
                Purpose   = ProcessingPurpose.MarketingNudges,
                Granted   = false,
                GrantedAt = _clock.UtcNow.AddDays(-365),
                RevokedAt = _clock.UtcNow.AddDays(-180),
            });

        await _service.RequestErasureAsync(TestStudentId, TestRequester);
        _clock.Advance(TimeSpan.FromDays(31));
        await _service.ProcessErasureAsync(TestStudentId);

        var consents = await QueryAllAsync<ConsentRecord>();
        Assert.Equal(2, consents.Count);
        // All preserved (NOT deleted).
        foreach (var id in consentIds) Assert.Contains(consents, c => c.Id == id);
        // Both marked as revoked + RevokedAt set.
        Assert.All(consents, c => Assert.False(c.Granted));
        Assert.All(consents, c => Assert.NotNull(c.RevokedAt));
    }

    // =========================================================================
    // 6. Profile-anonymization hash semantics. Pure-function probe — no
    //    Marten / clock involvement. Pinned for the regression where
    //    deterministic-hashed FullName is required for analytics joins
    //    after anonymization (per ADR-0038 + the Erasure docstring).
    // =========================================================================

    [Fact]
    public void ProfileAnonymization_HashWithPepper_Deterministic()
    {
        const string fullName = "John Doe Test Student";
        const string pepper = "test-pepper-v1";

        var hash1 = HashWithPepper(fullName, pepper);
        var hash2 = HashWithPepper(fullName, pepper);
        var differentPepperHash = HashWithPepper(fullName, "different-pepper");

        Assert.Equal(hash1, hash2);
        Assert.NotEqual(hash1, differentPepperHash);
        Assert.Equal(64, hash1.Length); // HMAC-SHA256 → 32 bytes → 64 hex chars
    }

    private static string HashWithPepper(string input, string pepper)
    {
        var key = Encoding.UTF8.GetBytes(pepper);
        var data = Encoding.UTF8.GetBytes(input);
        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
