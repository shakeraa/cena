// =============================================================================
// Cena Platform — MartenStudentTrialConsumptionStore (Phase 1D)
//
// Production multi-replica implementation. One Marten document per student
// (id = studentSubjectIdEncrypted). Increments are load-modify-save inside
// a fresh LightweightSession so the round-trip count is one per call. The
// document carries the active-dates set as a List<DateOnly> serialised by
// Marten's default JSON converter.
//
// Why not single-row "UPSERT counter += 1" SQL: keeping the seam plain
// Marten lets us swap to a different store later without bleeding raw SQL
// into the tests. The cap-counter contention is naturally low — one
// student's trial generates O(<200) increments across the whole trial
// window, and contention is per-student, not per-tenant.
// =============================================================================

using Marten;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Marten document persisted per student to back
/// <see cref="IStudentTrialConsumptionStore"/>. Marten serialises
/// <see cref="ActiveDates"/> as a JSON array of ISO-8601 date strings.
/// </summary>
public sealed class StudentTrialConsumptionDocument
{
    /// <summary>Marten identity = encrypted student subject id.</summary>
    public string Id { get; set; } = string.Empty;

    public int TutorTurnsUsed { get; set; }
    public int PhotoDiagnosticsUsed { get; set; }
    public int SessionsStarted { get; set; }

    /// <summary>
    /// Set of distinct UTC calendar dates the student was active. Backed
    /// by a List for Marten serialisation; on read we materialise into a
    /// HashSet for O(1) "did we already count this day?" checks.
    /// </summary>
    public List<DateOnly> ActiveDates { get; set; } = new();

    public DateTimeOffset LastUpdatedAt { get; set; }

    /// <summary>Project the document into the immutable snapshot type.</summary>
    public StudentTrialConsumption ToSnapshot() => new(
        TutorTurnsUsed: TutorTurnsUsed,
        PhotoDiagnosticsUsed: PhotoDiagnosticsUsed,
        SessionsStarted: SessionsStarted,
        DaysActive: ActiveDates.Count);
}

/// <summary>
/// Marten-backed implementation of <see cref="IStudentTrialConsumptionStore"/>.
/// Used in production where Actor-Host runs on multiple replicas.
/// </summary>
public sealed class MartenStudentTrialConsumptionStore : IStudentTrialConsumptionStore
{
    private readonly IDocumentStore _store;
    private readonly TimeProvider _clock;

    public MartenStudentTrialConsumptionStore(IDocumentStore store, TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc/>
    public async Task<StudentTrialConsumption> GetAsync(
        string studentSubjectIdEncrypted, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdEncrypted))
        {
            return StudentTrialConsumption.Empty;
        }
        await using var session = _store.QuerySession();
        var doc = await session.LoadAsync<StudentTrialConsumptionDocument>(
            studentSubjectIdEncrypted, ct);
        return doc?.ToSnapshot() ?? StudentTrialConsumption.Empty;
    }

    /// <inheritdoc/>
    public async Task<StudentTrialConsumption> IncrementAsync(
        string studentSubjectIdEncrypted,
        EntitlementFeature feature,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdEncrypted))
        {
            throw new ArgumentException(
                "Student subject id must be non-empty.", nameof(studentSubjectIdEncrypted));
        }

        await using var session = _store.LightweightSession();
        var doc = await session.LoadAsync<StudentTrialConsumptionDocument>(
            studentSubjectIdEncrypted, ct)
            ?? new StudentTrialConsumptionDocument { Id = studentSubjectIdEncrypted };

        switch (feature)
        {
            case EntitlementFeature.TutorTurn: doc.TutorTurnsUsed++; break;
            case EntitlementFeature.PhotoDiagnostic: doc.PhotoDiagnosticsUsed++; break;
            case EntitlementFeature.PracticeSession: doc.SessionsStarted++; break;
            case EntitlementFeature.Generic: break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(feature), feature, "Unknown EntitlementFeature value.");
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (!doc.ActiveDates.Contains(today))
        {
            doc.ActiveDates.Add(today);
        }
        doc.LastUpdatedAt = _clock.GetUtcNow();

        session.Store(doc);
        await session.SaveChangesAsync(ct);
        return doc.ToSnapshot();
    }

    /// <inheritdoc/>
    public async Task ResetAsync(string studentSubjectIdEncrypted, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdEncrypted))
        {
            return;
        }
        await using var session = _store.LightweightSession();
        var doc = await session.LoadAsync<StudentTrialConsumptionDocument>(
            studentSubjectIdEncrypted, ct);
        if (doc is null) return;

        doc.TutorTurnsUsed = 0;
        doc.PhotoDiagnosticsUsed = 0;
        doc.SessionsStarted = 0;
        doc.ActiveDates.Clear();
        doc.LastUpdatedAt = _clock.GetUtcNow();
        session.Store(doc);
        await session.SaveChangesAsync(ct);
    }
}
