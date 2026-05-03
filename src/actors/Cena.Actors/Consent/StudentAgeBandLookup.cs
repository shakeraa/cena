// =============================================================================
// Cena Platform — StudentAgeBandLookup (prr-052)
//
// Resolves a student's current AgeBand from their authoritative profile.
// This is the ONLY acceptable source of the age band for authorization
// decisions — a caller MUST NOT accept a band or age from a request
// parameter (the student could spoof it to claim veto rights they
// don't have).
//
// The implementation reads DateOfBirth from the Marten-projected
// StudentProfileSnapshot and delegates the arithmetic to
// AgeBandComputation. When DateOfBirth is absent (not yet captured by
// the age-gate flow), the service refuses to return a band — the
// endpoint handler should 403 rather than pick a default.
//
// Tenancy: the lookup does NOT enforce tenant scope. Callers that
// need tenant-gated reads must first pass ParentAuthorizationGuard
// or Marten's TenantScope filter; by the time this lookup runs the
// caller has already proved they may read the profile.
// =============================================================================

using Cena.Actors.Events;
using Marten;

namespace Cena.Actors.Consent;

/// <summary>
/// Resolves the authoritative <see cref="AgeBand"/> for a student subject.
/// </summary>
public interface IStudentAgeBandLookup
{
    /// <summary>
    /// Read the student's DateOfBirth from their profile snapshot and
    /// compute the age band as of <paramref name="asOf"/>. Returns null
    /// when the profile has no DateOfBirth yet (age-gate not completed);
    /// callers must treat null as "cannot authorize" and refuse the
    /// action.
    /// </summary>
    Task<AgeBand?> ResolveBandAsync(
        string studentSubjectId,
        DateTimeOffset asOf,
        CancellationToken ct = default);
}

/// <summary>
/// Marten-backed <see cref="IStudentAgeBandLookup"/>. Reads
/// <see cref="StudentProfileSnapshot"/> (inline projection) by student id.
/// </summary>
public sealed class MartenStudentAgeBandLookup : IStudentAgeBandLookup
{
    private readonly IDocumentStore _store;

    public MartenStudentAgeBandLookup(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<AgeBand?> ResolveBandAsync(
        string studentSubjectId,
        DateTimeOffset asOf,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectId))
        {
            return null;
        }

        await using var session = _store.QuerySession();
        var profile = await session.LoadAsync<StudentProfileSnapshot>(studentSubjectId, ct)
            .ConfigureAwait(false);
        if (profile?.DateOfBirth is null)
        {
            return null;
        }
        return AgeBandComputation.FromDateOfBirth(profile.DateOfBirth.Value, asOf);
    }
}

/// <summary>
/// In-memory lookup for tests + integration harnesses. Seed the (student,
/// DOB) map up front; <see cref="ResolveBandAsync"/> returns null for
/// unknown students, matching the "cannot authorize" contract.
/// </summary>
public sealed class InMemoryStudentAgeBandLookup : IStudentAgeBandLookup
{
    private readonly Dictionary<string, DateOnly> _dobs;

    public InMemoryStudentAgeBandLookup(IReadOnlyDictionary<string, DateOnly>? seed = null)
    {
        _dobs = seed is null
            ? new Dictionary<string, DateOnly>(StringComparer.Ordinal)
            : new Dictionary<string, DateOnly>(seed, StringComparer.Ordinal);
    }

    /// <summary>Register / update a student's DOB for tests.</summary>
    public void Set(string studentSubjectId, DateOnly dob)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectId))
        {
            throw new ArgumentException(
                "studentSubjectId must be non-empty.", nameof(studentSubjectId));
        }
        _dobs[studentSubjectId] = dob;
    }

    /// <inheritdoc />
    public Task<AgeBand?> ResolveBandAsync(
        string studentSubjectId,
        DateTimeOffset asOf,
        CancellationToken ct = default)
    {
        _ = ct;
        if (string.IsNullOrWhiteSpace(studentSubjectId)
            || !_dobs.TryGetValue(studentSubjectId, out var dob))
        {
            return Task.FromResult<AgeBand?>(null);
        }
        var band = AgeBandComputation.FromDateOfBirth(dob, asOf);
        return Task.FromResult<AgeBand?>(band);
    }
}
