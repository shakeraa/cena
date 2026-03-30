// =============================================================================
// Cena Platform -- Diagram Cache (LLM-009)
// Layer: Actors / Diagrams | Runtime: .NET 9
//
// Persists generated diagrams in Marten (PostgreSQL) keyed by a hash of the
// request parameters. Avoids redundant LLM calls for identical requests.
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagrams;

/// <summary>
/// Caches diagram generation results, backed by Marten document store.
/// </summary>
public interface IDiagramCache
{
    Task<DiagramCacheDocument?> GetAsync(DiagramRequest request, CancellationToken ct = default);
    Task<DiagramCacheDocument?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<DiagramCacheDocument> StoreAsync(DiagramRequest request, DiagramResult result, CancellationToken ct = default);
    Task<IReadOnlyList<DiagramCacheDocument>> ListAsync(int page, int pageSize, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Marten document for cached diagrams.
/// </summary>
public sealed class DiagramCacheDocument
{
    public Guid Id { get; set; }
    public string RequestHash { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Topic { get; set; } = "";
    public DiagramType DiagramType { get; set; }
    public string Description { get; set; } = "";
    public string Language { get; set; } = "he";
    public string SvgContent { get; set; } = "";
    public string MermaidCode { get; set; } = "";
    public string ResultDescription { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public int HitCount { get; set; }
}

public sealed class DiagramCache : IDiagramCache
{
    private readonly IDocumentStore _store;
    private readonly ILogger<DiagramCache> _logger;

    public DiagramCache(IDocumentStore store, ILogger<DiagramCache> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DiagramCacheDocument?> GetAsync(DiagramRequest request, CancellationToken ct = default)
    {
        var hash = ComputeHash(request);

        await using var session = _store.QuerySession();
        var doc = await session.Query<DiagramCacheDocument>()
            .FirstOrDefaultAsync(d => d.RequestHash == hash, ct);

        if (doc is null) return null;

        // Increment hit count
        await using var writeSession = _store.LightweightSession();
        doc.HitCount++;
        writeSession.Store(doc);
        await writeSession.SaveChangesAsync(ct);

        _logger.LogDebug("Diagram cache hit for {Subject}/{Topic} (hits: {HitCount})",
            request.Subject, request.Topic, doc.HitCount);

        return doc;
    }

    public async Task<DiagramCacheDocument?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        return await session.LoadAsync<DiagramCacheDocument>(id, ct);
    }

    public async Task<DiagramCacheDocument> StoreAsync(
        DiagramRequest request, DiagramResult result, CancellationToken ct = default)
    {
        var hash = ComputeHash(request);

        var doc = new DiagramCacheDocument
        {
            Id = Guid.NewGuid(),
            RequestHash = hash,
            Subject = request.Subject,
            Topic = request.Topic,
            DiagramType = request.DiagramType,
            Description = request.Description,
            Language = request.Language,
            SvgContent = result.SvgContent,
            MermaidCode = result.MermaidCode,
            ResultDescription = result.Description,
            CreatedAt = result.GeneratedAt,
            HitCount = 0
        };

        await using var session = _store.LightweightSession();
        session.Store(doc);
        await session.SaveChangesAsync(ct);

        _logger.LogInformation("Cached diagram {Id} for {Subject}/{Topic} ({DiagramType})",
            doc.Id, request.Subject, request.Topic, request.DiagramType);

        return doc;
    }

    public async Task<IReadOnlyList<DiagramCacheDocument>> ListAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        await using var session = _store.QuerySession();
        return await session.Query<DiagramCacheDocument>()
            .OrderByDescending(d => d.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();
        var existing = await session.LoadAsync<DiagramCacheDocument>(id, ct);
        if (existing is null) return false;

        session.Delete(existing);
        await session.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted cached diagram {Id}", id);
        return true;
    }

    // ── Hashing ──

    internal static string ComputeHash(DiagramRequest request)
    {
        var input = $"{request.Subject}|{request.Topic}|{request.DiagramType}|{request.Description}|{request.Language}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }
}
