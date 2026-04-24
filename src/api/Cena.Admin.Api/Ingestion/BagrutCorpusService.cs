// =============================================================================
// Cena Platform — Bagrut corpus service (prr-242, ADR-0043)
//
// Production implementation of IMinistryReferenceCorpus (the interface the
// MinistrySimilarityChecker used to depend on via EmptyMinistryReferenceCorpus).
// Backed by Marten-persisted BagrutCorpusItemDocument rows that land through
// the PDF ingestion pipeline.
//
// Responsibilities:
//   1. `UpsertAsync` — called by the ingestion layer with a computed
//      corpus item; idempotent on `Id`.
//   2. `QueryByTagsAsync` — the admin "corpus coverage" dashboard + the
//      isomorph prompt-builder call this to pull seed items for a given
//      exam/topic/track tuple.
//   3. `GetReferences(subject, trackKey)` — the IMinistryReferenceCorpus
//      contract the similarity checker calls. Returns normalised stems
//      only (RawText stays on the aggregate side).
//
// Caching: the GetReferences call is on the hot path (every candidate
// isomorph runs the similarity check). We warm an in-memory `refCache`
// keyed by (subject, trackKey), invalidated on Upsert.
// =============================================================================

using Cena.Actors.QuestionBank.Coverage;
using Cena.Infrastructure.Documents;
using Marten;

namespace Cena.Admin.Api.Ingestion;

/// <summary>
/// Tags used to query the corpus. Any null field is "don't care".
/// </summary>
public sealed record BagrutCorpusQuery(
    string? MinistrySubjectCode = null,
    string? MinistryQuestionPaperCode = null,
    int? Units = null,
    int? Year = null,
    BagrutCorpusSeason? Season = null,
    string? TopicId = null,
    BagrutCorpusStream? Stream = null,
    int Limit = 50);

public interface IBagrutCorpusService
{
    /// <summary>Persist a corpus item; idempotent on id.</summary>
    Task UpsertAsync(BagrutCorpusItemDocument item, CancellationToken ct = default);

    /// <summary>Bulk upsert; used by the PDF ingestion pipeline.</summary>
    Task UpsertManyAsync(
        IReadOnlyList<BagrutCorpusItemDocument> items,
        CancellationToken ct = default);

    /// <summary>Tag-filtered read for the isomorph prompt builder + admin dashboard.</summary>
    Task<IReadOnlyList<BagrutCorpusItemDocument>> QueryByTagsAsync(
        BagrutCorpusQuery query, CancellationToken ct = default);

    /// <summary>Count rows — powers the coverage dashboard.</summary>
    Task<int> CountAsync(BagrutCorpusQuery query, CancellationToken ct = default);
}

public sealed class BagrutCorpusService : IBagrutCorpusService, IMinistryReferenceCorpus
{
    private readonly IDocumentStore _store;
    private readonly Dictionary<(string subj, string track), IReadOnlyList<MinistryReferenceItem>> _refCache
        = new();
    private readonly object _cacheLock = new();

    public BagrutCorpusService(IDocumentStore store)
    {
        _store = store;
    }

    public async Task UpsertAsync(BagrutCorpusItemDocument item, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ValidateItem(item);

        await using var session = _store.LightweightSession();
        session.Store(item);
        await session.SaveChangesAsync(ct);

        InvalidateCache(item);
    }

    public async Task UpsertManyAsync(
        IReadOnlyList<BagrutCorpusItemDocument> items,
        CancellationToken ct = default)
    {
        if (items is null || items.Count == 0) return;

        foreach (var item in items) ValidateItem(item);

        await using var session = _store.LightweightSession();
        foreach (var item in items) session.Store(item);
        await session.SaveChangesAsync(ct);

        foreach (var item in items) InvalidateCache(item);
    }

