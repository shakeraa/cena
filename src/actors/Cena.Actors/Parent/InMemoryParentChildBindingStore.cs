// =============================================================================
// Cena Platform — InMemoryParentChildBindingStore (prr-009, EPIC-PRR-C)
//
// In-process store used for tests and local dev until the Marten-backed
// implementation lands (tracked under EPIC-PRR-A Sprint 2). Thread-safe
// for concurrent test harness use.
// =============================================================================

using System.Collections.Concurrent;
using Cena.Actors.Subscriptions;

namespace Cena.Actors.Parent;

/// <summary>
/// Thread-safe in-memory <see cref="IParentChildBindingStore"/>.
/// Production flips the DI registration to the Marten-backed variant
/// without touching any endpoint code. Also implements
/// <see cref="IStudentParentIndex"/> so the alpha-migration grace path
/// in <see cref="StudentEntitlementResolver"/> can enumerate the
/// parents bound to a given student (PRR-344).
/// </summary>
public sealed class InMemoryParentChildBindingStore : IParentChildBindingStore, IStudentParentIndex
{
    // Keyed on (parent, student, institute) — the full triple. A parent
    // with the same child enrolled at two institutes has two rows.
    private readonly ConcurrentDictionary<BindingKey, ParentChildBinding> _bindings = new();

    public Task<ParentChildBinding?> FindActiveAsync(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(parentActorId) ||
            string.IsNullOrWhiteSpace(studentSubjectId) ||
            string.IsNullOrWhiteSpace(instituteId))
        {
            return Task.FromResult<ParentChildBinding?>(null);
        }

        var key = new BindingKey(parentActorId, studentSubjectId, instituteId);
        if (_bindings.TryGetValue(key, out var binding) && binding.IsActive)
            return Task.FromResult<ParentChildBinding?>(binding);

        return Task.FromResult<ParentChildBinding?>(null);
    }

    public Task<IReadOnlyList<ParentChildBinding>> ListActiveForParentAsync(
        string parentActorId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(parentActorId))
            return Task.FromResult<IReadOnlyList<ParentChildBinding>>(Array.Empty<ParentChildBinding>());

        var active = _bindings.Values
            .Where(b => b.IsActive &&
                        string.Equals(b.ParentActorId, parentActorId, StringComparison.Ordinal))
            .ToList();
        return Task.FromResult<IReadOnlyList<ParentChildBinding>>(active);
    }

    public Task<ParentChildBinding> GrantAsync(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        DateTimeOffset grantedAtUtc,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(parentActorId))
            throw new ArgumentException("parentActorId required", nameof(parentActorId));
        if (string.IsNullOrWhiteSpace(studentSubjectId))
            throw new ArgumentException("studentSubjectId required", nameof(studentSubjectId));
        if (string.IsNullOrWhiteSpace(instituteId))
            throw new ArgumentException("instituteId required", nameof(instituteId));

        var key = new BindingKey(parentActorId, studentSubjectId, instituteId);
        var binding = _bindings.AddOrUpdate(
            key,
            _ => new ParentChildBinding(parentActorId, studentSubjectId, instituteId, grantedAtUtc),
            (_, existing) => existing.IsActive
                ? existing
                : new ParentChildBinding(parentActorId, studentSubjectId, instituteId, grantedAtUtc));
        return Task.FromResult(binding);
    }

    public Task RevokeAsync(
        string parentActorId,
        string studentSubjectId,
        string instituteId,
        DateTimeOffset revokedAtUtc,
        CancellationToken ct = default)
    {
        var key = new BindingKey(parentActorId, studentSubjectId, instituteId);
        _bindings.AddOrUpdate(
            key,
            _ => new ParentChildBinding(
                parentActorId, studentSubjectId, instituteId, revokedAtUtc, revokedAtUtc),
            (_, existing) => existing with { RevokedAtUtc = revokedAtUtc });
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListParentsForStudentAsync(
        string studentSubjectId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectId))
        {
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }
        var parents = _bindings.Values
            .Where(b => b.IsActive &&
                        string.Equals(b.StudentSubjectId, studentSubjectId, StringComparison.Ordinal))
            .Select(b => b.ParentActorId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(parents);
    }

    private readonly record struct BindingKey(
        string ParentActorId,
        string StudentSubjectId,
        string InstituteId);
}
