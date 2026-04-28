// =============================================================================
// Cena Platform — ITrialAllotmentConfigStore (task t_b89826b8bd60)
//
// Narrow persistence seam for the platform-wide trial-allotment knobs.
// Two implementations:
//   - InMemoryTrialAllotmentConfigStore : single-process testing default
//   - MartenTrialAllotmentConfigStore   : production (singleton document
//                                         + audit-event stream)
//
// The store contract intentionally separates Get (cheap, hot-path read by
// trial-start) from Update (admin-only write that emits the audit event).
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Read + write surface for <see cref="TrialAllotmentConfig"/>. Implementations
/// MUST return <see cref="TrialAllotmentConfig.DefaultZero"/> when no row
/// exists yet (never null). The Update path MUST validate via
/// <see cref="TrialAllotmentValidator"/> and throw
/// <see cref="TrialAllotmentValidationException"/> on failure.
/// </summary>
public interface ITrialAllotmentConfigStore
{
    /// <summary>
    /// Load the current platform-wide trial-allotment configuration. Returns
    /// the zero-default document if no row has been written yet.
    /// </summary>
    Task<TrialAllotmentConfig> GetAsync(CancellationToken ct);

    /// <summary>
    /// Update the platform-wide trial-allotment configuration. Validates the
    /// proposed values, persists the singleton row, and appends a
    /// <c>TrialAllotmentConfigChanged_V1</c> event to the audit stream.
    /// Returns the persisted snapshot.
    /// </summary>
    /// <exception cref="TrialAllotmentValidationException">
    /// Thrown when any value is out of range. The exception's
    /// <see cref="TrialAllotmentValidationException.FailedField"/> and
    /// <see cref="TrialAllotmentValidationException.Reason"/> name the
    /// offending knob so the API layer can surface a precise 400.
    /// </exception>
    Task<TrialAllotmentConfig> UpdateAsync(
        int trialDurationDays,
        int trialTutorTurns,
        int trialPhotoDiagnostics,
        int trialPracticeSessions,
        string changedByAdminEncrypted,
        CancellationToken ct);
}

/// <summary>
/// Thrown by <see cref="ITrialAllotmentConfigStore.UpdateAsync"/> when any
/// proposed value fails range validation. Carries the failed field name and
/// human-readable reason for the API layer.
/// </summary>
public sealed class TrialAllotmentValidationException : Exception
{
    /// <summary>The name of the field that failed validation.</summary>
    public string FailedField { get; }

    /// <summary>Human-readable reason describing the failure.</summary>
    public string Reason { get; }

    public TrialAllotmentValidationException(string failedField, string reason)
        : base($"trial-allotment validation failed: {failedField} {reason}")
    {
        FailedField = failedField;
        Reason = reason;
    }
}
