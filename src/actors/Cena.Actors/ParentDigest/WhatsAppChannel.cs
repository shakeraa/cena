// =============================================================================
// Cena Platform — WhatsApp Digest Channel (RDY-069 Phase 1A)
//
// Builds on the RDY-067 parent digest aggregator + renderer. The
// domain here models the channel-preference + delivery-state surface
// without committing to a specific vendor (Twilio / 360dialog / Meta
// Cloud API) — the vendor adapter lands in Phase 1B.
//
// Iman's operational concerns (Round 4):
//   - Idempotent retries: every dispatch carries a correlation id
//     from the digest envelope; the vendor's "already delivered"
//     response is honoured not retried
//   - Dead-letter queue: invalid numbers go to WhatsAppDeadLetter
//     with a reason + admin-reviewable queue
//   - Quality-based sender reputation: tracked as
//     WhatsAppSenderQuality (green / yellow / red) from vendor webhook
//   - Per-template pre-approval: templates ship with a
//     PreApprovalStatus so we can never send a non-approved template
// =============================================================================

namespace Cena.Actors.ParentDigest;

/// <summary>
/// Channel preference for a parent. Parents choose independently
/// per child — a parent with two minors can opt email for one and
/// WhatsApp for the other.
/// </summary>
public sealed record ParentChannelPreference(
    string ParentAnonId,
    string MinorAnonId,
    bool EmailOptIn,
    bool WhatsAppOptIn,
    string? WhatsAppPhoneHmac,
    DateTimeOffset ConfiguredAtUtc)
{
    /// <summary>
    /// True when at least one channel is opted in. Parents who opted
    /// out of all channels receive nothing; the digest pipeline
    /// short-circuits.
    /// </summary>
    public bool HasAnyOptIn => EmailOptIn || WhatsAppOptIn;

    /// <summary>
    /// Safe WhatsApp delivery gate: opt-in AND phone number on file
    /// (the phone is HMAC'd so the record doesn't carry a plaintext
    /// number; the vendor adapter resolves the HMAC to the live
    /// number from the parent identity store).
    /// </summary>
    public bool CanDeliverWhatsApp =>
        WhatsAppOptIn && !string.IsNullOrWhiteSpace(WhatsAppPhoneHmac);
}

/// <summary>
/// WhatsApp message-template pre-approval state. Meta requires every
/// outbound business-initiated template to be registered + approved
/// before use — we CANNOT send a template with
/// <see cref="PreApprovalStatus.Pending"/> or
/// <see cref="PreApprovalStatus.Rejected"/>.
/// </summary>
public enum PreApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Paused = 3
}

public sealed record WhatsAppTemplate(
    string TemplateId,
    string Locale,
    PreApprovalStatus Status,
    string BodyHash)
{
    public bool CanSend => Status == PreApprovalStatus.Approved;
}

/// <summary>
/// Sender reputation as reported by the vendor. Drives circuit-
/// breaking: when reputation drops to Red, the digest pipeline
/// pauses WhatsApp delivery for that number and logs an ops alert;
/// email fallback kicks in automatically.
/// </summary>
public enum WhatsAppSenderQuality
{
    Unknown = 0,
    Green = 1,
    Yellow = 2,
    Red = 3
}

/// <summary>
/// Delivery attempt for idempotent retry. <see cref="CorrelationId"/>
/// is the digest envelope id; the vendor adapter uses it to
/// deduplicate.
/// </summary>
/// <param name="CorrelationId">Digest envelope id — vendor dedup key.</param>
/// <param name="ParentAnonId">Parent actor id (anon).</param>
/// <param name="MinorAnonId">Minor student id (anon).</param>
/// <param name="TemplateId">Pre-approved WhatsApp template id.</param>
/// <param name="Locale">Render locale.</param>
/// <param name="AttemptNumber">1-based attempt counter.</param>
/// <param name="AttemptedAtUtc">Wall clock of this attempt.</param>
/// <param name="InstituteId">
/// Optional tenant scope (ADR-0001). Required for prr-108 opt-out
/// preferences lookup; null/empty falls through to a conservative
/// "no preferences store consulted → treat as opted-in" ONLY when the
/// policy is intentionally unwired (test fixtures). Production DI
/// always threads this.
/// </param>
public sealed record WhatsAppDeliveryAttempt(
    string CorrelationId,
    string ParentAnonId,
    string MinorAnonId,
    string TemplateId,
    string Locale,
    int AttemptNumber,
    DateTimeOffset AttemptedAtUtc,
    string? InstituteId = null);

