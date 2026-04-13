// =============================================================================
// Cena Platform -- GDPR Right to Erasure (SEC-005)
// Implements GDPR Article 17 — request, cooling period, and REAL erasure.
//
// Erasure strategy per data store:
//   - Student Profile Snapshot: ANONYMIZE (retain for referential integrity)
//   - Tutor Messages/Threads: HARD DELETE (free-text PII)
//   - Device Sessions: HARD DELETE
//   - Student Preferences: HARD DELETE
//   - Share Tokens: HARD DELETE
//   - Consent Records: PRESERVE (mark revoked - legal provenance)
//   - Access Logs: PRESERVE (FERPA requirement - disclosure records outlive subject)
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Cena.Infrastructure.Documents;
using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Status of a GDPR erasure request through its lifecycle.
/// </summary>
public enum ErasureStatus
{
    /// <summary>Initial request received, cooling period not yet started.</summary>
    Requested,

    /// <summary>30-day cooling period in effect.</summary>
    CoolingPeriod,

    /// <summary>Erasure actively being processed.</summary>
    Processing,

    /// <summary>Erasure completed successfully across all stores.</summary>
    Completed,

    /// <summary>Request cancelled by data subject before processing.</summary>
    Cancelled
}

/// <summary>
/// Types of erasure actions that can be taken against a data store.
/// </summary>
public enum ErasureAction
{
    /// <summary>Data anonymized (PII removed/irreversibly hashed, record retained for integrity).</summary>
    Anonymized,

    /// <summary>Data permanently deleted.</summary>
    Deleted,

    /// <summary>Data preserved (legal hold, audit requirement, or FERPA).</summary>
    Preserved
}

/// <summary>
/// Represents a single erasure action taken against a specific data store.
/// Part of the ErasureManifest for audit trail purposes.
/// </summary>
public sealed class ErasureManifestItem
{
    /// <summary>Name of the data store (e.g., "StudentProfile", "TutorMessages").</summary>
    public string Store { get; set; } = "";

    /// <summary>The type of action taken.</summary>
    public ErasureAction Action { get; set; }

    /// <summary>Number of records/rows affected in this store.</summary>
    public int Count { get; set; }

    /// <summary>Additional context for audit (e.g., "HMAC-SHA256 hashed").</summary>
    public string? Details { get; set; }

    public ErasureManifestItem(string store, ErasureAction action, int count, string? details = null)
    {
        Store = store;
        Action = action;
        Count = count;
        Details = details;
    }
}

/// <summary>
/// Complete manifest of all erasure actions taken across all data stores.
/// Provides full audit trail for compliance verification.
/// </summary>
public sealed class ErasureManifest
{
    /// <summary>The student ID that was erased.</summary>
    public string StudentId { get; set; } = "";

    /// <summary>Timestamp when erasure was completed.</summary>
    public DateTimeOffset CompletedAt { get; set; }

    /// <summary>All actions taken across data stores.</summary>
    public List<ErasureManifestItem> Actions { get; set; } = new();

    /// <summary>Total records affected across all stores.</summary>
    public int RowsAffected => Actions.Sum(a => a.Count);

    public ErasureManifest(string studentId, DateTimeOffset completedAt)
    {
        StudentId = studentId;
        CompletedAt = completedAt;
    }

    /// <summary>Adds an action to the manifest.</summary>
    public void AddAction(string store, ErasureAction action, int count, string? details = null)
    {
        Actions.Add(new ErasureManifestItem(store, action, count, details));
    }
}

/// <summary>
/// Interface for building an erasure manifest during processing.
/// Abstracts manifest construction for testability.
/// </summary>
public interface IErasureManifestBuilder
{
    /// <summary>Creates a new manifest for the specified student.</summary>
    ErasureManifest CreateManifest(string studentId, DateTimeOffset completedAt);
}

/// <summary>
/// Default implementation of the erasure manifest builder.
/// </summary>
public sealed class ErasureManifestBuilder : IErasureManifestBuilder
{
    /// <inheritdoc />
    public ErasureManifest CreateManifest(string studentId, DateTimeOffset completedAt)
    {
        return new ErasureManifest(studentId, completedAt);
    }
}

/// <summary>
/// Provides configuration for cryptographic operations during erasure.
/// </summary>
public interface IErasureCryptoConfig
{
    /// <summary>The pepper value used for HMAC hashing (should be 32+ bytes).</summary>
    string Pepper { get; }
}

