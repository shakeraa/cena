// =============================================================================
// Cena Platform — In-memory retention-extension store (prr-229)
//
// Thread-safe (ConcurrentDictionary) implementation of
// IExamTargetRetentionExtensionStore. Mirrors the other Phase-1
// InMemory* stores in the aggregate catalog (StudentPlan, mastery).
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Retention;

/// <summary>
/// In-memory implementation of <see cref="IExamTargetRetentionExtensionStore"/>.
/// </summary>
public sealed class InMemoryExamTargetRetentionExtensionStore
    : IExamTargetRetentionExtensionStore
{
    private readonly ConcurrentDictionary<string, ExamTargetRetentionExtension> _rows
        = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<bool> IsExtendedAsync(
        string studentAnonId,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            return Task.FromResult(false);
        }
        if (!_rows.TryGetValue(studentAnonId, out var row))
        {
            return Task.FromResult(false);
        }
        return Task.FromResult(row.ExtendedUntilUtc > nowUtc);
    }

    /// <inheritdoc />
    public Task SetAsync(
        ExamTargetRetentionExtension extension,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(extension);
        if (string.IsNullOrWhiteSpace(extension.StudentAnonId))
        {
            throw new ArgumentException(
                "StudentAnonId must be non-empty.",
                nameof(extension));
        }
        if (extension.ExtendedUntilUtc <= extension.SetAtUtc)
        {
            throw new ArgumentException(
                "ExtendedUntilUtc must be strictly after SetAtUtc.",
                nameof(extension));
        }

        _rows[extension.StudentAnonId] = extension;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<ExamTargetRetentionExtension?> TryGetAsync(
        string studentAnonId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            return Task.FromResult<ExamTargetRetentionExtension?>(null);
        }
        _rows.TryGetValue(studentAnonId, out var row);
        return Task.FromResult<ExamTargetRetentionExtension?>(row);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(
        string studentAnonId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            return Task.FromResult(false);
        }
        return Task.FromResult(_rows.TryRemove(studentAnonId, out _));
    }
}
