// =============================================================================
// Cena Platform — ISessionPlanNotifier (prr-149)
//
// Abstraction over the outbound notification path for a newly-computed
// session plan. Lets SessionEndpoints fire "plan updated" without
// referencing SignalR directly (kept in the API layer's DI graph). The
// null implementation is the safe default for tests and for the actor
// host where SignalR is not available.
// =============================================================================

namespace Cena.Actors.Sessions;

/// <summary>
/// Fires the "plan updated" notification to the student UI.
/// Implementations decide the transport (SignalR in the student API,
/// NATS event in the actor host, no-op in tests).
/// </summary>
public interface ISessionPlanNotifier
{
    /// <summary>
    /// Notify the session owner that a new plan is available. MUST NOT
    /// throw — a notifier failure is observability-only and must never
    /// break the session-start command.
    /// </summary>
    Task NotifyAsync(
        SessionPlanSnapshot snapshot,
        string inputsSource,
        CancellationToken ct = default);
}

/// <summary>
/// No-op notifier used when SignalR is not wired (tests, host without
/// hub). Safe singleton.
/// </summary>
public sealed class NullSessionPlanNotifier : ISessionPlanNotifier
{
    public static readonly NullSessionPlanNotifier Instance = new();
    private NullSessionPlanNotifier() { }

    public Task NotifyAsync(
        SessionPlanSnapshot snapshot,
        string inputsSource,
        CancellationToken ct = default) => Task.CompletedTask;
}
