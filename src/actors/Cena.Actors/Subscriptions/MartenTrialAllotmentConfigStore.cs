// =============================================================================
// Cena Platform — MartenTrialAllotmentConfigStore (task t_b89826b8bd60)
//
// Production implementation. Persists:
//   1. The singleton document (TrialAllotmentConfig, id="current") — fast
//      hot-path read by trial-start.
//   2. An audit event (TrialAllotmentConfigChanged_V1) appended to a
//      dedicated `trial-allotment-config` stream — full history of every
//      super-admin change for compliance + retrospection.
//
// Singleton + audit-stream pattern matches AlphaMigrationSeedDocument
// (PRR-344) which sets the precedent. Reads do a single document load by
// id (cheap); writes do one Store + one stream append + one SaveChanges
// inside a single Marten session for atomicity.
// =============================================================================

using Cena.Actors.Subscriptions.Events;
using Marten;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Marten-backed singleton store. Uses lightweight sessions for both reads
/// and writes; the audit event is appended in the same session as the
/// document overwrite so a partial failure leaves no half-applied state.
/// </summary>
public sealed class MartenTrialAllotmentConfigStore : ITrialAllotmentConfigStore
{
    /// <summary>
    /// Marten event-stream key for the audit history. Singleton stream —
    /// every change appends here, never to a per-tenant stream.
    /// </summary>
    public const string AuditStreamKey = "trial-allotment-config";

    private readonly IDocumentStore _store;
    private readonly TimeProvider _clock;

    public MartenTrialAllotmentConfigStore(IDocumentStore store, TimeProvider? clock = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public async Task<TrialAllotmentConfig> GetAsync(CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var doc = await session.LoadAsync<TrialAllotmentConfig>(
            TrialAllotmentConfig.CurrentId, ct);
        return doc ?? TrialAllotmentConfig.DefaultZero();
    }

    /// <inheritdoc/>
    public async Task<TrialAllotmentConfig> UpdateAsync(
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

        await using var session = _store.LightweightSession();

        // Read previous state inside the same session so the audit event's
        // PreviousTrialEnabled flag is consistent with the overwrite below.
        var previous = await session.LoadAsync<TrialAllotmentConfig>(
            TrialAllotmentConfig.CurrentId, ct);
        var previousEnabled = previous?.TrialEnabled ?? false;

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

        // Document overwrite (Marten upserts on id match).
        session.Store(doc);

        // Audit event append — single singleton stream so the full history
        // of every super-admin change is in one place.
        var auditEvent = new TrialAllotmentConfigChanged_V1(
            ChangedAt: now,
            ChangedByAdminEncrypted: changedByAdminEncrypted,
            TrialDurationDays: trialDurationDays,
            TrialTutorTurns: trialTutorTurns,
            TrialPhotoDiagnostics: trialPhotoDiagnostics,
            TrialPracticeSessions: trialPracticeSessions,
            PreviousTrialEnabled: previousEnabled);
        session.Events.Append(AuditStreamKey, auditEvent);

        await session.SaveChangesAsync(ct);
        return doc;
    }

    /// <summary>
    /// Operational accessor: returns the full audit history for the admin
    /// status surface. Reads the singleton stream in append order.
    /// </summary>
    public async Task<IReadOnlyList<TrialAllotmentConfigChanged_V1>> ReadAuditHistoryAsync(
        CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var events = await session.Events.FetchStreamAsync(AuditStreamKey, token: ct);
        return events
            .Select(e => e.Data)
            .OfType<TrialAllotmentConfigChanged_V1>()
            .ToArray();
    }
}
