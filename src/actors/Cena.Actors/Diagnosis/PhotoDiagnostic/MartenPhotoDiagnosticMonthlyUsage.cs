// =============================================================================
// Cena Platform — MartenPhotoDiagnosticMonthlyUsage (EPIC-PRR-J PRR-400)
//
// Production Marten-backed implementation. Mirrors
// MartenSkillKeyedMasteryStore (prr-222) — document-style (not event
// sourced) for a running counter.
//
// Concurrency note: Marten doc-store default is last-writer-wins for
// documents without an UseOptimisticConcurrency attribute. For a monthly
// usage counter, a lost update on concurrent increments under-counts
// (student gets an extra diagnostic), which is strictly safer than
// over-counting (wrongly denying a paid student). Given per-student
// intake is single-device + rate-limited upstream by the UI, the
// collision probability is near-zero in practice; if it ever becomes
// material we can switch to a single-statement SQL upsert with
// `count = count + 1` via ISession.Connection.
// =============================================================================

using Marten;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public sealed class MartenPhotoDiagnosticMonthlyUsage : IPhotoDiagnosticMonthlyUsage
{
    private readonly IDocumentStore _store;
    private readonly TimeProvider _clock;

    public MartenPhotoDiagnosticMonthlyUsage(IDocumentStore store, TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<int> GetAsync(string studentSubjectIdHash, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        var id = PhotoDiagnosticUsageDocument.KeyOf(studentSubjectIdHash, MonthlyUsageKey.For(asOfUtc));
        await using var session = _store.QuerySession();
        var doc = await session.LoadAsync<PhotoDiagnosticUsageDocument>(id, ct).ConfigureAwait(false);
        return doc?.Count ?? 0;
    }

    public async Task<int> IncrementAsync(string studentSubjectIdHash, DateTimeOffset asOfUtc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));

        var monthKey = MonthlyUsageKey.For(asOfUtc);
        var id = PhotoDiagnosticUsageDocument.KeyOf(studentSubjectIdHash, monthKey);

        await using var session = _store.LightweightSession();
        var doc = await session.LoadAsync<PhotoDiagnosticUsageDocument>(id, ct).ConfigureAwait(false);
        var next = (doc?.Count ?? 0) + 1;
        var updated = new PhotoDiagnosticUsageDocument
        {
            Id = id,
            StudentSubjectIdHash = studentSubjectIdHash,
            MonthKey = monthKey,
            Count = next,
            UpdatedAt = _clock.GetUtcNow(),
        };
        session.Store(updated);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
        return next;
    }
}
