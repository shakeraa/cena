// =============================================================================
// Cena Platform — HardCapSupportService (EPIC-PRR-J PRR-402)
//
// Application-layer orchestration for the hard-cap → contact-support flow.
// Mirrors DiagnosticCreditService and DiagnosticDisputeService in shape:
// one service, tight validation, structured metrics, and a typed-exception
// contract that HTTP adapters translate into status codes.
//
// Ledger-not-decrement rationale
// ------------------------------
// The hard cap is set by the catalog (Premium = 300/mo) and enforced by
// PhotoDiagnosticQuotaGate. When support grants an extension we do NOT
// reach back into IPhotoDiagnosticMonthlyUsage and subtract — that counter
// is the honest record of "how many uploads did this student actually make
// this month". Abuse detection, unit-economics, and the accuracy-audit
// sampler all depend on it staying monotonic. Instead the extension lands
// in this aggregate as an additive grant; the quota gate reads the sum and
// bumps the effective hard cap for the student's UTC month. Same pattern
// as PRR-391 DiagnosticCreditLedger, applied at a different layer of the
// cap enforcement.
//
// Why Premium-only
// ----------------
// The hard cap exists only on Premium (100 soft / 300 hard, per TierCatalog).
// Plus has soft = hard = 20; Basic has 0 (photo diagnostic off); Unsubscribed
// has 0; SchoolSku has 50/100. The hard-cap support-ticket affordance is
// specifically the "legitimate heavy use during an exam crunch" escape
// valve built for Premium customers — a Plus customer hitting 20/mo is
// expected to upgrade, not file a ticket (the upsell path is the healthy
// product response). Other tiers get ArgumentException here; the endpoint
// translates to 400.
//
// Ship-gate discipline
// --------------------
// Nothing this service writes contains scarcity or countdown copy. The
// student-typed reason passes through verbatim (stored, not rendered
// server-side into any notification), so their choice of words is their
// own. The API responses we return are error codes + ticket ids; the UI
// layer localizes the student-facing copy — exactly the same pattern as
// DiagnosticDisputeEndpoints.
// =============================================================================

using Cena.Actors.Subscriptions;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public interface IHardCapSupportService
{
    /// <summary>
    /// File a new support ticket for a Premium student at hard cap. Validates
    /// tier (Premium only), reason length (≤ 500), and rejects duplicates
    /// within the same month window. Returns the persisted ticket.
    /// </summary>
    Task<HardCapSupportTicketDocument> OpenTicketAsync(
        string studentSubjectIdHash,
        string parentSubjectIdEncrypted,
        SubscriptionTier tier,
        int uploadCount,
        string monthWindow,
        string? reason,
        CancellationToken ct);

    /// <summary>
    /// Support agent grants a one-time month-end extension. Validates the
    /// ticket is Open and the extension count is in
    /// <c>[MinGrantCount..MaxGrantCount]</c>; writes Resolved state; emits
    /// a metric for ops dashboards. Returns the updated ticket.
    /// </summary>
    Task<HardCapSupportTicketDocument> ResolveWithExtensionAsync(
        string ticketId,
        string adminSubjectId,
        int extensionCount,
        CancellationToken ct);

    /// <summary>
    /// Support agent declines the ticket (no extension granted). Validates
    /// Open state; writes Rejected state; emits a metric. Returns the
    /// updated ticket.
    /// </summary>
    Task<HardCapSupportTicketDocument> RejectAsync(
        string ticketId,
        string adminSubjectId,
        CancellationToken ct);
}

public sealed class HardCapSupportService : IHardCapSupportService
{
    /// <summary>Default grant when the admin UI chooses "one-time extension" without typing a number.</summary>
    public const int DefaultGrantCount = 50;

    /// <summary>
    /// Abuse lower bound on a single grant. Below this is likely a typo /
    /// misclick; the UI should never submit 0.
    /// </summary>
    public const int MinGrantCount = 1;

    /// <summary>
    /// Abuse upper bound on a single grant. 100 is chosen so a legitimate
    /// exam-week extension comfortably fits (Premium hard cap = 300, so
    /// +100 = 400 is a full 33 % bump) but a single compromised admin
    /// account can't mint unlimited free usage without being noticed on
    /// the metrics dashboard.
    /// </summary>
    public const int MaxGrantCount = 100;

    /// <summary>Max free-text reason bound (matches <see cref="HardCapSupportTicketDocument.Reason"/>).</summary>
    public const int MaxReasonLength = 500;

    private readonly IHardCapSupportTicketRepository _repo;
    private readonly PhotoDiagnosticMetrics _metrics;
    private readonly TimeProvider _clock;
    private readonly ILogger<HardCapSupportService> _logger;