    public async Task<IReadOnlyList<BagrutCorpusItemDocument>> QueryByTagsAsync(
        BagrutCorpusQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var session = _store.QuerySession();

        var q = session.Query<BagrutCorpusItemDocument>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.MinistrySubjectCode))
            q = q.Where(x => x.MinistrySubjectCode == query.MinistrySubjectCode);
        if (!string.IsNullOrWhiteSpace(query.MinistryQuestionPaperCode))
            q = q.Where(x => x.MinistryQuestionPaperCode == query.MinistryQuestionPaperCode);
        if (query.Units is int u)
            q = q.Where(x => x.Units == u);
        if (query.Year is int y)
            q = q.Where(x => x.Year == y);
        if (query.Season is BagrutCorpusSeason s)
            q = q.Where(x => x.Season == s);
        if (!string.IsNullOrWhiteSpace(query.TopicId))
            q = q.Where(x => x.TopicId == query.TopicId);
        if (query.Stream is BagrutCorpusStream st)
            q = q.Where(x => x.Stream == st);

        var limit = query.Limit > 0 ? query.Limit : 50;
        var result = await q.Take(limit).ToListAsync(ct);
        return result.Cast<BagrutCorpusItemDocument>().ToList();
    }

    public async Task<int> CountAsync(BagrutCorpusQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var session = _store.QuerySession();

        var q = session.Query<BagrutCorpusItemDocument>().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.MinistrySubjectCode))
            q = q.Where(x => x.MinistrySubjectCode == query.MinistrySubjectCode);
        if (!string.IsNullOrWhiteSpace(query.MinistryQuestionPaperCode))
            q = q.Where(x => x.MinistryQuestionPaperCode == query.MinistryQuestionPaperCode);
        if (query.Units is int u)
            q = q.Where(x => x.Units == u);
        if (query.Year is int y)
            q = q.Where(x => x.Year == y);
        if (query.Season is BagrutCorpusSeason s)
            q = q.Where(x => x.Season == s);
        if (!string.IsNullOrWhiteSpace(query.TopicId))
            q = q.Where(x => x.TopicId == query.TopicId);
        if (query.Stream is BagrutCorpusStream st)
            q = q.Where(x => x.Stream == st);

        return await q.CountAsync(ct);
    }

    // ---- IMinistryReferenceCorpus (hot path for similarity checker) ----

    public IReadOnlyList<MinistryReferenceItem> GetReferences(string subject, string trackKey)
    {
        var key = (subject ?? string.Empty, trackKey ?? string.Empty);

        lock (_cacheLock)
        {
            if (_refCache.TryGetValue(key, out var cached))
                return cached;
        }

        // Blocking Marten read outside the lock — GetReferences is
        // fundamentally synchronous per the upstream interface, but we
        // avoid holding the cache lock across I/O.
        using var session = _store.QuerySession();
        var ministrySubjectCode = MapSubjectToMinistryCode(subject ?? string.Empty);
        var items = session.Query<BagrutCorpusItemDocument>()
            .Where(x => x.MinistrySubjectCode == ministrySubjectCode
                        && x.TrackKey == trackKey)
            .Select(x => new MinistryReferenceItem(x.Id, x.NormalisedStem))
            .Take(500)
            .ToList();

        lock (_cacheLock)
        {
            _refCache[key] = items;
        }
        return items;
    }

    // ---- helpers ----

    private static void ValidateItem(BagrutCorpusItemDocument item)
    {
        if (string.IsNullOrWhiteSpace(item.Id))
            throw new ArgumentException("BagrutCorpusItem: Id required", nameof(item));
        if (string.IsNullOrWhiteSpace(item.MinistrySubjectCode))
            throw new ArgumentException("BagrutCorpusItem: MinistrySubjectCode required", nameof(item));
        if (string.IsNullOrWhiteSpace(item.MinistryQuestionPaperCode))
            throw new ArgumentException("BagrutCorpusItem: MinistryQuestionPaperCode required", nameof(item));
        if (item.QuestionNumber <= 0)
            throw new ArgumentException("BagrutCorpusItem: QuestionNumber must be > 0", nameof(item));
        if (string.IsNullOrWhiteSpace(item.NormalisedStem))
            throw new ArgumentException("BagrutCorpusItem: NormalisedStem required", nameof(item));
    }

    private void InvalidateCache(BagrutCorpusItemDocument item)
    {
        // Keyed by (subject-string, trackKey) — the similarity checker asks
        // with a logical subject name (e.g. "math") that maps to "035";
        // invalidate both possible keys to be safe.
        lock (_cacheLock)
        {
            var toRemove = _refCache.Keys
                .Where(k => string.Equals(k.track, item.TrackKey, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var k in toRemove) _refCache.Remove(k);
        }
    }

    // Subject-name → ministry subject code. Conservative mapping; unknown
    // subject strings pass through to the caller as-is so a future catalog
    // entry doesn't silently stop finding references.
    internal static string MapSubjectToMinistryCode(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return string.Empty;
        return subject.Trim().ToLowerInvariant() switch
        {
            "math" or "mathematics" or "מתמטיקה" => "035",
            _ => subject.Trim(),
        };
    }
}
