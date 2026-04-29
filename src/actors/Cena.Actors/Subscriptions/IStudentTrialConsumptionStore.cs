// =============================================================================
// Cena Platform — IStudentTrialConsumptionStore (Phase 1D, §5.5 cap counters)
//
// Per-student counter store backing the trial cap-hit decision in
// RequireEntitlementFilter and the §11.1 entitlement read surface.
//
// Counter lifecycle.
//   - IncrementAsync is called on the SUCCESS path of consumption sites
//     (tutor turn handler, photo handler, session start handler — Phase
//     1E wires those). Failed AI calls do NOT burn caps.
//   - ResetAsync is called on TrialStarted_V1 to clean up any stale state
//     left by an earlier admin-override re-trial path. Idempotent on a
//     never-seen student id (no-op).
//   - GetAsync is called by the read path (filter + entitlement endpoint)
//     and is the ONLY non-mutating operation in the contract.
//
// Thread safety contract.
//   IncrementAsync MUST be atomic per (studentId, feature) — concurrent
//   increments from two replicas / two requests must not lose a count.
//   InMemory uses a ConcurrentDictionary; Marten uses a single-doc
//   load-modify-save inside a serializable session.
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Read + write surface for per-student trial consumption counters.
/// Implementations MUST be safe under concurrent <see cref="IncrementAsync"/>
/// calls on the same key.
/// </summary>
public interface IStudentTrialConsumptionStore
{
    /// <summary>
    /// Return the current consumption snapshot for the student. Returns
    /// <see cref="StudentTrialConsumption.Empty"/> when no row exists yet
    /// (never null).
    /// </summary>
    Task<StudentTrialConsumption> GetAsync(
        string studentSubjectIdEncrypted,
        CancellationToken ct);

    /// <summary>
    /// Atomically increment the counter for <paramref name="feature"/> by 1.
    /// Adds <paramref name="now"/>'s UTC date to the active-days set if not
    /// already present. Returns the post-increment snapshot.
    /// </summary>
    Task<StudentTrialConsumption> IncrementAsync(
        string studentSubjectIdEncrypted,
        EntitlementFeature feature,
        DateTimeOffset now,
        CancellationToken ct);

    /// <summary>
    /// Reset the counters to all-zero. Idempotent. Used at trial-start so a
    /// re-trial (admin-override path, Phase 2+) gets a clean slate.
    /// </summary>
    Task ResetAsync(
        string studentSubjectIdEncrypted,
        CancellationToken ct);
}

/// <summary>
/// Strongly-typed feature identifier for cap accounting. Matches the four
/// knobs on <see cref="TrialAllotmentConfig"/>; <see cref="Generic"/> is a
/// non-counted entitlement check (used by the filter for endpoints that
/// require entitlement but don't burn a per-feature cap, e.g. read-only
/// dashboard endpoints).
/// </summary>
public enum EntitlementFeature
{
    /// <summary>Generic entitlement gate (no per-feature cap).</summary>
    Generic = 0,
    /// <summary>One tutor-turn consumption (LLM round-trip).</summary>
    TutorTurn = 1,
    /// <summary>One photo-diagnostic upload + OCR cycle.</summary>
    PhotoDiagnostic = 2,
    /// <summary>One practice-session start.</summary>
    PracticeSession = 3,
}
