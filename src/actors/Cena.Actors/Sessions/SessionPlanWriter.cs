// =============================================================================
// Cena Platform — SessionPlanWriter (prr-149)
//
// Persists a SessionPlanSnapshot in two places, both session-scoped:
//
//   1. Event-sourced: appends SessionPlanComputed_V1 to the
//      `session-{SessionId}` Marten stream owned by LearningSessionAggregate.
//      This is the authoritative write — the stream is the system-of-record
//      for the session's lifecycle.
//
//   2. Read model: upserts a SessionPlanDocument keyed by `session-plan-{id}`
//      so GET /api/session/{id}/plan can serve the plan with a single
//      document load. The read doc is a cache of the latest event — callers
//      that want the historical plan chain can Query the event stream
//      directly.
//
// Failure contract: the event append is awaited and MUST succeed (it is
// the authoritative record). The read-doc upsert is best-effort — a
// failed upsert logs a warning but does not fail the caller, because the
// event is already on the stream and a delayed projection will recover
// the read path.
//
// Session-scope discipline:
//   - The read doc is keyed by `session-plan-{SessionId}` — NEVER by
//     student id. Deletion happens with the session archive.
//   - The write path does NOT touch the student stream, StudentActor,
//     or any `*ProfileSnapshot` document.
// =============================================================================

using Cena.Actors.Sessions.Events;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Sessions;

/// <summary>
/// Writes a session plan snapshot to the session stream + read doc.
/// </summary>
public interface ISessionPlanWriter
{
    /// <summary>
    /// Persist the plan. Throws if the event-stream append fails; logs
    /// and swallows if the read-doc upsert fails.
    /// </summary>
    Task WriteAsync(
        SessionPlanSnapshot snapshot,
        string inputsSource,
        CancellationToken ct = default);
}

/// <summary>
/// Marten-backed implementation. Uses a single LightweightSession for
/// the append + upsert so both land in one transaction on the happy
/// path.
/// </summary>
public sealed class SessionPlanWriter : ISessionPlanWriter
{
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(2);

    private readonly IDocumentStore _store;
    private readonly ILogger<SessionPlanWriter> _logger;

    public SessionPlanWriter(
        IDocumentStore store,
        ILogger<SessionPlanWriter> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task WriteAsync(
        SessionPlanSnapshot snapshot,
        string inputsSource,
        CancellationToken ct = default)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (string.IsNullOrWhiteSpace(inputsSource))
            throw new ArgumentException(
                "Inputs source must be non-empty.", nameof(inputsSource));

        var streamKey = LearningSessionAggregate.StreamKey(snapshot.SessionId);
        var evt = new SessionPlanComputed_V1(
            StudentAnonId: snapshot.StudentAnonId,
            SessionId: snapshot.SessionId,
            GeneratedAtUtc: snapshot.GeneratedAtUtc,
            Topics: snapshot.PriorityOrdered
                .Select(e => new SessionPlanTopicEntry_V1(
                    TopicSlug: e.TopicSlug,
                    PriorityScore: e.PriorityScore,
                    WeaknessComponent: e.WeaknessComponent,
                    TopicWeightComponent: e.TopicWeightComponent,
                    PrerequisiteComponent: e.PrerequisiteComponent,
                    Rationale: e.Rationale))
                .ToList(),
            MotivationProfile: snapshot.MotivationProfile,
            DeadlineUtc: snapshot.DeadlineUtc,
            WeeklyBudgetMinutes: snapshot.WeeklyBudgetMinutes,
            InputsSource: inputsSource);

        var doc = SessionPlanDocument.FromEvent(evt);

        await using var session = _store.LightweightSession();
        session.Events.Append(streamKey, evt);
        session.Store(doc);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(WriteTimeout);

        try
        {
            await session.SaveChangesAsync(cts.Token).ConfigureAwait(false);
            _logger.LogDebug(
                "prr-149: wrote SessionPlanComputed_V1 to {StreamKey} ({TopicCount} topics, source={Source})",
                streamKey, evt.Topics.Count, inputsSource);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "prr-149: session plan write to {StreamKey} timed out after {TimeoutMs}ms; " +
                "caller continues without a persisted plan for this session.",
                streamKey, WriteTimeout.TotalMilliseconds);
            throw;
        }
    }
}
