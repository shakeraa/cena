// =============================================================================
// Cena Platform — Marten-backed classroom roster lookup (PRR-236)
//
// Concrete implementation of IClassroomRosterLookup that rebuilds the
// active roster by replaying EnrollmentCreated_V1 + EnrollmentStatusChanged_V1
// for a specific classroom. Same pattern as TeacherConsole/HeatmapEndpoint's
// BuildRosterAsync — extracted here so the PRR-236 endpoint can share it
// without duplicating event-stream logic.
//
// Phase 1: event replay (low-volume per classroom). A projection-backed
// roster read model is a follow-up; the fan-out is small (tens of rows)
// so the projection is not on the critical path yet.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.StudentPlan;
using Marten;

namespace Cena.Admin.Api.Host.Endpoints;

/// <summary>
/// Rebuilds a classroom's active roster from the event stream on demand.
/// </summary>
public sealed class MartenClassroomRosterLookup : IClassroomRosterLookup
{
    private readonly IDocumentStore _store;

    public MartenClassroomRosterLookup(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetActiveRosterAsync(
        string instituteId,
        string classroomId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(classroomId)) return Array.Empty<string>();

        await using var session = _store.QuerySession();

        // 1. Creations for the classroom.
        var creations = await session.Events
            .QueryRawEventDataOnly<EnrollmentCreated_V1>()
            .Where(e => e.ClassroomId == classroomId)
            .ToListAsync(ct).ConfigureAwait(false);

        if (creations.Count == 0) return Array.Empty<string>();

        // 2. Status changes — EnrollmentStatusChanged_V1 carries no
        // ClassroomId, so we load the global set and filter by the
        // enrollment ids we just gathered. Phase 1: acceptable fan-out
        // given typical tenant size.
        var allStatus = await session.Events
            .QueryRawEventDataOnly<EnrollmentStatusChanged_V1>()
            .ToListAsync(ct).ConfigureAwait(false);

        var byEnrollment = new Dictionary<string, (string StudentId, bool Active)>(StringComparer.Ordinal);
        foreach (var c in creations)
            byEnrollment[c.EnrollmentId] = (c.StudentId, Active: true);

        foreach (var change in allStatus.OrderBy(c => c.ChangedAt))
        {
            if (!byEnrollment.TryGetValue(change.EnrollmentId, out var entry)) continue;
            var isActive = string.Equals(
                change.NewStatus, "Active", StringComparison.OrdinalIgnoreCase);
            byEnrollment[change.EnrollmentId] = (entry.StudentId, isActive);
        }

        return byEnrollment.Values
            .Where(v => v.Active)
            .Select(v => v.StudentId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
    }
}
