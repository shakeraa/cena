// =============================================================================
// Cena Platform — IStudentInstituteLookup (prr-150)
//
// Narrow abstraction the TeacherOverrideCommands handler uses to resolve
// the student's active enrollment institute for the tenant-scope check.
// Kept deliberately tiny and Marten-free so Cena.Actors does not drag in a
// transitive dependency on the EnrollmentDocument schema — the admin API
// composition root owns the Marten-backed concrete.
//
// Returning null means "student has no active enrollment the caller can
// see" — the command handler treats this identically to a tenant mismatch
// (the override is denied without leaking whether the student exists).
// =============================================================================

namespace Cena.Actors.Teacher.ScheduleOverride;

/// <summary>
/// Resolves the institute id for a student's active enrollment. Used by
/// <see cref="TeacherOverrideCommands"/> to enforce the ADR-0001 tenant
/// invariant before appending any event to the override stream.
/// </summary>
public interface IStudentInstituteLookup
{
    /// <summary>
    /// Return the institute id for the student's active enrollment, or
    /// null if no active enrollment is visible to the caller. Never
    /// throws on lookup miss — callers use null to deny the operation.
    /// </summary>
    Task<string?> GetActiveInstituteAsync(string studentAnonId, CancellationToken ct = default);
}

/// <summary>
/// In-memory test double. Ships with the bounded context so Phase-1
/// wiring does not require a concrete Marten backing to run end-to-end.
/// Admin API host composition registers a Marten-backed implementation
/// that queries <c>EnrollmentDocument</c> for the active enrollment.
/// </summary>
public sealed class InMemoryStudentInstituteLookup : IStudentInstituteLookup
{
    private readonly Dictionary<string, string> _map;

    public InMemoryStudentInstituteLookup(IDictionary<string, string>? seed = null)
    {
        _map = seed is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(seed, StringComparer.Ordinal);
    }

    /// <summary>
    /// Set or replace the active-institute binding for a student. Intended
    /// for test setup.
    /// </summary>
    public void Set(string studentAnonId, string instituteId)
        => _map[studentAnonId] = instituteId;

    /// <inheritdoc />
    public Task<string?> GetActiveInstituteAsync(string studentAnonId, CancellationToken ct = default)
    {
        _map.TryGetValue(studentAnonId, out var id);
        return Task.FromResult<string?>(id);
    }
}
