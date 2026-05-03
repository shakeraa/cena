// =============================================================================
// Cena Platform — MartenSkillKeyedMasteryStore (prr-222 production binding)
//
// Production Marten-backed implementation of ISkillKeyedMasteryStore.
// Replaces InMemorySkillKeyedMasteryStore as the production DI binding
// per memory "No stubs — production grade" (2026-04-11). Without this,
// every BKT posterior a student builds up during sessions is erased on
// every actor-host or API-host restart — unacceptable at Launch.
//
// Pattern mirrors MartenStudentPlanAggregateStore (prr-218 production
// binding) and MartenSubscriptionAggregateStore (EPIC-PRR-I PRR-300).
// Document-style (not event-sourced): the skill-keyed mastery projection
// is the source of truth for BKT P(L), so it is persisted as a document
// table, not derived from a separate event stream.
//
// Thread safety is delegated to Marten's session-per-unit-of-work model
// (a fresh LightweightSession per write, fresh QuerySession per read).
// =============================================================================

using Cena.Actors.ExamTargets;
using Marten;

namespace Cena.Actors.Mastery;

/// <summary>
/// Marten-backed implementation of <see cref="ISkillKeyedMasteryStore"/>.
/// Persists <see cref="SkillKeyedMasteryDocument"/> rows keyed on the
/// canonical <c>studentAnonId|examTargetCode|skillCode</c> form produced
/// by <see cref="MasteryKey.ToString"/>.
/// </summary>
public sealed class MartenSkillKeyedMasteryStore : ISkillKeyedMasteryStore
{
    private readonly IDocumentStore _store;

    public MartenSkillKeyedMasteryStore(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<SkillKeyedMasteryRow?> TryGetAsync(
        MasteryKey key,
        CancellationToken ct = default)
    {
        var docId = key.ToString();
        await using var session = _store.QuerySession();
        var doc = await session
            .LoadAsync<SkillKeyedMasteryDocument>(docId, ct)
            .ConfigureAwait(false);
        return doc is null ? null : ToRow(doc);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SkillKeyedMasteryRow>> ListByStudentAsync(
        string studentAnonId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException(
                "studentAnonId must be non-empty.",
                nameof(studentAnonId));
        }

        await using var session = _store.QuerySession();
        var docs = await session
            .Query<SkillKeyedMasteryDocument>()
            .Where(d => d.StudentAnonId == studentAnonId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return docs.Select(ToRow).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SkillKeyedMasteryRow>> ListByTargetAsync(
        string studentAnonId,
        ExamTargetCode examTargetCode,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException(
                "studentAnonId must be non-empty.",
                nameof(studentAnonId));
        }

        var targetValue = examTargetCode.Value;
        await using var session = _store.QuerySession();
        var docs = await session
            .Query<SkillKeyedMasteryDocument>()
            .Where(d => d.StudentAnonId == studentAnonId
                     && d.ExamTargetCode == targetValue)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return docs.Select(ToRow).ToList();
    }

    /// <inheritdoc />
    public async Task UpsertAsync(
        SkillKeyedMasteryRow row,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);

        // Invariant checks replicated from InMemorySkillKeyedMasteryStore
        // so the contract holds identically across both bindings.
        if (row.MasteryProbability is < 0.001f or > 0.999f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(row),
                row.MasteryProbability,
                "MasteryProbability must be in [0.001, 0.999] per BktTracer clamp.");
        }
        if (row.AttemptCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(row),
                row.AttemptCount,
                "AttemptCount must be non-negative.");
        }

        var doc = ToDocument(row);
        await using var session = _store.LightweightSession();
        session.Store(doc);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> DeleteByStudentAsync(
        string studentAnonId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException(
                "studentAnonId must be non-empty.",
                nameof(studentAnonId));
        }

        await using var session = _store.LightweightSession();

        // Count-then-delete so the return value matches the InMemory
        // implementation (actual rows removed). DeleteWhere compiles to
        // a single SQL DELETE — no N+1.
        var count = await session
            .Query<SkillKeyedMasteryDocument>()
            .CountAsync(d => d.StudentAnonId == studentAnonId, ct)
            .ConfigureAwait(false);
        if (count == 0)
        {
            return 0;
        }

        session.DeleteWhere<SkillKeyedMasteryDocument>(
            d => d.StudentAnonId == studentAnonId);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
        return count;
    }

    /// <inheritdoc />
    public async Task<int> DeleteByTargetAsync(
        string studentAnonId,
        ExamTargetCode examTargetCode,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException(
                "studentAnonId must be non-empty.",
                nameof(studentAnonId));
        }

        var targetValue = examTargetCode.Value;
        await using var session = _store.LightweightSession();

        var count = await session
            .Query<SkillKeyedMasteryDocument>()
            .CountAsync(d => d.StudentAnonId == studentAnonId
                          && d.ExamTargetCode == targetValue, ct)
            .ConfigureAwait(false);
        if (count == 0)
        {
            return 0;
        }

        session.DeleteWhere<SkillKeyedMasteryDocument>(
            d => d.StudentAnonId == studentAnonId
              && d.ExamTargetCode == targetValue);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
        return count;
    }

    // ---- row <-> document conversion ----

    private static SkillKeyedMasteryRow ToRow(SkillKeyedMasteryDocument doc)
    {
        var key = MasteryKey.From(
            doc.StudentAnonId,
            doc.ExamTargetCode,
            doc.SkillCode);
        return new SkillKeyedMasteryRow(
            key,
            doc.MasteryProbability,
            doc.AttemptCount,
            doc.UpdatedAt,
            doc.Source);
    }

    private static SkillKeyedMasteryDocument ToDocument(SkillKeyedMasteryRow row)
        => new()
        {
            Id = row.Key.ToString(),
            StudentAnonId = row.Key.StudentAnonId,
            ExamTargetCode = row.Key.ExamTargetCode.Value,
            SkillCode = row.Key.SkillCode.Value,
            MasteryProbability = row.MasteryProbability,
            AttemptCount = row.AttemptCount,
            UpdatedAt = row.UpdatedAt,
            Source = row.Source,
        };
}
