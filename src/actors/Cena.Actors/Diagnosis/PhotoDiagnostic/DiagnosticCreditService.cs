// =============================================================================
// Cena Platform — DiagnosticCreditService (EPIC-PRR-J PRR-391)
//
// Application-layer orchestration for "agent confirms dispute = real
// system error → one-click credit + auto-apology". Mirrors the RefundService
// shape (prr-306): a single entry point that threads the side-effects in
// a well-defined order, so either every step lands or the caller sees a
// typed exception and the DB stays consistent.
//
// Order of operations (intentional):
//   1. Validate inputs      — reject empty dispute id / out-of-range bump
//   2. Load the dispute     — 404 equivalent if the dispute id is garbage
//   3. Idempotency guard    — if the dispute is already Upheld, refuse;
//                             a second admin click must not issue a
//                             second credit row (the ledger IS the side-
//                             effect of saying "Upheld").
//   4. Flip dispute status  — via IDiagnosticDisputeService.ReviewAsync;
//                             reviewer note carries the credit summary
//                             so the admin audit surface (PRR-390) shows
//                             "credit issued: N uploads" without joining.
//   5. Write ledger row     — IssueAsync on the credit ledger. This is
//                             the step PhotoDiagnosticQuotaGate reads.
//   6. Dispatch apology     — best-effort via IDiagnosticCreditDispatcher.
//                             A dispatch failure does NOT roll back the
//                             credit — the credit is the student-facing
//                             remedy and must not hinge on email
//                             deliverability. Failure is logged (Null
//                             dispatcher returns NOT_CONFIGURED).
//
// Why not decrement IPhotoDiagnosticMonthlyUsage directly: see
// DiagnosticCreditLedgerDocument.cs banner — the counter is an append-
// only truth record of real upload attempts, and decrementing would
// corrupt metrics, audit samplers, and abuse-detection baselines.
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public interface IDiagnosticCreditService
{
    /// <summary>
    /// Confirm a dispute as a real system error and issue a credit.
    /// Idempotent against already-upheld disputes (throws so the caller
    /// sees a 409-equivalent; the endpoint translates). Returns the
    /// persisted ledger row.
    /// </summary>
    Task<DiagnosticCreditLedgerDocument> IssueCreditAsync(
        string disputeId,
        string adminSubjectId,
        int uploadQuotaBumpCount,
        string reason,
        CancellationToken ct);
}

public sealed class DiagnosticCreditService : IDiagnosticCreditService
{
    /// <summary>Minimum free-upload credit allowed (lower bound of the abuse window).</summary>
    public const int MinUploadQuotaBumpCount = 1;

    /// <summary>
    /// Maximum free-upload credit allowed per dispute. Tight upper bound
    /// so a misclick (or a compromised admin account) can't mint unlimited
    /// free usage. 50 is ~2.5x the Plus tier monthly cap (20); Premium's
    /// 100 soft-cap means a legitimate "full month grace" fits inside.
    /// </summary>
    public const int MaxUploadQuotaBumpCount = 50;

    /// <summary>Free-text reason bound (matches DiagnosticCreditLedgerDocument.Reason).</summary>
    public const int MaxReasonLength = 500;

    private readonly IDiagnosticDisputeService _disputes;
    private readonly IDiagnosticDisputeRepository _disputeRepo;
    private readonly IDiagnosticCreditLedger _ledger;
    private readonly IDiagnosticCreditDispatcher _dispatcher;
    private readonly TimeProvider _clock;
    private readonly ILogger<DiagnosticCreditService> _logger;

