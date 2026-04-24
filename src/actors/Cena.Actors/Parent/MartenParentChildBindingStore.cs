// =============================================================================
// Cena Platform — MartenParentChildBindingStore (prr-009 / EPIC-PRR-C prod)
//
// Production Marten-backed implementation of IParentChildBindingStore.
// Replaces InMemoryParentChildBindingStore as the production DI binding
// per memory "No stubs — production grade" (2026-04-11). Without this,
// every parent-child grant is erased on every host restart: a compliance
// violation because the authorization guard reads this store as the
// authoritative source of truth (the JWT `parent_of` cache is advisory,
// per ADR-0041 §3 and the ParentChildBindingService.cs comment).
//
// Pattern mirrors MartenSkillKeyedMasteryStore (prr-222) and
// MartenExamTargetRetentionExtensionStore (prr-229). Document-style:
// bindings are profile state, not event-sourced. Revocations set
// RevokedAtUtc on the document (preserving the audit row) rather than
// deleting; per ADR-0042 the consent context is append-only.
// =============================================================================

using Cena.Actors.Subscriptions;
using Marten;

namespace Cena.Actors.Parent;

/// <summary>
/// Marten-backed implementation of <see cref="IParentChildBindingStore"/>.
/// Thread safety is delegated to Marten's session-per-unit-of-work model
/// (a fresh LightweightSession per write, fresh QuerySession per read).
/// Also implements <see cref="IStudentParentIndex"/> (PRR-344) so the
/// alpha-migration grace path can enumerate parents bound to a given
/// student via an indexed Postgres query.
/// </summary>
public sealed class MartenParentChildBindingStore : IParentChildBindingStore, IStudentParentIndex
{
    private readonly IDocumentStore _store;

    public MartenParentChildBindingStore(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<ParentChildBinding?> FindActiveAsync(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(parentActorId) ||
            string.IsNullOrWhiteSpace(studentSubjectId) ||
            string.IsNullOrWhiteSpace(instituteId))
        {
            return null;
        }

        var docId = ParentChildBindingDocument.MakeId(
            parentActorId, studentSubjectId, instituteId);

        await using var session = _store.QuerySession();
        var doc = await session
            .LoadAsync<ParentChildBindingDocument>(docId, ct)
            .ConfigureAwait(false);

        // Refuse to leak the revoked row — callers see "active or nothing"
        // to match the InMemory contract. Surfacing the revoked form would
        // be an existence oracle (per the TeacherOverride ADR-0001 precedent
        // cited in the interface docstring).
        if (doc is null || doc.RevokedAtUtc is not null)
        {
            return null;
        }

        return ToDomain(doc);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ParentChildBinding>> ListActiveForParentAsync(
        string parentActorId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(parentActorId))
        {
            return Array.Empty<ParentChildBinding>();
        }

        await using var session = _store.QuerySession();
        var docs = await session
            .Query<ParentChildBindingDocument>()
            .Where(d => d.ParentActorId == parentActorId
                     && d.RevokedAtUtc == null)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return docs.Select(ToDomain).ToList();
    }

    /// <inheritdoc />
    public async Task<ParentChildBinding> GrantAsync(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        DateTimeOffset grantedAtUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(parentActorId))
        {
            throw new ArgumentException("parentActorId required", nameof(parentActorId));
        }
        if (string.IsNullOrWhiteSpace(studentSubjectId))
        {
            throw new ArgumentException(
                "studentSubjectId required",
                nameof(studentSubjectId));
        }
        if (string.IsNullOrWhiteSpace(instituteId))
        {
            throw new ArgumentException("instituteId required", nameof(instituteId));
        }

        var docId = ParentChildBindingDocument.MakeId(
            parentActorId, studentSubjectId, instituteId);

        await using var session = _store.LightweightSession();
        var existing = await session
            .LoadAsync<ParentChildBindingDocument>(docId, ct)
            .ConfigureAwait(false);

        // Idempotent: if an ACTIVE binding exists, return it as-is.
        // If the row is revoked or missing, create a fresh active binding
        // anchored at the new grantedAtUtc — matches the InMemory
        // AddOrUpdate branching.
        ParentChildBindingDocument doc;
        if (existing is not null && existing.RevokedAtUtc is null)
        {
            return ToDomain(existing);
        }

        doc = new ParentChildBindingDocument
        {
            Id = docId,
            ParentActorId = parentActorId,
            StudentSubjectId = studentSubjectId,
            InstituteId = instituteId,
            GrantedAtUtc = grantedAtUtc,
            RevokedAtUtc = null,
        };
        session.Store(doc);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDomain(doc);
    }

    /// <inheritdoc />
    public async Task RevokeAsync(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        DateTimeOffset revokedAtUtc,
        CancellationToken ct = default)
    {
        var docId = ParentChildBindingDocument.MakeId(
            parentActorId, studentSubjectId, instituteId);

        await using var session = _store.LightweightSession();
        var existing = await session
            .LoadAsync<ParentChildBindingDocument>(docId, ct)
            .ConfigureAwait(false);

        // Match the InMemory AddOrUpdate semantics: if no row exists,
        // insert a tombstone row (Granted == Revoked). If a row exists,
        // stamp RevokedAtUtc on it. This preserves the audit row either
        // way — consent history is append-only per ADR-0042.
        var updated = existing is null
            ? new ParentChildBindingDocument
                {
                    Id = docId,
                    ParentActorId = parentActorId,
                    StudentSubjectId = studentSubjectId,
                    InstituteId = instituteId,
                    GrantedAtUtc = revokedAtUtc,
                    RevokedAtUtc = revokedAtUtc,
                }
            : existing with { RevokedAtUtc = revokedAtUtc };

        session.Store(updated);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListParentsForStudentAsync(
        string studentSubjectId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectId))
        {
            return Array.Empty<string>();
        }
        await using var session = _store.QuerySession();
        var parents = await session
            .Query<ParentChildBindingDocument>()
            .Where(d => d.StudentSubjectId == studentSubjectId
                     && d.RevokedAtUtc == null)
            .Select(d => d.ParentActorId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return parents.Distinct(StringComparer.Ordinal).ToList();
    }

    // ---- conversion ----

    private static ParentChildBinding ToDomain(ParentChildBindingDocument doc)
        => new(
            doc.ParentActorId,
            doc.StudentSubjectId,
            doc.InstituteId,
            doc.GrantedAtUtc,
            doc.RevokedAtUtc);
}
