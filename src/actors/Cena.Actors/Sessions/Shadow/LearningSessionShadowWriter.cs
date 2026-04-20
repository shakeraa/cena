// =============================================================================
// Cena Platform — Default LearningSessionShadowWriter implementation
// EPIC-PRR-A Sprint 1 (ADR-0012 Schedule Lock)
//
// Phase 1 dual-write behaviour:
//   - When the env-var flag is OFF: no-op (instant rollback).
//   - When ON: opens a Marten LightweightSession, appends SessionStarted_V2
//     to `session-{sessionId}`, saves. Failures are logged and swallowed —
//     the student-stream write is the source of truth and must not be
//     broken by a shadow failure.
//
// Timeout pattern matches StudentActor.FlushEvents (RES-001): 2s hard cap
// on the SaveChanges call so a degraded Postgres cannot starve the caller.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Sessions.Events;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Sessions.Shadow;

/// <summary>
/// Default production implementation of
/// <see cref="ILearningSessionShadowWriter"/>. Writes to the
/// <c>session-{SessionId}</c> stream via Marten.
/// </summary>
public sealed class LearningSessionShadowWriter : ILearningSessionShadowWriter
{
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromMilliseconds(2000);

    private readonly IDocumentStore _store;
    private readonly ILogger<LearningSessionShadowWriter> _logger;

    public LearningSessionShadowWriter(
        IDocumentStore store,
        ILogger<LearningSessionShadowWriter> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task AppendSessionStartedAsync(
        SessionStarted_V1 v1Event,
        CancellationToken cancellationToken = default)
    {
        if (v1Event is null) throw new ArgumentNullException(nameof(v1Event));

        if (!LearningSessionShadowWriteFeatureFlag.IsEnabled())
        {
            // Flag off — do nothing. This is the default production state in
            // Phase 1 until ADR-0012 Sprint 2+ cutover flips it.
            return;
        }

        var streamKey = LearningSessionAggregate.StreamKey(v1Event.SessionId);
        var v2 = new SessionStarted_V2(
            StudentId: v1Event.StudentId,
            SessionId: v1Event.SessionId,
            DeviceType: v1Event.DeviceType,
            AppVersion: v1Event.AppVersion,
            Methodology: v1Event.Methodology,
            ExperimentCohort: v1Event.ExperimentCohort,
            IsOffline: v1Event.IsOffline,
            ClientTimestamp: v1Event.ClientTimestamp,
            SchoolId: v1Event.SchoolId);

        try
        {
            await using var session = _store.LightweightSession();
            session.Events.Append(streamKey, v2);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(WriteTimeout);

            await session.SaveChangesAsync(cts.Token);

            _logger.LogDebug(
                "Shadow-wrote SessionStarted_V2 to {StreamKey} for student {StudentId}",
                streamKey, v1Event.StudentId);
        }
        catch (OperationCanceledException)
        {
            // Timeout or caller cancellation. Primary write already succeeded;
            // do not propagate.
            _logger.LogWarning(
                "Shadow-write of SessionStarted_V2 timed out for {StreamKey}. " +
                "Primary V1 write is unaffected.",
                streamKey);
        }
        catch (Exception ex)
        {
            // Swallow all exceptions — shadow is best-effort.
            _logger.LogWarning(ex,
                "Shadow-write of SessionStarted_V2 failed for {StreamKey}. " +
                "Primary V1 write is unaffected.",
                streamKey);
        }
    }
}

/// <summary>
/// No-op shadow writer used as the safe default when
/// <see cref="ILearningSessionShadowWriter"/> is not injected (e.g. in unit
/// tests that predate Phase 1). Prevents the primary StudentActor path from
/// depending on a DI registration that might be missing.
/// </summary>
public sealed class NullLearningSessionShadowWriter : ILearningSessionShadowWriter
{
    /// <summary>Shared process-wide singleton.</summary>
    public static readonly NullLearningSessionShadowWriter Instance = new();

    private NullLearningSessionShadowWriter() { }

    /// <inheritdoc />
    public Task AppendSessionStartedAsync(
        SessionStarted_V1 v1Event,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