    public DiagnosticCreditService(
        IDiagnosticDisputeService disputes,
        IDiagnosticDisputeRepository disputeRepo,
        IDiagnosticCreditLedger ledger,
        IDiagnosticCreditDispatcher dispatcher,
        TimeProvider clock,
        ILogger<DiagnosticCreditService> logger)
    {
        _disputes = disputes ?? throw new ArgumentNullException(nameof(disputes));
        _disputeRepo = disputeRepo ?? throw new ArgumentNullException(nameof(disputeRepo));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DiagnosticCreditLedgerDocument> IssueCreditAsync(
        string disputeId,
        string adminSubjectId,
        int uploadQuotaBumpCount,
        string reason,
        CancellationToken ct)
    {
        // ---- 1. Input validation --------------------------------------
        if (string.IsNullOrWhiteSpace(disputeId))
            throw new ArgumentException("disputeId is required.", nameof(disputeId));
        if (string.IsNullOrWhiteSpace(adminSubjectId))
            throw new ArgumentException("adminSubjectId is required.", nameof(adminSubjectId));
        if (uploadQuotaBumpCount is < MinUploadQuotaBumpCount or > MaxUploadQuotaBumpCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(uploadQuotaBumpCount),
                uploadQuotaBumpCount,
                $"uploadQuotaBumpCount must be in [{MinUploadQuotaBumpCount}, {MaxUploadQuotaBumpCount}].");
        }
        var trimmedReason = (reason ?? string.Empty).Trim();
        if (trimmedReason.Length > MaxReasonLength)
        {
            throw new ArgumentException(
                $"Reason exceeds {MaxReasonLength} characters.", nameof(reason));
        }

        // ---- 2. Load the dispute --------------------------------------
        // We read from the repository directly (not IDiagnosticDisputeService.GetAsync)
        // so we see the raw document — the view would hide the student hash we
        // need to write onto the ledger row.
        var dispute = await _disputeRepo.GetAsync(disputeId, ct).ConfigureAwait(false);
        if (dispute is null)
            throw new InvalidOperationException($"Dispute {disputeId} not found.");

        // ---- 3. Idempotency guard -------------------------------------
        // An already-Upheld dispute means the credit has already been issued
        // on a prior click. Refusing here keeps the ledger 1:1 with disputes.
        if (dispute.Status == DisputeStatus.Upheld)
        {
            throw new InvalidOperationException(
                $"Dispute {disputeId} is already Upheld; credit was previously issued.");
        }

        // ---- 4. Flip dispute status -----------------------------------
        // ReviewAsync enforces "no transition back to New" and updates
        // ReviewedAt. The note embeds the credit summary so the admin
        // audit dashboard (PRR-390) shows context without a join.
        var reviewerNote = string.IsNullOrEmpty(trimmedReason)
            ? $"credit issued: {uploadQuotaBumpCount} uploads"
            : $"credit issued: {uploadQuotaBumpCount} uploads — {trimmedReason}";
        await _disputes.ReviewAsync(disputeId, DisputeStatus.Upheld, reviewerNote, ct)
            .ConfigureAwait(false);

        // ---- 5. Write ledger row --------------------------------------
        var ledgerRow = new DiagnosticCreditLedgerDocument
        {
            Id = Guid.NewGuid().ToString("D"),
            DisputeId = disputeId,
            StudentSubjectIdHash = dispute.StudentSubjectIdHash,
            CreditedBy = adminSubjectId,
            CreditKind = DiagnosticCreditKind.FreeUploadQuota,
            UploadQuotaBumpCount = uploadQuotaBumpCount,
            IssuedAtUtc = _clock.GetUtcNow(),
            Reason = trimmedReason,
        };
        await _ledger.IssueAsync(ledgerRow, ct).ConfigureAwait(false);

        // ---- 6. Dispatch apology (best-effort) ------------------------
        // Not rolled back on failure: credit-on-account > email deliverability.
        // The Null dispatcher returns NOT_CONFIGURED and we log + move on.
        try
        {
            var dispatch = await _dispatcher.DispatchAsync(
                new DiagnosticCreditDispatchRequest(ledgerRow, dispute.StudentSubjectIdHash),
                ct).ConfigureAwait(false);

            if (!dispatch.Delivered)
            {
                _logger.LogInformation(
                    "[prr-391] credit issued but apology not delivered "
                    + "creditId={CreditId} channel={Channel} error={ErrorCode}",
                    ledgerRow.Id, dispatch.Channel, dispatch.ErrorCode);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "[prr-391] credit issued but apology dispatch threw; credit stands. "
                + "creditId={CreditId}",
                ledgerRow.Id);
        }

        _logger.LogInformation(
            "[prr-391] diagnostic credit issued "
            + "creditId={CreditId} disputeId={DisputeId} student={StudentHash} "
            + "admin={Admin} kind={Kind} uploadBump={UploadBump}",
            ledgerRow.Id, disputeId, dispute.StudentSubjectIdHash,
            adminSubjectId, ledgerRow.CreditKind, uploadQuotaBumpCount);

        return ledgerRow;
    }
}
