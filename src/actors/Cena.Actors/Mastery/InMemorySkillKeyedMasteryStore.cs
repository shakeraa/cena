// =============================================================================
// Cena Platform — In-memory skill-keyed mastery store (prr-222)
//
// Thread-safe (ConcurrentDictionary) in-memory impl of
// ISkillKeyedMasteryStore. Serves as both the test-double and the
// Phase-1 production fallback (same pattern as
// InMemoryStudentPlanAggregateStore — Marten-backed overlay lands once
// the prr-218 aggregate stream catalog stabilises).
//
// Dedup invariant: one row per MasteryKey. Re-upserting for the same key
// overwrites in place; the ConcurrentDictionary guarantees atomicity.
// =============================================================================

using System.Collections.Concurrent;
using Cena.Actors.ExamTargets;

namespace Cena.Actors.Mastery;

/// <summary>
/// In-memory implementation of <see cref="ISkillKeyedMasteryStore"/>.
/// </summary>
public sealed class InMemorySkillKeyedMasteryStore : ISkillKeyedMasteryStore
{
    private readonly ConcurrentDictionary<MasteryKey, SkillKeyedMasteryRow> _rows = new();

    /// <inheritdoc />
    public Task<SkillKeyedMasteryRow?> TryGetAsync(
        MasteryKey key,
        CancellationToken ct = default)
    {
        _rows.TryGetValue(key, out var row);
        return Task.FromResult(row);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SkillKeyedMasteryRow>> ListByStudentAsync(
        string studentAnonId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException(
                "studentAnonId must be non-empty.",
                nameof(studentAnonId));
        }

        IReadOnlyList<SkillKeyedMasteryRow> result = _rows
            .Where(kv => string.Equals(
                kv.Key.StudentAnonId, studentAnonId, StringComparison.Ordinal))
            .Select(kv => kv.Value)
            .ToList();
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SkillKeyedMasteryRow>> ListByTargetAsync(
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

        IReadOnlyList<SkillKeyedMasteryRow> result = _rows
            .Where(kv =>
                string.Equals(
                    kv.Key.StudentAnonId, studentAnonId, StringComparison.Ordinal)
                && kv.Key.ExamTargetCode.Equals(examTargetCode))
            .Select(kv => kv.Value)
            .ToList();
        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task UpsertAsync(
        SkillKeyedMasteryRow row,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(row);
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

        _rows[row.Key] = row;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> DeleteByStudentAsync(
        string studentAnonId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException(
                "studentAnonId must be non-empty.",
                nameof(studentAnonId));
        }

        var toRemove = _rows
            .Where(kv => string.Equals(
                kv.Key.StudentAnonId, studentAnonId, StringComparison.Ordinal))
            .Select(kv => kv.Key)
            .ToList();

        var removed = 0;
        foreach (var k in toRemove)
        {
            if (_rows.TryRemove(k, out _))
            {
                removed++;
            }
        }
        return Task.FromResult(removed);
    }

    /// <inheritdoc />
    public Task<int> DeleteByTargetAsync(
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

        var toRemove = _rows
            .Where(kv =>
                string.Equals(
                    kv.Key.StudentAnonId, studentAnonId, StringComparison.Ordinal)
                && kv.Key.ExamTargetCode.Equals(examTargetCode))
            .Select(kv => kv.Key)
            .ToList();

        var removed = 0;
        foreach (var k in toRemove)
        {
            if (_rows.TryRemove(k, out _))
            {
                removed++;
            }
        }
        return Task.FromResult(removed);
    }
}
