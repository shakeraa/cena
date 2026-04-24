// ═══════════════════════════════════════════════════════════════════════
// Cena Platform — Outreach Context Domain Events
// Layer: Domain Events | Runtime: .NET 9
// ═══════════════════════════════════════════════════════════════════════

namespace Cena.Actors.Events;

/// <summary>
/// Emitted when an outreach message is dispatched via a channel
/// (whatsapp, telegram, push, voice).
/// </summary>
public record OutreachMessageSent_V1(
    string StudentId,
    string MessageId,
    string Channel,
    string TriggerType,
    string ContentHash
);

/// <summary>
/// Emitted when delivery confirmation is received from the channel provider.
/// </summary>
public record OutreachMessageDelivered_V1(
    string StudentId,
    string MessageId,
    string Channel,
    DateTimeOffset DeliveredAt
);

/// <summary>
/// Emitted when the student responds to an outreach message
/// (quiz_answer, dismissed, clicked, replied).
/// </summary>
public record OutreachResponseReceived_V1(
    string StudentId,
    string MessageId,
    string ResponseType,
    string? ResponseContentHash
);
