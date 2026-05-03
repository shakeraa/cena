// =============================================================================
// Cena Platform — InMemoryRetakeCohortReader (prr-238)
//
// Phase-1 in-memory impl of IRetakeCohortReader. Hosts that wire the
// aggregate store with an in-memory backing (tests + pre-Marten dev) use
// this reader; Marten-backed hosts register a Marten-projection-backed
// reader instead.
//
// Storage shape: a delegate provider that yields (studentAnonId,
// instituteId) pairs, allowing the reader to enumerate all known students
// and then ask the aggregate store which ones carry Retake targets. This
// keeps the reader decoupled from the student-directory bounded context.
// =============================================================================

namespace Cena.Actors.StudentPlan;

/// <summary>
/// Minimal student-directory seam — yields (studentAnonId, instituteId)
/// pairs for every student known to the host. Hosts that have a real
/// student-directory projection implement this; tests use the in-memory
/// <see cref="StaticStudentDirectory"/>.
/// </summary>
public interface IStudentDirectory
{
    /// <summary>
    /// Enumerate every known (studentAnonId, instituteId) pair. Optional
    /// filter on institute id; when supplied, implementations should
    /// return only students in that institute.
    /// </summary>
    Task<IReadOnlyList<(string StudentAnonId, string InstituteId)>> ListStudentsAsync(
        string? instituteIdFilter = null,
        CancellationToken ct = default);
}

/// <summary>Static in-memory student directory — for tests.</summary>
public sealed class StaticStudentDirectory : IStudentDirectory
{
    private readonly IReadOnlyList<(string StudentAnonId, string InstituteId)> _rows;

    /// <summary>Construct from a fixed roster.</summary>
    public StaticStudentDirectory(IEnumerable<(string StudentAnonId, string InstituteId)> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        _rows = rows.ToArray();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<(string StudentAnonId, string InstituteId)>> ListStudentsAsync(
        string? instituteIdFilter = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(instituteIdFilter))
        {
            return Task.FromResult(_rows);
        }
        var filtered = _rows
            .Where(r => string.Equals(r.InstituteId, instituteIdFilter, StringComparison.Ordinal))
            .ToArray();
        return Task.FromResult<IReadOnlyList<(string, string)>>(filtered);
    }
}

/// <summary>
/// Default reader — composes a student directory with the per-student
/// plan reader to produce the retake cohort rollup.
/// </summary>
public sealed class InMemoryRetakeCohortReader : IRetakeCohortReader
{
    private readonly IStudentDirectory _directory;
    private readonly IStudentPlanReader _planReader;

    /// <summary>Wire via DI.</summary>
    public InMemoryRetakeCohortReader(
        IStudentDirectory directory,
        IStudentPlanReader planReader)
    {
        _directory = directory ?? throw new ArgumentNullException(nameof(directory));
        _planReader = planReader ?? throw new ArgumentNullException(nameof(planReader));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RetakeCohortRow>> ListRetakeCohortAsync(
        string instituteId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instituteId);

        var students = await _directory
            .ListStudentsAsync(instituteId, ct)
            .ConfigureAwait(false);

        var rows = new List<RetakeCohortRow>();
        foreach (var (studentAnonId, studentInstitute) in students)
        {
            var targets = await _planReader
                .ListTargetsAsync(studentAnonId, includeArchived: false, ct)
                .ConfigureAwait(false);

            var retakeOnly = targets
                .Where(t => t.ReasonTag == ReasonTag.Retake)
                .ToArray();

            if (retakeOnly.Length == 0) continue;

            rows.Add(new RetakeCohortRow(
                StudentAnonId: studentAnonId,
                InstituteId: studentInstitute,
                RetakeTargets: retakeOnly));
        }
        return rows;
    }
}
