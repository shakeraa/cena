// =============================================================================
// Cena Platform — IRetakeCohortReader (prr-238)
//
// Reads the "retake cohort" for an institute: the list of students who
// have at least one active ExamTarget carrying ReasonTag=Retake.
//
// Why a separate interface from IStudentPlanReader: the plan reader is
// per-student (needed by the scheduler hot path). The retake cohort read
// is admin-facing, institute-scoped, and low-frequency (finance dashboard,
// educator console). Keeping them separate lets the production Marten
// impl back each with the right index without coupling.
//
// Tenancy: the reader accepts an instituteId filter. ADR-0001 enforcement
// happens at the endpoint layer via TenantScope + institute_id claim —
// the reader does not itself consult the ClaimsPrincipal.
// =============================================================================

namespace Cena.Actors.StudentPlan;

/// <summary>One row per retake-cohort student.</summary>
/// <param name="StudentAnonId">Pseudonymous id for the student.</param>
/// <param name="InstituteId">Tenant the student belongs to.</param>
/// <param name="RetakeTargets">The student's active Retake-tagged targets.
/// Never empty — students with zero retake targets are excluded upstream.</param>
public sealed record RetakeCohortRow(
    string StudentAnonId,
    string InstituteId,
    IReadOnlyList<ExamTarget> RetakeTargets);

/// <summary>
/// Reads the retake-cohort membership for an institute.
/// </summary>
public interface IRetakeCohortReader
{
    /// <summary>
    /// List all students whose active plan contains at least one
    /// ExamTarget with <see cref="ReasonTag.Retake"/>. Never returns
    /// null; returns an empty list when no students match.
    /// </summary>
    Task<IReadOnlyList<RetakeCohortRow>> ListRetakeCohortAsync(
        string instituteId,
        CancellationToken ct = default);
}
