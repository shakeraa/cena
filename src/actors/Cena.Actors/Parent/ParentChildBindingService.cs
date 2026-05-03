// =============================================================================
// Cena Platform — ParentChildBindingService (prr-009, EPIC-PRR-C)
//
// Glue between the authoritative binding store (Cena.Actors.Parent) and the
// guard (Cena.Infrastructure.Security). Lives here because the store is a
// bounded-context primitive; Infrastructure declares the service interface
// so it can consume bindings without a circular reference to Actors.
// =============================================================================

using Cena.Infrastructure.Security;

namespace Cena.Actors.Parent;

/// <summary>
/// Reads the authoritative binding store. No caching layer here —
/// ADR-0041 says revocation must take effect "at most one session cycle
/// after revocation", and a cache would widen that window.
/// </summary>
public sealed class ParentChildBindingService : IParentChildBindingService
{
    private readonly IParentChildBindingStore _store;

    public ParentChildBindingService(IParentChildBindingStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<bool> IsBindingActiveAsync(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        CancellationToken ct = default)
    {
        var binding = await _store
            .FindActiveAsync(parentActorId, studentSubjectId, instituteId, ct)
            .ConfigureAwait(false);
        // FindActiveAsync only returns rows whose InstituteId equals the
        // argument and whose RevokedAtUtc is null, so presence is the
        // answer. Refusing to return the row to callers avoids leaking
        // GrantedAt metadata through the guard.
        return binding is { IsActive: true };
    }
}
