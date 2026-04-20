// =============================================================================
// Cena Platform — InMemoryParentDigestPreferencesStore (prr-051).
//
// Thread-safe in-memory store. Used for tests, local dev, and the first
// production phase while the Marten-backed projection is built out.
// Production cutover flips the DI registration; endpoint code does not
// change.
// =============================================================================

using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Cena.Actors.ParentDigest;

/// <summary>
/// Thread-safe in-memory <see cref="IParentDigestPreferencesStore"/>.
/// </summary>
public sealed class InMemoryParentDigestPreferencesStore : IParentDigestPreferencesStore
{
    // Keyed on the full triple so cross-tenant probes miss naturally.
    private readonly ConcurrentDictionary<PrefKey, ParentDigestPreferences> _rows = new();

    public Task<ParentDigestPreferences?> FindAsync(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(parentActorId) ||
            string.IsNullOrWhiteSpace(studentSubjectId) ||
            string.IsNullOrWhiteSpace(instituteId))
        {
            return Task.FromResult<ParentDigestPreferences?>(null);
        }

        var key = new PrefKey(parentActorId, studentSubjectId, instituteId);
        return _rows.TryGetValue(key, out var row)
            ? Task.FromResult<ParentDigestPreferences?>(row)
            : Task.FromResult<ParentDigestPreferences?>(null);
    }

    public Task<ParentDigestPreferences> ApplyUpdateAsync(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        ImmutableDictionary<DigestPurpose, OptInStatus> updates,
        DateTimeOffset updatedAtUtc,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(updates);
        if (string.IsNullOrWhiteSpace(parentActorId))
            throw new ArgumentException("parentActorId required", nameof(parentActorId));
        if (string.IsNullOrWhiteSpace(studentSubjectId))
            throw new ArgumentException("studentSubjectId required", nameof(studentSubjectId));
        if (string.IsNullOrWhiteSpace(instituteId))
            throw new ArgumentException("instituteId required", nameof(instituteId));

        var key = new PrefKey(parentActorId, studentSubjectId, instituteId);
        var next = _rows.AddOrUpdate(
            key,
            _ => ParentDigestPreferences
                .Empty(parentActorId, studentSubjectId, instituteId, updatedAtUtc)
                .WithUpdates(updates, updatedAtUtc),
            (_, existing) => existing.WithUpdates(updates, updatedAtUtc));
        return Task.FromResult(next);
    }

    public Task<ParentDigestPreferences> ApplyUnsubscribeAllAsync(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        DateTimeOffset unsubscribedAtUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(parentActorId))
            throw new ArgumentException("parentActorId required", nameof(parentActorId));
        if (string.IsNullOrWhiteSpace(studentSubjectId))
            throw new ArgumentException("studentSubjectId required", nameof(studentSubjectId));
        if (string.IsNullOrWhiteSpace(instituteId))
            throw new ArgumentException("instituteId required", nameof(instituteId));

        var key = new PrefKey(parentActorId, studentSubjectId, instituteId);
        var next = _rows.AddOrUpdate(
            key,
            _ => ParentDigestPreferences
                .Empty(parentActorId, studentSubjectId, instituteId, unsubscribedAtUtc)
                .AsFullyUnsubscribed(unsubscribedAtUtc),
            (_, existing) => existing.AsFullyUnsubscribed(unsubscribedAtUtc));
        return Task.FromResult(next);
    }

    private readonly record struct PrefKey(
        string ParentActorId,
        string StudentSubjectId,
        string InstituteId);
}