/// <summary>
/// Outcome of one delivery. Accepted means the vendor queued the
/// message; Duplicate means the correlation id was already seen;
/// InvalidRecipient moves to dead-letter; RateLimited requests a
/// backoff retry; VendorError is retriable up to the per-correlation
/// cap; OptedOut is the prr-108 short-circuit (parent has withdrawn
/// consent for WhatsApp on this child — terminal, no retry, no vendor
/// call).
/// </summary>
public enum WhatsAppDeliveryOutcome
{
    Accepted = 0,
    Duplicate = 1,
    InvalidRecipient = 2,
    RateLimited = 3,
    VendorError = 4,
    TemplateNotApproved = 5,
    SenderParked = 6,
    OptedOut = 7
}

/// <summary>
/// Abstraction over the vendor (Twilio / 360dialog / Meta Cloud).
/// Phase 1A ships the interface + <see cref="NullWhatsAppSender"/>
/// graceful-disabled default.
/// </summary>
public interface IWhatsAppSender
{
    string VendorId { get; }
    bool IsConfigured { get; }
    Task<WhatsAppDeliveryOutcome> SendAsync(
        WhatsAppDeliveryAttempt attempt,
        CancellationToken ct = default);
}

/// <summary>
/// Graceful-disabled default — the pipeline sees VendorError +
/// falls back to email (when parent has email opt-in). Admin /health
/// surfaces IsConfigured=false so ops notices the gap.
/// </summary>
public sealed class NullWhatsAppSender : IWhatsAppSender
{
    public string VendorId => "null";
    public bool IsConfigured => false;

    public Task<WhatsAppDeliveryOutcome> SendAsync(
        WhatsAppDeliveryAttempt attempt,
        CancellationToken ct = default)
        => Task.FromResult(WhatsAppDeliveryOutcome.VendorError);
}

/// <summary>
/// Dead-letter record for invalid recipients / permanently-failed
/// deliveries. The admin console renders the queue + lets ops fix
/// or purge rows.
/// </summary>
public sealed record WhatsAppDeadLetter(
    string CorrelationId,
    string ParentAnonId,
    string MinorAnonId,
    WhatsAppDeliveryOutcome FinalOutcome,
    string Reason,
    DateTimeOffset DeadLetteredAtUtc);

/// <summary>
/// Pure dispatcher decision — given (preference, template, sender
/// quality), decide whether to attempt WhatsApp delivery now, fall
/// back to email, or skip entirely. No side effects.
/// </summary>
public static class WhatsAppDispatcher
{
    public enum Decision
    {
        AttemptWhatsApp = 0,
        FallBackToEmail = 1,
        SkipAllChannels = 2
    }

    public static Decision Decide(
        ParentChannelPreference preference,
        WhatsAppTemplate template,
        WhatsAppSenderQuality senderQuality,
        bool emailAvailable)
    {
        ArgumentNullException.ThrowIfNull(preference);
        ArgumentNullException.ThrowIfNull(template);

        // Parent opted out of everything → skip.
        if (!preference.HasAnyOptIn) return Decision.SkipAllChannels;

        // WhatsApp path gated on: opt-in + approved template +
        // non-red sender. Any failure drops to email fallback when
        // email is opted in; else skip.
        var canAttemptWhatsApp =
            preference.CanDeliverWhatsApp
            && template.CanSend
            && senderQuality != WhatsAppSenderQuality.Red;

        if (canAttemptWhatsApp) return Decision.AttemptWhatsApp;
        if (preference.EmailOptIn && emailAvailable) return Decision.FallBackToEmail;
        return Decision.SkipAllChannels;
    }
}
