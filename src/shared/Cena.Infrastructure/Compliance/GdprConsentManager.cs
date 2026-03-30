// =============================================================================
// Cena Platform -- GDPR Consent Manager (SEC-005)
// Tracks per-student consent for analytics, marketing, and third-party sharing.
// Backed by Marten document store (PostgreSQL).
// =============================================================================

using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Compliance;

public enum ConsentType { Analytics, Marketing, ThirdParty }

public sealed class ConsentRecord
{
    public Guid Id { get; set; }
    public string StudentId { get; set; } = "";
    public ConsentType ConsentType { get; set; }
    public bool Granted { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public interface IGdprConsentManager
{
    Task RecordConsentAsync(string studentId, ConsentType type, CancellationToken ct = default);
    Task RevokeConsentAsync(string studentId, ConsentType type, CancellationToken ct = default);
    Task<IReadOnlyList<ConsentRecord>> GetConsentsAsync(string studentId, CancellationToken ct = default);
    Task<bool> HasConsentAsync(string studentId, ConsentType type, CancellationToken ct = default);
}

public sealed class GdprConsentManager : IGdprConsentManager
{
    private readonly IDocumentStore _store;
    private readonly ILogger<GdprConsentManager> _logger;

    public GdprConsentManager(IDocumentStore store, ILogger<GdprConsentManager> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task RecordConsentAsync(string studentId, ConsentType type, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();

        var existing = await session.Query<ConsentRecord>()
            .FirstOrDefaultAsync(c => c.StudentId == studentId && c.ConsentType == type, ct);

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
                ConsentType = type,
                Granted = true,
                GrantedAt = DateTimeOffset.UtcNow
            });
        }

        await session.SaveChangesAsync(ct);
        _logger.LogInformation("GDPR consent recorded: {StudentId} granted {Type}", studentId, type);
    }

    public async Task RevokeConsentAsync(string studentId, ConsentType type, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();

        var existing = await session.Query<ConsentRecord>()
            .FirstOrDefaultAsync(c => c.StudentId == studentId && c.ConsentType == type, ct);

        if (existing is null) return;

        existing.Granted = false;
        existing.RevokedAt = DateTimeOffset.UtcNow;
        session.Store(existing);
        await session.SaveChangesAsync(ct);

        _logger.LogInformation("GDPR consent revoked: {StudentId} revoked {Type}", studentId, type);
    }

    public async Task<IReadOnlyList<ConsentRecord>> GetConsentsAsync(string studentId, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        return await session.Query<ConsentRecord>()
            .Where(c => c.StudentId == studentId)
            .ToListAsync(ct);
    }

    public async Task<bool> HasConsentAsync(string studentId, ConsentType type, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        return await session.Query<ConsentRecord>()
            .AnyAsync(c => c.StudentId == studentId && c.ConsentType == type && c.Granted, ct);
    }
}
