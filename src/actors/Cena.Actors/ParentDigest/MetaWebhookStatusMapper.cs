// =============================================================================
// Cena Platform — MetaWebhookStatusMapper (PRR-437)
//
// Translates Meta's webhook-envelope status strings + error codes into
// internal outcomes the dispatcher / dead-letter store can act on.
// Pure lookup; no I/O; no state. The table below is the contract —
// every Meta status/code pair that the endpoint emits downstream goes
// through this one function so the routing logic cannot drift.
//
// Meta status / error reference (from the PRR-437 task body + Meta's
// own documentation):
//
//   status "sent"     + no error   -> Breadcrumb (no state change)
//   status "delivered" + no error  -> Delivered  (metric update)
//   status "read"     + no error   -> Read       (cadence-tuner signal)
//   status "failed"   + code 131047 -> DeadLetter "re-engagement_window_expired"
//   status "failed"   + code 131050 -> SenderQualityRed (pause channel 24h)
//   status "failed"   + code 132000..132999 -> DeadLetter "template_not_approved"
//                                              + mark template Paused
//   status "failed"   + any other   -> DeadLetter "meta-code:{code}"
//   unknown status                 -> Unknown (log + drop)
//
// The reason-code string on DeadLetter is stable + machine-readable so
// the admin ops queue can group by reason without string-match drift.
// Numeric Meta codes surface via the meta-code:{n} convention when they
// aren't in our named list.
// =============================================================================

namespace Cena.Actors.ParentDigest;

/// <summary>
/// What the ingest pipeline should do with an inbound Meta webhook status.
/// </summary>
public enum MetaWebhookActionKind
{
    /// <summary>Just record a breadcrumb / metric; no state mutation.</summary>
    Breadcrumb = 0,

    /// <summary>Transition correlation to Delivered.</summary>
    Delivered = 1,

    /// <summary>Transition correlation to Read (cadence-tuner input).</summary>
    Read = 2,

    /// <summary>Move to WhatsAppDeadLetter with <see cref="MetaWebhookActionDecision.ReasonCode"/>.</summary>
    DeadLetter = 3,

    /// <summary>
    /// Sender-quality Red: pause the WhatsApp channel for 24h in addition
    /// to writing a dead-letter row.
    /// </summary>
    SenderQualityRed = 4,

    /// <summary>
    /// Template failure (Meta code 132xxx) — pause the template in the
    /// catalog + write a dead-letter row.
    /// </summary>
    TemplateFailure = 5,

    /// <summary>Status string was not recognized; log + drop.</summary>
    Unknown = 6,
}

/// <summary>Outcome of the status mapping.</summary>
/// <param name="Action">What to do next.</param>
/// <param name="ReasonCode">
/// Stable machine-readable reason for the admin queue / dead-letter row.
/// Null for <see cref="MetaWebhookActionKind.Breadcrumb"/> /
/// <see cref="MetaWebhookActionKind.Delivered"/> /
/// <see cref="MetaWebhookActionKind.Read"/>.
/// </param>
/// <param name="MetaCode">The raw Meta error code, when one was present.</param>
public sealed record MetaWebhookActionDecision(
    MetaWebhookActionKind Action,
    string? ReasonCode,
    int? MetaCode);

/// <summary>Pure mapper. No DI, no state.</summary>
public static class MetaWebhookStatusMapper
{
    /// <summary>
    /// Map a Meta status string (as read from the envelope's
    /// <c>entry[].changes[].value.statuses[].status</c> field) plus an
    /// optional error code (from the same status's <c>errors[0].code</c>)
    /// to an internal action.
    /// </summary>
    /// <param name="status">
    /// Meta status string — case-insensitive match against
    /// {"sent","delivered","read","failed"}. Null / empty → Unknown.
    /// </param>
    /// <param name="metaErrorCode">
    /// Meta's numeric error code on the status row, or null when absent.
    /// </param>
    public static MetaWebhookActionDecision Map(string? status, int? metaErrorCode)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return new MetaWebhookActionDecision(
                MetaWebhookActionKind.Unknown, ReasonCode: null, MetaCode: metaErrorCode);
        }

        switch (status.Trim().ToLowerInvariant())
        {
            case "sent":
                return new MetaWebhookActionDecision(
                    MetaWebhookActionKind.Breadcrumb, null, metaErrorCode);

            case "delivered":
                return new MetaWebhookActionDecision(
                    MetaWebhookActionKind.Delivered, null, metaErrorCode);

            case "read":
                return new MetaWebhookActionDecision(
                    MetaWebhookActionKind.Read, null, metaErrorCode);

            case "failed":
                return MapFailed(metaErrorCode);

            default:
                return new MetaWebhookActionDecision(
                    MetaWebhookActionKind.Unknown, null, metaErrorCode);
        }
    }

    private static MetaWebhookActionDecision MapFailed(int? metaErrorCode)
    {
        // Specific documented codes first. See the header banner for the
        // canonical list; the code → reason mapping must stay stable so
        // admin dashboards can group by ReasonCode across deploys.
        switch (metaErrorCode)
        {
            case 131047:
                return new MetaWebhookActionDecision(
                    MetaWebhookActionKind.DeadLetter,
                    ReasonCode: "re_engagement_window_expired",
                    MetaCode: metaErrorCode);

            case 131050:
                return new MetaWebhookActionDecision(
                    MetaWebhookActionKind.SenderQualityRed,
                    ReasonCode: "sender_quality_red",
                    MetaCode: metaErrorCode);
        }

        // Range match for template-approval failures (132000..132999).
        if (metaErrorCode is int code && code >= 132000 && code <= 132999)
        {
            return new MetaWebhookActionDecision(
                MetaWebhookActionKind.TemplateFailure,
                ReasonCode: "template_not_approved",
                MetaCode: metaErrorCode);
        }

        // Unknown failure code — dead-letter with the raw Meta code baked
        // into the reason so ops can still pivot on it.
        var reason = metaErrorCode is null
            ? "meta_failed_no_code"
            : $"meta-code:{metaErrorCode}";
        return new MetaWebhookActionDecision(
            MetaWebhookActionKind.DeadLetter,
            ReasonCode: reason,
            MetaCode: metaErrorCode);
    }
}
