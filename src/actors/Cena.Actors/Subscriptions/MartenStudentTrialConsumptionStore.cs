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
    public async Task<CapIncrementResult> IncrementIfUnderCapAsync(
        string studentSubjectIdEncrypted,
        EntitlementFeature feature,
        int cap,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(studentSubjectIdEncrypted))
        {
            throw new ArgumentException(
                "Student subject id must be non-empty.", nameof(studentSubjectIdEncrypted));
        }

        // Atomic check-and-increment via a session-scoped Postgres advisory
        // lock keyed on a stable 64-bit hash of the student id. The lock is
        // acquired on a dedicated connection (NOT the Marten session's
        // connection) so we can hold it across the full load-check-modify-
        // save cycle without fighting Marten's transaction management.
        // Two concurrent callers on the same student serialise on the lock;
        // unrelated students never contend.
        var lockKey = AdvisoryLockKey(studentSubjectIdEncrypted);
        await using var lockConn = (Npgsql.NpgsqlConnection)_store
            .Storage.Database.CreateConnection();
        await lockConn.OpenAsync(ct).ConfigureAwait(false);
        try
        {
            await using (var lockCmd = lockConn.CreateCommand())
            {
                lockCmd.CommandText = "SELECT pg_advisory_lock(@k);";
                var p = lockCmd.CreateParameter();
                p.ParameterName = "k";
                p.Value = lockKey;
                lockCmd.Parameters.Add(p);
                await lockCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            // Lock held. Marten session uses its own connection / transaction
            // — that's fine because the lock serialises us against any other
            // caller doing the same operation on the same student id. Even
            // though the load-check-save reads through a different connection,
            // it's now sequenced behind any concurrent caller.
            await using var session = _store.LightweightSession();
            var doc = await session.LoadAsync<StudentTrialConsumptionDocument>(
                studentSubjectIdEncrypted, ct)
                ?? new StudentTrialConsumptionDocument { Id = studentSubjectIdEncrypted };

            if (cap > 0)
            {
                var current = feature switch
                {
                    EntitlementFeature.TutorTurn => doc.TutorTurnsUsed,
                    EntitlementFeature.PhotoDiagnostic => doc.PhotoDiagnosticsUsed,
                    EntitlementFeature.PracticeSession => doc.SessionsStarted,
                    EntitlementFeature.Generic => 0,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(feature), feature, "Unknown EntitlementFeature value."),
                };
                if (current >= cap)
                {
                    return new CapIncrementResult(
                        Allowed: false, Snapshot: doc.ToSnapshot());
                }
            }
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
            await session.SaveChangesAsync(ct).ConfigureAwait(false);
            return new CapIncrementResult(Allowed: true, Snapshot: doc.ToSnapshot());
        }
        finally
        {
            // Explicit release — pg_advisory_unlock is idempotent on a held
            // lock and a no-op on a not-held lock.
            try
            {
                await using var releaseCmd = lockConn.CreateCommand();
                releaseCmd.CommandText = "SELECT pg_advisory_unlock(@k);";
                var p = releaseCmd.CreateParameter();
                p.ParameterName = "k";
                p.Value = lockKey;
                releaseCmd.Parameters.Add(p);
                await releaseCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // Lock will release on connection dispose; swallow so the
                // primary path's exception (if any) is the one surfaced.
            }
        }
    }

    /// <summary>
    /// Stable 64-bit signed advisory-lock key derived from the student id.
    /// Postgres pg_advisory_xact_lock takes a bigint; we hash the id and
    /// reinterpret the first 8 bytes as a signed long.
    /// </summary>
    private static long AdvisoryLockKey(string studentSubjectIdEncrypted)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(studentSubjectIdEncrypted);
        var sha = System.Security.Cryptography.SHA256.HashData(bytes);
        return BitConverter.ToInt64(sha, 0);
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