/// <summary>
/// Default implementation reading pepper from configuration.
/// </summary>
public sealed class ErasureCryptoConfig : IErasureCryptoConfig
{
    /// <inheritdoc />
    public string Pepper { get; }

    public ErasureCryptoConfig(string pepper)
    {
        if (string.IsNullOrEmpty(pepper))
            throw new ArgumentException("Erasure pepper must be configured", nameof(pepper));
        Pepper = pepper;
    }
}

/// <summary>
/// A GDPR erasure request record stored in Marten.
/// </summary>
public sealed class ErasureRequest
{
    /// <summary>Unique identifier for this request.</summary>
    public Guid Id { get; set; }

    /// <summary>The student ID to erase.</summary>
    public string StudentId { get; set; } = "";

    /// <summary>Current status of the erasure request.</summary>
    public ErasureStatus Status { get; set; }

    /// <summary>When the request was initially received.</summary>
    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>When the erasure was actually processed (null if not yet).</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Who requested the erasure (e.g., "student:self", "parent:email@example.com").</summary>
    public string? RequestedBy { get; set; }

    /// <summary>The completed erasure manifest (null until completion).</summary>
    public ErasureManifest? Manifest { get; set; }
}

/// <summary>
/// Lightweight POCO for loading StudentProfileSnapshot from Marten without
/// pulling in the Cena.Actors assembly. Marten resolves by document type name
/// at runtime, so we configure the table mapping via StoreOptions in the host.
/// Contains only the fields needed for GDPR erasure anonymization.
/// </summary>
internal class StudentProfileRef
{
    public string Id { get; set; } = "";
    public string? FullName { get; set; }
    public string? DisplayName { get; set; }
    public string? Bio { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public string? ParentEmail { get; set; }
    public string? AccountStatus { get; set; }
    public string? SchoolId { get; set; }
}

/// <summary>
/// Service interface for GDPR Right to Erasure operations.
/// </summary>
public interface IRightToErasureService
{
    /// <summary>Submit a new erasure request (enters cooling period).</summary>
    Task<ErasureRequest> RequestErasureAsync(string studentId, string requestedBy, CancellationToken ct = default);

    /// <summary>Process the erasure for a student (after cooling period).</summary>
    Task ProcessErasureAsync(string studentId, CancellationToken ct = default);

    /// <summary>Get the current status of an erasure request.</summary>
    Task<ErasureRequest?> GetErasureStatusAsync(string studentId, CancellationToken ct = default);
}

/// <summary>
/// Implements GDPR Article 17 Right to Erasure with proper store-specific handling.
/// </summary>
public sealed class RightToErasureService : IRightToErasureService
{
    private static readonly TimeSpan CoolingPeriod = TimeSpan.FromDays(30);

    private readonly IDocumentStore _store;
    private readonly ILogger<RightToErasureService> _logger;
    private readonly IClock _clock;
    private readonly IErasureManifestBuilder _manifestBuilder;
    private readonly IErasureCryptoConfig _cryptoConfig;

    /// <summary>
    /// Creates a new RightToErasureService.
    /// </summary>
    public RightToErasureService(
        IDocumentStore store,
        ILogger<RightToErasureService> logger,
        IClock clock,
        IErasureManifestBuilder manifestBuilder,
        IErasureCryptoConfig cryptoConfig)
    {
        _store = store;
        _logger = logger;
        _clock = clock;
        _manifestBuilder = manifestBuilder;
        _cryptoConfig = cryptoConfig;
    }

    /// <inheritdoc />
    public async Task<ErasureRequest> RequestErasureAsync(string studentId, string requestedBy, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();

        // Check for existing pending request
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

        _logger.LogInformation("[SIEM] ErasureRequested: {RequestId} for {StudentId} by {RequestedBy}. 30-day cooling period starts.",
            request.Id, studentId, requestedBy);

        return request;
    }

    /// <inheritdoc />
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

        // Enforce cooling period
        if (_clock.UtcNow - request.RequestedAt < CoolingPeriod)
        {
            _logger.LogInformation("Erasure for {StudentId} still in cooling period (requested {RequestedAt})",
                studentId, request.RequestedAt);
            return;
        }

        request.Status = ErasureStatus.Processing;
        session.Store(request);
        await session.SaveChangesAsync(ct);

