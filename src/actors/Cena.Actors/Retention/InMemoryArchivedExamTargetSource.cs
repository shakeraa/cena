// =============================================================================
// Cena Platform — In-memory archived-target source + noop notifier (prr-229)
//
// Phase-1 in-memory impl. Holds a list of ArchivedExamTargetRow records
// pushed in by whatever host wires the worker. Test code and Phase-1
// production composition both drive through this type; the
// Marten-backed overlay lands with prr-218 completion.
// =============================================================================

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Cena.Actors.ExamTargets;

namespace Cena.Actors.Retention;

/// <summary>
/// In-memory implementation of <see cref="IArchivedExamTargetSource"/>.
/// Exposes Append / Remove helpers so Phase-1 hosts (and tests) can
/// drive it; the production overlay will not need these helpers.
/// </summary>
public sealed class InMemoryArchivedExamTargetSource
    : IArchivedExamTargetSource
{
    private readonly ConcurrentDictionary<string, ArchivedExamTargetRow> _rows
        = new(StringComparer.Ordinal);

    /// <summary>
    /// Append an archived-target row. The key is
    /// <c>{StudentAnonId}|{ExamTargetCode}</c> so one row per (student,
    /// target) pair (ADR-0050 §5 — archive is terminal, so no double-add
    /// per target).
    /// </summary>
    public void Append(ArchivedExamTargetRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _rows[KeyOf(row)] = row;
    }

    /// <summary>
    /// Remove a row. Called by the retention worker itself after a
    /// successful shred so we don't re-process on the next sweep.
    /// </summary>
    public bool Remove(string studentAnonId, ExamTargetCode examTargetCode)
    {
        return _rows.TryRemove(KeyOf(studentAnonId, examTargetCode), out _);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ArchivedExamTargetRow> ListArchivedAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var kv in _rows)
        {
            ct.ThrowIfCancellationRequested();
            yield return kv.Value;
        }
        await Task.CompletedTask;
    }

    private static string KeyOf(ArchivedExamTargetRow row)
        => KeyOf(row.StudentAnonId, row.ExamTargetCode);

    private static string KeyOf(string studentAnonId, ExamTargetCode code)
        => studentAnonId + "|" + code.Value;
}

/// <summary>
/// No-op notifier. Used by unit tests and the Phase-1 composition when
/// the notification dispatcher is not available (e.g. in sidecar hosts).
/// </summary>
public sealed class NoopRetentionShredNotifier : IRetentionShredNotifier
{
    /// <inheritdoc />
    public Task NotifyShreddedAsync(
        string studentAnonId,
        ExamTargetCode examTargetCode,
        DateTimeOffset shreddedAtUtc,
        CancellationToken ct = default)
        => Task.CompletedTask;
}
