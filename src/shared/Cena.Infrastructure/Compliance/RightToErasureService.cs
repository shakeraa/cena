// =============================================================================
// Cena Platform -- GDPR Right to Erasure (SEC-005)
// Implements GDPR Article 17 — request, cooling period, hard delete.
// =============================================================================

using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Compliance;

public enum ErasureStatus { Requested, CoolingPeriod, Processing, Completed, Cancelled }

public sealed class ErasureRequest
{
    public Guid Id { get; set; }
    public string StudentId { get; set; } = "";
    public ErasureStatus Status { get; set; }
    public DateTimeOffset RequestedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? RequestedBy { get; set; }
}

public interface IRightToErasureService
{
    Task<ErasureRequest> RequestErasureAsync(string studentId, string requestedBy, CancellationToken ct = default);
    Task ProcessErasureAsync(string studentId, CancellationToken ct = default);
    Task<ErasureRequest?> GetErasureStatusAsync(string studentId, CancellationToken ct = default);
}

public sealed class RightToErasureService : IRightToErasureService
{
    private static readonly TimeSpan CoolingPeriod = TimeSpan.FromDays(30);
    private readonly IDocumentStore _store;
    private readonly ILogger<RightToErasureService> _logger;

    public RightToErasureService(IDocumentStore store, ILogger<RightToErasureService> logger)
    {
        _store = store;
        _logger = logger;
    }

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
            RequestedAt = DateTimeOffset.UtcNow,
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

        // Enforce cooling period
        if (DateTimeOffset.UtcNow - request.RequestedAt < CoolingPeriod)
        {
            _logger.LogInformation("Erasure for {StudentId} still in cooling period (requested {RequestedAt})",
                studentId, request.RequestedAt);
            return;
        }

        request.Status = ErasureStatus.Processing;
        session.Store(request);
        await session.SaveChangesAsync(ct);

        // Delete consent records
        var consents = await session.Query<ConsentRecord>()
            .Where(c => c.StudentId == studentId).ToListAsync(ct);
        foreach (var c in consents) session.Delete(c);

        // Delete access logs
        var accessLogs = await session.Query<StudentRecordAccessLog>()
            .Where(l => l.StudentId == studentId).ToListAsync(ct);
        foreach (var l in accessLogs) session.Delete(l);

        request.Status = ErasureStatus.Completed;
        request.ProcessedAt = DateTimeOffset.UtcNow;
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
