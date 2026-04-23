// =============================================================================
// Cena Platform — HardCapSupportTicketDocument (EPIC-PRR-J PRR-402)
//
// Marten-backed record for a legitimate heavy-use support ticket raised
// when a Premium-tier student hits the 300/mo hard cap. The ticket pre-
// populates the context a human support agent needs to triage the case
// (parent tier, month window, raw upload count at request-time, optional
// student-supplied reason) so the inbound queue isn't just "user says
// they need more uploads, no context".
//
// Why append-only + additive-grant, not counter-decrement
// -------------------------------------------------------
// The hard cap is the policy; the IPhotoDiagnosticMonthlyUsage counter is
// the evidence. When support legitimately grants a month-end extension,
// the honest answer is "we raised the cap for this student this month",
// not "we pretended the uploads never happened". So this aggregate — just
// like the PRR-391 DiagnosticCreditLedgerDocument — records an ADDITIVE
// grant. PhotoDiagnosticQuotaGate sums active grants for the student's
// UTC month window and bumps the effective hard cap up by that amount.
// The raw upload counter stays truthful, which is what abuse detection,
// audit sampling, and unit-economics analytics require.
//
// Privacy
// -------
//   - StudentSubjectIdHash : already hashed upstream (matches every other
//                            photo-diagnostic doc).
//   - ParentSubjectIdEncrypted : encrypted per ADR-0038. Support uses it
//                            to look up billing context; we don't store
//                            raw PII.
//   - Reason : optional free-text bounded at 500 chars. If the student
//              types scarcity/countdown copy ("I need it NOW for the exam")
//              the server still stores it — the ship-gate banned-terms
//              rule is about what WE render, not what students type.
//
// Lifecycle
// ---------
//   Open      → ticket submitted, awaiting support.
//   Resolved  → support granted an extension; GrantedExtensionCount > 0,
//               ResolvedAtUtc + GrantedBy stamped.
//   Rejected  → support reviewed and declined; GrantedExtensionCount = 0,
//               ResolvedAtUtc + GrantedBy stamped.
//
// Retention: tickets survive with the subscription lifecycle — they feed
// into per-tenant billing audit the same way credit-ledger rows do.
// =============================================================================

using Cena.Actors.Subscriptions;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>Lifecycle status of a hard-cap support ticket.</summary>
public enum HardCapSupportTicketStatus
{
    /// <summary>Ticket filed, awaiting support triage.</summary>
    Open = 0,

    /// <summary>Support granted a one-time month-end extension.</summary>
    Resolved = 1,

    /// <summary>Support reviewed and declined the extension.</summary>
    Rejected = 2,
}

/// <summary>
/// Marten document: one row per student-initiated hard-cap support ticket.
/// Primary key is a GUID assigned by HardCapSupportService.OpenTicketAsync
/// (not the student — there can legitimately be more than one ticket per
/// student across months, though the service rejects concurrent duplicates
/// within the same month window).
/// </summary>
public sealed record HardCapSupportTicketDocument
{
    /// <summary>Primary key (GUID, "D" format).</summary>
    public string Id { get; init; } = "";

    /// <summary>Hashed student subject id — matches other PhotoDiagnostic docs.</summary>
    public string StudentSubjectIdHash { get; init; } = "";

    /// <summary>Encrypted parent subject id (billing lookup on the support side).</summary>
    public string ParentSubjectIdEncrypted { get; init; } = "";

    /// <summary>Tier the student was on at cap-time.</summary>
    public SubscriptionTier Tier { get; init; }

    /// <summary>Raw upload counter at the moment the ticket was filed.</summary>
    public int UploadCountAtRequest { get; init; }

    /// <summary>Student's UTC calendar month in "YYYY-MM" form (see <see cref="MonthlyUsageKey"/>).</summary>
    public string MonthlyWindow { get; init; } = "";

    /// <summary>When the ticket was filed (UTC).</summary>
    public DateTimeOffset RequestedAtUtc { get; init; }

    /// <summary>
    /// Optional free-text reason the student typed. Bounded server-side at
    /// <see cref="HardCapSupportService.MaxReasonLength"/> chars; empty string
    /// if the student didn't supply one.
    /// </summary>
    public string Reason { get; init; } = "";

    /// <summary>Current lifecycle state. See <see cref="HardCapSupportTicketStatus"/>.</summary>
    public HardCapSupportTicketStatus Status { get; init; }

    /// <summary>When support resolved or rejected (UTC). Null while <c>Status == Open</c>.</summary>
    public DateTimeOffset? ResolvedAtUtc { get; init; }

    /// <summary>
    /// Admin subject id of the support agent who resolved / rejected the
    /// ticket. Not hashed — support agents are staff, not students. Empty
    /// while <c>Status == Open</c>.
    /// </summary>
    public string GrantedBy { get; init; } = "";

    /// <summary>
    /// Extension the agent granted this month. 0 on Open and Rejected tickets,
    /// <c>[MinGrantCount..MaxGrantCount]</c> on Resolved. Summed across active
    /// grants by <see cref="IHardCapSupportTicketRepository.ListActiveGrantsForStudentAsync"/>
    /// so PhotoDiagnosticQuotaGate can bump the effective hard cap.
    /// </summary>
    public int GrantedExtensionCount { get; init; }
}
