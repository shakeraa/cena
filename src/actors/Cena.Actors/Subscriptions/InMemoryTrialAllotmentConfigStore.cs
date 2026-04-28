// =============================================================================
// Cena Platform — InMemoryTrialAllotmentConfigStore (task t_b89826b8bd60)
//
// Test/dev default. Holds the singleton document in a ConcurrentDictionary
// keyed by the literal "current" id (matching the Marten convention) so
// behavior parity with the production store is exact. The audit event log
// is exposed for tests to assert event emission.
// =============================================================================

using System.Collections.Concurrent;
using Cena.Actors.Subscriptions.Events;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// In-memory implementation. Thread-safe via ConcurrentDictionary; usable
/// across parallel test fixtures. The audit-event log is captured in a
/// thread-safe list and exposed via <see cref="GetAuditEvents"/> for test
/// assertions.
/// </summary>
public sealed class InMemoryTrialAllotmentConfigStore : ITrialAllotmentConfigStore
{
    private readonly ConcurrentDictionary<string, TrialAllotmentConfig> _docs = new();
    private readonly List<TrialAllotmentConfigChanged_V1> _events = new();
    private readonly object _eventsLock = new();
    private readonly TimeProvider _clock;

    public InMemoryTrialAllotmentConfigStore(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public Task<TrialAllotmentConfig> GetAsync(CancellationToken ct)
    {
        var doc = _docs.GetOrAdd(
            TrialAllotmentConfig.CurrentId,
            _ => TrialAllotmentConfig.DefaultZero());
        // Return a defensive copy so callers cannot mutate the stored row.
        return Task.FromResult(Clone(doc));
    }

    /// <inheritdoc/>
    public Task<TrialAllotmentConfig> UpdateAsync(
        int trialDurationDays,
        int trialTutorTurns,
        int trialPhotoDiagnostics,
        int trialPracticeSessions,
        string changedByAdminEncrypted,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(changedByAdminEncrypted))
        {
            throw new ArgumentException(
                "changedByAdminEncrypted is required for audit trail.",
                nameof(changedByAdminEncrypted));
        }

        var validation = TrialAllotmentValidator.Validate(
            trialDurationDays,
            trialTutorTurns,
            trialPhotoDiagnostics,
            trialPracticeSessions);
        if (!validation.IsValid)
        {
            throw new TrialAllotmentValidationException(
                validation.FailedField!,
                validation.Reason!);
        }

        // Capture previous-enabled flag BEFORE overwrite so the audit event
        // can carry a clean transition signal.
        var previous = _docs.GetOrAdd(
            TrialAllotmentConfig.CurrentId,
            _ => TrialAllotmentConfig.DefaultZero());
        var previousEnabled = previous.TrialEnabled;

        var now = _clock.GetUtcNow();
        var doc = new TrialAllotmentConfig
        {
            Id = TrialAllotmentConfig.CurrentId,
            TrialDurationDays = trialDurationDays,
            TrialTutorTurns = trialTutorTurns,
            TrialPhotoDiagnostics = trialPhotoDiagnostics,
            TrialPracticeSessions = trialPracticeSessions,
            LastUpdatedByAdminEncrypted = changedByAdminEncrypted,
            LastUpdatedAtUtc = now,
        };
        _docs[TrialAllotmentConfig.CurrentId] = doc;

        var auditEvent = new TrialAllotmentConfigChanged_V1(
            ChangedAt: now,
            ChangedByAdminEncrypted: changedByAdminEncrypted,
            TrialDurationDays: trialDurationDays,
            TrialTutorTurns: trialTutorTurns,
            TrialPhotoDiagnostics: trialPhotoDiagnostics,
            TrialPracticeSessions: trialPracticeSessions,
            PreviousTrialEnabled: previousEnabled);
        lock (_eventsLock)
        {
            _events.Add(auditEvent);
        }

        return Task.FromResult(Clone(doc));
    }

    /// <summary>
    /// Test-only accessor. Returns the audit-event log in append order.
    /// </summary>
    public IReadOnlyList<TrialAllotmentConfigChanged_V1> GetAuditEvents()
    {
        lock (_eventsLock)
        {
            return _events.ToArray();
        }
    }

    private static TrialAllotmentConfig Clone(TrialAllotmentConfig source) => new()
    {
        Id = source.Id,
        TrialDurationDays = source.TrialDurationDays,
        TrialTutorTurns = source.TrialTutorTurns,
        TrialPhotoDiagnostics = source.TrialPhotoDiagnostics,
        TrialPracticeSessions = source.TrialPracticeSessions,
        LastUpdatedByAdminEncrypted = source.LastUpdatedByAdminEncrypted,
        LastUpdatedAtUtc = source.LastUpdatedAtUtc,
    };
}
