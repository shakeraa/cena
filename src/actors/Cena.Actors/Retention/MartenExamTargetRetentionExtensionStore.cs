// =============================================================================
// Cena Platform — MartenExamTargetRetentionExtensionStore (prr-229 prod)
//
// Production Marten-backed implementation of IExamTargetRetentionExtensionStore.
// Replaces InMemoryExamTargetRetentionExtensionStore as the production DI
// binding per memory "No stubs — production grade" (2026-04-11). Without
// this, every student opt-in to the 60-month extended retention window
// (ADR-0050 §6) is erased on every host restart and the retention worker
// falls back to the 24-month default — silently violating the opted-in
// student's preference.
//
// Pattern mirrors MartenSkillKeyedMasteryStore (prr-222) and
// MartenStudentPlanAggregateStore (prr-218). Document-style; Id is the
// pseudonymous student id (one row per student).
// =============================================================================

using Marten;

namespace Cena.Actors.Retention;

/// <summary>
/// Marten-backed implementation of <see cref="IExamTargetRetentionExtensionStore"/>.
/// </summary>
public sealed class MartenExamTargetRetentionExtensionStore
    : IExamTargetRetentionExtensionStore
{
    private readonly IDocumentStore _store;

    public MartenExamTargetRetentionExtensionStore(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<bool> IsExtendedAsync(
        string studentAnonId,
        DateTimeOffset nowUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            return false;
        }

        await using var session = _store.QuerySession();
        var doc = await session
            .LoadAsync<ExamTargetRetentionExtensionDocument>(studentAnonId, ct)
            .ConfigureAwait(false);
        return doc is not null && doc.ExtendedUntilUtc > nowUtc;
    }

    /// <inheritdoc />
    public async Task SetAsync(
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

        var doc = new ExamTargetRetentionExtensionDocument
        {
            Id = extension.StudentAnonId,
            SetAtUtc = extension.SetAtUtc,
            ExtendedUntilUtc = extension.ExtendedUntilUtc,
        };

        await using var session = _store.LightweightSession();
        session.Store(doc);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ExamTargetRetentionExtension?> TryGetAsync(
        string studentAnonId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            return null;
        }

        await using var session = _store.QuerySession();
        var doc = await session
            .LoadAsync<ExamTargetRetentionExtensionDocument>(studentAnonId, ct)
            .ConfigureAwait(false);
        if (doc is null)
        {
            return null;
        }
        return new ExamTargetRetentionExtension(
            doc.Id,
            doc.SetAtUtc,
            doc.ExtendedUntilUtc);
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        string studentAnonId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            return false;
        }

        await using var session = _store.LightweightSession();

        // LoadAsync then Delete so we can return whether the row existed,
        // matching the InMemory contract (bool return). A blind Delete
        // would always succeed silently against a non-existent key.
        var doc = await session
            .LoadAsync<ExamTargetRetentionExtensionDocument>(studentAnonId, ct)
            .ConfigureAwait(false);
        if (doc is null)
        {
            return false;
        }

        session.Delete<ExamTargetRetentionExtensionDocument>(studentAnonId);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }
}