        // Create manifest builder for recording all actions
        var completedAt = _clock.UtcNow;
        var manifest = _manifestBuilder.CreateManifest(studentId, completedAt);

        try
        {
            // =============================================================================
            // 1. STUDENT PROFILE SNAPSHOT - ANONYMIZE (don't delete)
            // =============================================================================
            // Hash FullName with HMAC-SHA256, null out PII fields, keep StudentId for referential integrity
            var profile = await session.LoadAsync<StudentProfileRef>(studentId, ct);
            if (profile is not null)
            {
                var originalName = profile.FullName;

                // Hash the full name with HMAC-SHA256 using configured pepper
                if (!string.IsNullOrEmpty(profile.FullName))
                {
                    profile.FullName = HashWithHmacSha256(profile.FullName, _cryptoConfig.Pepper);
                }

                // Anonymize PII fields
                profile.DisplayName = "[deleted]";
                profile.Bio = null;
                profile.DateOfBirth = null;
                profile.ParentEmail = null;
                profile.AccountStatus = "Anonymized";

                // SchoolId and StudentId are preserved for audit and referential integrity
                session.Store(profile);

                manifest.AddAction("StudentProfileSnapshot", ErasureAction.Anonymized, 1,
                    $"FullName hashed with HMAC-SHA256, DisplayName set to [deleted], PII cleared");

                _logger.LogDebug("Anonymized StudentProfileSnapshot for {StudentId}", studentId);
            }
            else
            {
                manifest.AddAction("StudentProfileSnapshot", ErasureAction.Anonymized, 0, "No profile found");
            }

            // =============================================================================
            // 2. TUTOR MESSAGES + THREADS - HARD DELETE (free-text PII)
            // =============================================================================
            var tutorMessages = await session.Query<TutorMessageDocument>()
                .Where(m => m.StudentId == studentId)
                .ToListAsync(ct);
            int msgCount = tutorMessages.Count;
            foreach (var msg in tutorMessages)
            {
                session.Delete(msg);
            }
            manifest.AddAction("TutorMessageDocument", ErasureAction.Deleted, msgCount);
            _logger.LogDebug("Deleted {Count} TutorMessageDocument for {StudentId}", msgCount, studentId);

            var tutorThreads = await session.Query<TutorThreadDocument>()
                .Where(t => t.StudentId == studentId)
                .ToListAsync(ct);
            int threadCount = tutorThreads.Count;
            foreach (var thread in tutorThreads)
            {
                session.Delete(thread);
            }
            manifest.AddAction("TutorThreadDocument", ErasureAction.Deleted, threadCount);
            _logger.LogDebug("Deleted {Count} TutorThreadDocument for {StudentId}", threadCount, studentId);

            // =============================================================================
            // 3. DEVICE SESSIONS - HARD DELETE
            // =============================================================================
            var deviceSessions = await session.Query<DeviceSessionDocument>()
                .Where(d => d.StudentId == studentId)
                .ToListAsync(ct);
            int deviceCount = deviceSessions.Count;
            foreach (var device in deviceSessions)
            {
                session.Delete(device);
            }
            manifest.AddAction("DeviceSessionDocument", ErasureAction.Deleted, deviceCount);
            _logger.LogDebug("Deleted {Count} DeviceSessionDocument for {StudentId}", deviceCount, studentId);

            // =============================================================================
            // 4. STUDENT PREFERENCES - HARD DELETE
            // =============================================================================
            var preferences = await session.Query<StudentPreferencesDocument>()
                .Where(p => p.StudentId == studentId)
                .ToListAsync(ct);
            int prefCount = preferences.Count;
            foreach (var pref in preferences)
            {
                session.Delete(pref);
            }
            manifest.AddAction("StudentPreferencesDocument", ErasureAction.Deleted, prefCount);
            _logger.LogDebug("Deleted {Count} StudentPreferencesDocument for {StudentId}", prefCount, studentId);

            // =============================================================================
            // 5. SHARE TOKENS - HARD DELETE
            // =============================================================================
            var shareTokens = await session.Query<ShareTokenDocument>()
                .Where(s => s.StudentId == studentId)
                .ToListAsync(ct);
            int tokenCount = shareTokens.Count;
            foreach (var token in shareTokens)
            {
                session.Delete(token);
            }
            manifest.AddAction("ShareTokenDocument", ErasureAction.Deleted, tokenCount);
            _logger.LogDebug("Deleted {Count} ShareTokenDocument for {StudentId}", tokenCount, studentId);

            // =============================================================================
            // 6. CONSENT RECORDS - PRESERVE (mark revoked, don't delete)
            // =============================================================================
            // Consent provenance must be retained for legal/audit purposes.
            // The retention worker will handle final purging per policy.
            var consents = await session.Query<ConsentRecord>()
                .Where(c => c.StudentId == studentId)
                .ToListAsync(ct);
            int consentCount = consents.Count;
            foreach (var consent in consents)
            {
                consent.Granted = false;
                consent.RevokedAt = _clock.UtcNow;
                session.Store(consent);
            }
            manifest.AddAction("ConsentRecord", ErasureAction.Preserved, consentCount,
                "Marked as revoked - retention worker handles per policy");
            _logger.LogDebug("Revoked {Count} ConsentRecord for {StudentId}", consentCount, studentId);

            // =============================================================================
            // 7. STUDENT RECORD ACCESS LOG - PRESERVE (FERPA requirement)
            // =============================================================================
            // FERPA requires disclosure records to outlive the record itself.
            // NEVER delete access logs - they are the audit trail.
            var accessLogCount = await session.Query<StudentRecordAccessLog>()
                .CountAsync(l => l.StudentId == studentId, ct);
            manifest.AddAction("StudentRecordAccessLog", ErasureAction.Preserved, accessLogCount,
                "FERPA requirement - disclosure records preserved");
            _logger.LogDebug("Preserved {Count} StudentRecordAccessLog for {StudentId} (FERPA)", accessLogCount, studentId);

            // =============================================================================
            // 8. Structured SIEM event for erasure completion
            // =============================================================================
            // Domain event (StudentErasureCompleted_V1) is emitted by the host layer
            // which can reference Cena.Actors.Events. Infrastructure only logs + persists.
            _logger.LogInformation(
                "[SIEM] ErasureManifestReady: {RequestId} for {StudentId}, Stores={StoreCount}, Rows={TotalRows}",
                request.Id, studentId, manifest.Actions.Count, manifest.RowsAffected);

            // =============================================================================
            // 9. Update request status and save
            // =============================================================================
            request.Status = ErasureStatus.Completed;
            request.ProcessedAt = completedAt;
            request.Manifest = manifest;
            session.Store(request);

            await session.SaveChangesAsync(ct);

            // =============================================================================
            // 10. Structured SIEM logging
            // =============================================================================
            _logger.LogInformation(
                "[SIEM] ErasureCompleted: {RequestId} for {StudentId} at {CompletedAt}. " +
                "TotalRows={TotalRows}, Actions=[{Actions}]",
                request.Id,
                studentId,
                completedAt,
                manifest.RowsAffected,
                string.Join("; ", manifest.Actions.Select(a => $"{a.Store}:{a.Action}={a.Count}")));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SIEM] ErasureFailed: {RequestId} for {StudentId} - {Error}",
                request.Id, studentId, ex.Message);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ErasureRequest?> GetErasureStatusAsync(string studentId, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        return await session.Query<ErasureRequest>()
            .OrderByDescending(e => e.RequestedAt)
            .FirstOrDefaultAsync(e => e.StudentId == studentId, ct);
    }

    /// <summary>
    /// Hashes the input with HMAC-SHA256 using the configured pepper.
    /// </summary>
    private static string HashWithHmacSha256(string input, string pepper)
    {
        var key = Encoding.UTF8.GetBytes(pepper);
        var data = Encoding.UTF8.GetBytes(input);
        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// Extension methods for registering Right to Erasure services.
/// </summary>
public static class RightToErasureServiceExtensions
{
    /// <summary>
    /// Adds GDPR Right to Erasure services to the DI container.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="pepper">The cryptographic pepper for HMAC hashing (must be 32+ bytes).</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRightToErasureService(
        this IServiceCollection services,
        string pepper)
    {
        services.AddSingleton<IErasureCryptoConfig>(new ErasureCryptoConfig(pepper));
        services.AddSingleton<IErasureManifestBuilder, ErasureManifestBuilder>();
        services.AddScoped<IRightToErasureService, RightToErasureService>();
        return services;
    }
}
