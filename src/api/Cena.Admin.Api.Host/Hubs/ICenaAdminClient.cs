// =============================================================================
// Cena Platform — ICenaAdminClient (RDY-060)
//
// Strongly-typed SignalR client contract used by CenaAdminHub. Admin
// dashboards subscribe to `ReceiveEvent(subject, payload)` and dispatch
// client-side on the NATS subject. Keeping the contract narrow means a
// new admin surface (new NATS subject) doesn't need a server-side method
// per event — the client adds a subject handler and we're done.
// =============================================================================

namespace Cena.Admin.Api.Host.Hubs;

public interface ICenaAdminClient
{
    /// <summary>
    /// Push a NATS event envelope to the connected admin(s).
    /// Payload is the raw (already-unwrapped) event JSON; subject is the
    /// NATS subject exactly as published by the actor host.
    /// </summary>
    Task ReceiveEvent(AdminHubEnvelope envelope);
}

/// <summary>
/// Wire shape for admin hub events. Client code filters by subject
/// prefix (e.g. startsWith("cena.events.session.")) and parses the
/// payload as the specific event schema it expects.
/// </summary>
public sealed record AdminHubEnvelope(
    string Subject,
    string Group,
    string PayloadJson,
    DateTimeOffset ServerTimestamp);