    public HardCapSupportService(
        IHardCapSupportTicketRepository repo,
        PhotoDiagnosticMetrics metrics,
        TimeProvider clock,
        ILogger<HardCapSupportService> logger)
    {
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<HardCapSupportTicketDocument> OpenTicketAsync(
        string studentSubjectIdHash,
        string parentSubjectIdEncrypted,
        SubscriptionTier tier,
        int uploadCount,
        string monthWindow,
        string? reason,
        CancellationToken ct)
    {
        // ---- Input validation --------------------------------------------
        if (string.IsNullOrWhiteSpace(studentSubjectIdHash))
            throw new ArgumentException("studentSubjectIdHash is required.", nameof(studentSubjectIdHash));
        if (string.IsNullOrWhiteSpace(parentSubjectIdEncrypted))
            throw new ArgumentException("parentSubjectIdEncrypted is required.", nameof(parentSubjectIdEncrypted));
        if (tier != SubscriptionTier.Premium)
        {
            // Hard-cap ticket is a Premium-only affordance (see class banner).
            // The endpoint translates this to a 400 so the UI can map it to
            // an upsell / not-applicable copy.
            throw new ArgumentException(
                $"Hard-cap support tickets are available only for Premium tier (got {tier}).",
                nameof(tier));
        }
        if (uploadCount < 0)
            throw new ArgumentOutOfRangeException(nameof(uploadCount), uploadCount, "uploadCount must be ≥ 0.");
        if (string.IsNullOrWhiteSpace(monthWindow))
            throw new ArgumentException("monthWindow is required.", nameof(monthWindow));

        var trimmedReason = (reason ?? string.Empty).Trim();
        if (trimmedReason.Length > MaxReasonLength)
        {
            throw new ArgumentException(
                $"Reason exceeds {MaxReasonLength} characters.", nameof(reason));
        }

        // ---- Duplicate guard ---------------------------------------------
        // One open ticket per (student, month) is enough signal for support.
        // A second submission within the same month window is almost always
        // a student retrying the form, not a new legitimate need.
        if (await _repo.HasOpenTicketInMonthAsync(studentSubjectIdHash, monthWindow, ct)
                .ConfigureAwait(false))
        {
            throw new InvalidOperationException(
                $"An open hard-cap support ticket already exists for this student in {monthWindow}.");
        }

        // ---- Persist -----------------------------------------------------
        var ticket = new HardCapSupportTicketDocument
        {
            Id = Guid.NewGuid().ToString("D"),
            StudentSubjectIdHash = studentSubjectIdHash,
            ParentSubjectIdEncrypted = parentSubjectIdEncrypted,
            Tier = tier,
            UploadCountAtRequest = uploadCount,
            MonthlyWindow = monthWindow,
            RequestedAtUtc = _clock.GetUtcNow(),
            Reason = trimmedReason,
            Status = HardCapSupportTicketStatus.Open,
            GrantedExtensionCount = 0,
            GrantedBy = string.Empty,
            ResolvedAtUtc = null,
        };
        await _repo.OpenAsync(ticket, ct).ConfigureAwait(false);

        _metrics.RecordAuditSampled("hard_cap_support_ticket_opened");
        _logger.LogInformation(
            "[PRR-402] hard-cap support ticket opened "
            + "ticketId={TicketId} student={StudentHash} month={MonthWindow} uploadCount={UploadCount}",
            ticket.Id, studentSubjectIdHash, monthWindow, uploadCount);

        return ticket;
    }

    public async Task<HardCapSupportTicketDocument> ResolveWithExtensionAsync(
        string ticketId,
        string adminSubjectId,
        int extensionCount,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ticketId))
            throw new ArgumentException("ticketId is required.", nameof(ticketId));
        if (string.IsNullOrWhiteSpace(adminSubjectId))
            throw new ArgumentException("adminSubjectId is required.", nameof(adminSubjectId));
        if (extensionCount is < MinGrantCount or > MaxGrantCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(extensionCount),
                extensionCount,
                $"extensionCount must be in [{MinGrantCount}, {MaxGrantCount}].");
        }

        var ticket = await _repo.GetAsync(ticketId, ct).ConfigureAwait(false);
        if (ticket is null)
            throw new InvalidOperationException($"Hard-cap ticket {ticketId} not found.");
        if (ticket.Status != HardCapSupportTicketStatus.Open)
        {
            throw new InvalidOperationException(
                $"Hard-cap ticket {ticketId} is not Open (current: {ticket.Status}).");
        }

        var now = _clock.GetUtcNow();
        await _repo.ResolveAsync(ticketId, extensionCount, adminSubjectId, now, ct)
            .ConfigureAwait(false);

        _metrics.RecordAuditSampled("hard_cap_support_ticket_resolved");
        _logger.LogInformation(
            "[PRR-402] hard-cap support ticket resolved "
            + "ticketId={TicketId} admin={Admin} extension={Extension}",
            ticketId, adminSubjectId, extensionCount);

        return ticket with
        {
            Status = HardCapSupportTicketStatus.Resolved,
            GrantedExtensionCount = extensionCount,
            GrantedBy = adminSubjectId,
            ResolvedAtUtc = now,
        };
    }

    public async Task<HardCapSupportTicketDocument> RejectAsync(
        string ticketId,
        string adminSubjectId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ticketId))
            throw new ArgumentException("ticketId is required.", nameof(ticketId));
        if (string.IsNullOrWhiteSpace(adminSubjectId))
            throw new ArgumentException("adminSubjectId is required.", nameof(adminSubjectId));

        var ticket = await _repo.GetAsync(ticketId, ct).ConfigureAwait(false);
        if (ticket is null)
            throw new InvalidOperationException($"Hard-cap ticket {ticketId} not found.");
        if (ticket.Status != HardCapSupportTicketStatus.Open)
        {
            throw new InvalidOperationException(
                $"Hard-cap ticket {ticketId} is not Open (current: {ticket.Status}).");
        }

        var now = _clock.GetUtcNow();
        await _repo.RejectAsync(ticketId, adminSubjectId, now, ct).ConfigureAwait(false);

        _metrics.RecordAuditSampled("hard_cap_support_ticket_rejected");
        _logger.LogInformation(
            "[PRR-402] hard-cap support ticket rejected "
            + "ticketId={TicketId} admin={Admin}",
            ticketId, adminSubjectId);

        return ticket with
        {
            Status = HardCapSupportTicketStatus.Rejected,
            GrantedExtensionCount = 0,
            GrantedBy = adminSubjectId,
            ResolvedAtUtc = now,
        };
    }
}
