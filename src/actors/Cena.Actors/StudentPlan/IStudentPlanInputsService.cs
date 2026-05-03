// =============================================================================
// Cena Platform — StudentPlan read-side service (prr-148)
//
// Thin read-side facade over IStudentPlanAggregateStore for the WRITE-side
// bounded context of the student's plan config (deadline + weekly budget).
//
// *** INTENTIONALLY DISTINCT from Cena.Actors.Sessions.IStudentPlanConfigService ***
// The Sessions namespace owns a scheduler-facing read contract (prr-149)
// that bundles MotivationProfile with deadline+budget and applies sensible
// fallbacks. This service is the UPSTREAM source the scheduler bridge
// reads FROM — it returns raw "what did the student set?" data with null
// fields when unset, no defaulting. prr-149 is responsible for the bridge
// that joins this + MotivationProfile (from RDY-057) into the scheduler's
// input VO. Keeping them apart means:
//
//   - This bounded context owns the write model; the Sessions bridge owns
//     the fallback policy.
//   - Changes to the fallback policy (e.g. "default weekly budget 5h →
//     10h") never touch this layer; changes to the persisted shape never
//     touch the scheduler's consumption point.
// =============================================================================

namespace Cena.Actors.StudentPlan;

/// <summary>
/// Read-side lookup for a student's current plan inputs (the raw values
/// the student has supplied, with null = unset). NOT the scheduler-facing
/// bundle — that belongs to prr-149 in Cena.Actors.Sessions.
/// </summary>
public interface IStudentPlanInputsService
{
    /// <summary>
    /// Return the student's current plan inputs (deadline + weekly budget).
    /// Fields are null when the student has not yet set that value.
    /// </summary>
    Task<StudentPlanConfig> GetAsync(string studentAnonId, CancellationToken ct = default);
}

/// <summary>
/// Default implementation. Delegates to the aggregate store and projects
/// the folded state into a <see cref="StudentPlanConfig"/>.
/// </summary>
public sealed class StudentPlanInputsService : IStudentPlanInputsService
{
    private readonly IStudentPlanAggregateStore _store;

    public StudentPlanInputsService(IStudentPlanAggregateStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<StudentPlanConfig> GetAsync(string studentAnonId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException("studentAnonId must be non-empty.", nameof(studentAnonId));
        }

        var aggregate = await _store.LoadAsync(studentAnonId, ct).ConfigureAwait(false);
        return aggregate.ToConfig(studentAnonId);
    }
}
