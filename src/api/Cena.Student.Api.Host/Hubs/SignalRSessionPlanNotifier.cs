// =============================================================================
// Cena Platform — SignalRSessionPlanNotifier (prr-149)
//
// IHubContext-backed ISessionPlanNotifier. Fires SessionPlanUpdated to
// the student's SignalR group without going through NATS — the plan is
// computed inline in the API, so there is no actor-host hop to bridge.
// Swallows all failures per the ISessionPlanNotifier contract (observability
// only).
// =============================================================================

using Cena.Actors.Sessions;
using Cena.Api.Contracts.Hub;
using Microsoft.AspNetCore.SignalR;

namespace Cena.Api.Host.Hubs;

/// <summary>
/// Pushes SessionPlanUpdated to the owning student's SignalR group.
/// </summary>
public sealed class SignalRSessionPlanNotifier : ISessionPlanNotifier
{
    private readonly IHubContext<CenaHub, ICenaClient> _hubContext;
    private readonly ILogger<SignalRSessionPlanNotifier> _logger;

    public SignalRSessionPlanNotifier(
        IHubContext<CenaHub, ICenaClient> hubContext,
        ILogger<SignalRSessionPlanNotifier> logger)
    {
        _hubContext = hubContext ?? throw new ArgumentNullException(nameof(hubContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task NotifyAsync(
        SessionPlanSnapshot snapshot,
        string inputsSource,
        CancellationToken ct = default)
    {
        if (snapshot is null) return;

        try
        {
            var evt = new SessionPlanUpdatedEvent(
                SessionId: snapshot.SessionId,
                StudentAnonId: snapshot.StudentAnonId,
                GeneratedAtUtc: snapshot.GeneratedAtUtc,
                TopicCount: snapshot.PriorityOrdered.Length,
                InputsSource: inputsSource,
                Timestamp: DateTimeOffset.UtcNow);

            // Student SignalR groups are keyed by student id (see
            // SignalRGroupManager + NatsSignalRBridge.RouteEvent above).
            await _hubContext
                .Clients
                .Group(snapshot.StudentAnonId)
                .SessionPlanUpdated(evt);
        }
        catch (Exception ex)
        {
            // Observability only — plan is already persisted, failing here
            // must not break session-start.
            _logger.LogWarning(ex,
                "prr-149: failed to push SessionPlanUpdated for session {SessionId}",
                snapshot.SessionId);
        }
    }
}
