// =============================================================================
// Cena Platform -- GDPR Consent Manager (SEC-005)
// Tracks per-student consent for analytics, marketing, and third-party sharing.
// Backed by Marten document store (PostgreSQL).
// =============================================================================

using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Audit log entry for consent changes. Immutable record for compliance trails.
/// </summary>
public sealed class ConsentChangeLog
{
    public Guid Id { get; set; }
    public string StudentId { get; set; } = "";
    public ProcessingPurpose Purpose { get; set; }
    public bool? PreviousValue { get; set; }
    public bool NewValue { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public string ChangedBy { get; set; } = "";
    public string Source { get; set; } = ""; // ui, api, system
}

/// <summary>
/// Consent record document stored in Marten.
/// </summary>
public sealed class ConsentRecord
{
    public Guid Id { get; set; }
    public string StudentId { get; set; } = "";
    public ProcessingPurpose Purpose { get; set; }
    public bool Granted { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>
/// Interface for GDPR consent management operations.
/// </summary>
public interface IGdprConsentManager
{
    Task RecordConsentAsync(string studentId, ProcessingPurpose purpose, CancellationToken ct = default);
    Task RevokeConsentAsync(string studentId, ProcessingPurpose purpose, CancellationToken ct = default);
    Task<IReadOnlyList<ConsentRecord>> GetConsentsAsync(string studentId, CancellationToken ct = default);
    Task<bool> HasConsentAsync(string studentId, ProcessingPurpose purpose, bool isMinor, CancellationToken ct = default);
    Task RecordConsentChangeAsync(string studentId, ProcessingPurpose purpose, bool granted, string recordedBy, string source = "system", CancellationToken ct = default);
    Task<IReadOnlyDictionary<ProcessingPurpose, bool>> GetDefaultConsentsAsync(bool isMinor, CancellationToken ct = default);
    Task<IReadOnlyDictionary<ProcessingPurpose, bool>> BatchCheckConsentAsync(string studentId, IReadOnlyList<ProcessingPurpose> purposes, bool isMinor, CancellationToken ct = default);
}

/// <summary>
/// GDPR consent manager implementation using Marten document store.
/// </summary>
public sealed class GdprConsentManager : IGdprConsentManager
{
    private readonly IDocumentStore _store;
    private readonly ILogger<GdprConsentManager> _logger;

    public GdprConsentManager(IDocumentStore store, ILogger<GdprConsentManager> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task RecordConsentAsync(string studentId, ProcessingPurpose purpose, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();

        var existing = await session.Query<ConsentRecord>()
            .FirstOrDefaultAsync(c => c.StudentId == studentId && c.Purpose == purpose, ct);

        if (existing is not null)
        {
            existing.Granted = true;
            existing.GrantedAt = DateTimeOffset.UtcNow;
            existing.RevokedAt = null;
            session.Store(existing);
        }
        else
        {
            session.Store(new ConsentRecord
            {
                Id = Guid.NewGuid(),
                StudentId = studentId,
                Purpose = purpose,
                Granted = true,
                GrantedAt = DateTimeOffset.UtcNow
            });
        }

        await session.SaveChangesAsync(ct);
        _logger.LogInformation("GDPR consent recorded: {StudentId} granted {Purpose}", studentId, purpose);
    }

    public async Task RevokeConsentAsync(string studentId, ProcessingPurpose purpose, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();

        var existing = await session.Query<ConsentRecord>()
            .FirstOrDefaultAsync(c => c.StudentId == studentId && c.Purpose == purpose, ct);

        if (existing is null) return;

        existing.Granted = false;
        existing.RevokedAt = DateTimeOffset.UtcNow;
        session.Store(existing);
        await session.SaveChangesAsync(ct);

        _logger.LogInformation("GDPR consent revoked: {StudentId} revoked {Purpose}", studentId, purpose);
    }

    public async Task<IReadOnlyList<ConsentRecord>> GetConsentsAsync(string studentId, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        return await session.Query<ConsentRecord>()
            .Where(c => c.StudentId == studentId)
            .ToListAsync(ct);
    }

    public async Task<bool> HasConsentAsync(string studentId, ProcessingPurpose purpose, bool isMinor, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        
        var consent = await session.Query<ConsentRecord>()
            .FirstOrDefaultAsync(c => c.StudentId == studentId && c.Purpose == purpose, ct);

        bool result;
        if (consent is not null)
        {
            result = consent.Granted;
        }
        else
        {
            // No record found - return default based on student age
            result = purpose.GetDefaultConsent(isMinor);
        }

        _logger.LogInformation("[SIEM] ConsentChecked: {StudentId}, Purpose={Purpose}, Result={Result}, IsMinor={IsMinor}",
            studentId, purpose, result, isMinor);

        return result;
    }

    public async Task RecordConsentChangeAsync(string studentId, ProcessingPurpose purpose, bool granted, string recordedBy, string source = "system", CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();

        // Get previous value if exists
        var existing = await session.Query<ConsentRecord>()
            .FirstOrDefaultAsync(c => c.StudentId == studentId && c.Purpose == purpose, ct);

        bool? previousValue = existing?.Granted;

        // Create audit log entry
        var changeLog = new ConsentChangeLog
        {
            Id = Guid.NewGuid(),
            StudentId = studentId,
            Purpose = purpose,
            PreviousValue = previousValue,
            NewValue = granted,
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = recordedBy,
            Source = source
        };
        session.Store(changeLog);

        // Apply the consent change
        if (existing is not null)
        {
            existing.Granted = granted;
            if (granted)
            {
                existing.GrantedAt = DateTimeOffset.UtcNow;
                existing.RevokedAt = null;
            }
            else
            {
                existing.RevokedAt = DateTimeOffset.UtcNow;
            }
            session.Store(existing);
        }
        else
        {
            session.Store(new ConsentRecord
            {
                Id = Guid.NewGuid(),
                StudentId = studentId,
                Purpose = purpose,
                Granted = granted,
                GrantedAt = DateTimeOffset.UtcNow,
                RevokedAt = granted ? null : DateTimeOffset.UtcNow
            });
        }

        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "[SIEM] ConsentChangeRecorded: {StudentId}, Purpose={Purpose}, Previous={Previous}, New={New}, By={RecordedBy}, Source={Source}",
            studentId, purpose, previousValue, granted, recordedBy, source);
    }

    public Task<IReadOnlyDictionary<ProcessingPurpose, bool>> GetDefaultConsentsAsync(bool isMinor, CancellationToken ct = default)
    {
        var defaults = new Dictionary<ProcessingPurpose, bool>();
        
        foreach (ProcessingPurpose purpose in Enum.GetValues<ProcessingPurpose>())
        {
            // High-privacy default: everything false except for always-required purposes
            // MarketingNudges always false for minors
            if (purpose == ProcessingPurpose.MarketingNudges && isMinor)
            {
                defaults[purpose] = false;
            }
            else
            {
                defaults[purpose] = purpose.GetDefaultConsent(isMinor);
            }
        }

        return Task.FromResult<IReadOnlyDictionary<ProcessingPurpose, bool>>(defaults);
    }

    public async Task<IReadOnlyDictionary<ProcessingPurpose, bool>> BatchCheckConsentAsync(string studentId, IReadOnlyList<ProcessingPurpose> purposes, bool isMinor, CancellationToken ct = default)
    {
        var results = new Dictionary<ProcessingPurpose, bool>();

        foreach (var purpose in purposes)
        {
            results[purpose] = await HasConsentAsync(studentId, purpose, isMinor, ct);
        }

        return results;
    }
}
