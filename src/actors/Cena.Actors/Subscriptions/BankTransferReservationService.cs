// =============================================================================
// Cena Platform — BankTransferReservationService (EPIC-PRR-I PRR-304)
//
// Orchestrates the three-step bank-transfer workflow:
//
//   1. ReserveAsync — parent picks bank-transfer at checkout. Service
//      generates a unique reference code, pins the amount from
//      TierCatalog, writes a Pending document that expires 14 days out.
//      Returns the document so the endpoint can render bank details +
//      reference code to the parent.
//
//   2. ConfirmAsync — finance admin marks payment received. Service
//      validates the reservation is still Pending (not already
//      confirmed, not expired), then calls SubscriptionCommands.Activate
//      against the parent's aggregate with synthetic
//      "bank-transfer:<referenceCode>" as the payment-txn id. Marks the
//      reservation Confirmed. Returns the activation event so the
//      endpoint can append + apply on the subscription aggregate store.
//
//   3. ExpirePastDueAsync — daily worker calls this. Any Pending
//      reservation with ExpiresAt ≤ now transitions to Expired. The
//      subscription aggregate is never touched for expiry — the
//      reservation simply dies and the parent can create a new one.
//
// The service is the single transactional boundary for the three
// operations. Endpoints and workers depend on the service, never on the
// raw store. The service depends on the store + TierCatalog + clock +
// ISubscriptionAggregateStore for Confirm's activation side-effect.
// =============================================================================

using Cena.Actors.Subscriptions.Events;

namespace Cena.Actors.Subscriptions;

/// <summary>Thrown when a bank-transfer workflow operation violates a precondition.</summary>
public sealed class BankTransferReservationException : Exception
{
    /// <summary>Stable machine-readable reason code for API surfacing.</summary>
    public string ReasonCode { get; }

    public BankTransferReservationException(string reasonCode, string message)
        : base(message)
    {
        ReasonCode = reasonCode;
    }
}

/// <summary>
/// Facade over <see cref="IBankTransferReservationStore"/> +
/// <see cref="ISubscriptionAggregateStore"/> that enforces the bank-transfer
/// workflow invariants. Endpoints and the expiry worker depend on this,
/// never on the raw store.
/// </summary>
public sealed class BankTransferReservationService
{
    /// <summary>Default reservation lifetime in days. Task scope: 14.</summary>
    public const int DefaultReservationDays = 14;

    /// <summary>Maximum reference-code collision retries before surfacing.</summary>
    public const int MaxCodeCollisionRetries = 5;

    /// <summary>
    /// Synthetic payment-transaction id prefix used when confirming a
    /// reservation into the subscription aggregate. Downstream readers
    /// (finance reporting, reconciliation) can tell a bank-transfer
    /// activation from a Stripe/Bit/PayBox one by this prefix without
    /// needing to join to the reservation store.
    /// </summary>
    public const string PaymentTxnPrefix = "bank-transfer:";

    private readonly IBankTransferReservationStore _store;
    private readonly ISubscriptionAggregateStore _subscriptionStore;
    private readonly TimeProvider _clock;

    public BankTransferReservationService(
        IBankTransferReservationStore store,
        ISubscriptionAggregateStore subscriptionStore,
        TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _subscriptionStore = subscriptionStore
            ?? throw new ArgumentNullException(nameof(subscriptionStore));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Create a Pending reservation for <paramref name="parentSubjectIdEncrypted"/>
    /// at <paramref name="tier"/>. Amount pinned from TierCatalog Annual
    /// price. Returns the persisted document (caller renders bank details
    /// + reference code on top of it).
    /// </summary>
    /// <exception cref="BankTransferReservationException">
    /// Thrown with ReasonCode:
    ///   "invalid_tier"           — not a retail tier / Unsubscribed
    ///   "subscription_active"    — parent already has an Active subscription
    ///   "duplicate_pending"      — parent already has a Pending reservation
    ///   "code_collision"         — reference-code collision retry exhausted (extreme bad luck)
    /// </exception>
    public async Task<BankTransferReservationDocument> ReserveAsync(
        string parentSubjectIdEncrypted,
        string primaryStudentSubjectIdEncrypted,
        SubscriptionTier tier,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(parentSubjectIdEncrypted))
        {
            throw new BankTransferReservationException(
                "invalid_parent", "ParentSubjectIdEncrypted is required.");
        }
        if (string.IsNullOrWhiteSpace(primaryStudentSubjectIdEncrypted))
        {
            throw new BankTransferReservationException(
                "invalid_student", "PrimaryStudentSubjectIdEncrypted is required.");
        }
        if (tier == SubscriptionTier.Unsubscribed)
        {
            throw new BankTransferReservationException(
                "invalid_tier", "Cannot reserve Unsubscribed tier.");
        }
        var def = TierCatalog.Get(tier);
        if (!def.IsRetail)
        {
            // SchoolSku has its own B2B invoice flow (PRR-340); bank-transfer
            // here is the retail-parent path only.
            throw new BankTransferReservationException(
                "invalid_tier", "Only retail tiers can be reserved via bank transfer.");
        }

        // Parent-level rules: don't reserve on top of an Active subscription,
        // and don't leave multiple Pending reservations around for one parent.
        var aggregate = await _subscriptionStore.LoadAsync(parentSubjectIdEncrypted, ct)
            .ConfigureAwait(false);
        if (aggregate.State.Status == SubscriptionStatus.Active)
        {
            throw new BankTransferReservationException(
                "subscription_active",
                "Subscription is already active; no new reservation needed.");
        }

        var pending = await _store.ListPendingAsync(ct).ConfigureAwait(false);
        var duplicate = pending.FirstOrDefault(
            p => p.ParentSubjectIdEncrypted == parentSubjectIdEncrypted);
        if (duplicate is not null)
        {
            throw new BankTransferReservationException(
                "duplicate_pending",
                $"Parent already has a pending reservation ({duplicate.ReferenceCode}).");
        }

        var now = _clock.GetUtcNow();
        var expiresAt = now.AddDays(DefaultReservationDays);
        var amount = def.AnnualPrice.Amount;

        // Generate with collision retry — extremely unlikely at 2^-50 per row
        // but correctness > optimism.
        for (int attempt = 0; attempt < MaxCodeCollisionRetries; attempt++)
        {
            var code = BankTransferReferenceCodeGenerator.Generate();
            var existing = await _store.GetByReferenceCodeAsync(code, ct).ConfigureAwait(false);
            if (existing is not null) continue;

            var doc = new BankTransferReservationDocument
            {
                Id = code,
                ReferenceCode = code,
                ParentSubjectIdEncrypted = parentSubjectIdEncrypted,
                PrimaryStudentSubjectIdEncrypted = primaryStudentSubjectIdEncrypted,
                Tier = tier,
                AmountAgorot = amount,
                CreatedAt = now,
                ExpiresAt = expiresAt,
                Status = BankTransferReservationStatus.Pending,
                ConfirmedAt = null,
                ExpiredAt = null,
                ConfirmedByAdminSubjectIdEncrypted = null,
            };
            await _store.SaveAsync(doc, ct).ConfigureAwait(false);
            return doc;
        }

        throw new BankTransferReservationException(
            "code_collision",
            $"Reference-code collision after {MaxCodeCollisionRetries} attempts; retry later.");
    }

    /// <summary>
    /// Admin confirms that payment was received against the given reference
    /// code. Transitions the reservation to Confirmed and appends a
    /// <c>SubscriptionActivated_V1</c> event on the parent's subscription
    /// aggregate. Returns the activation event so the endpoint can mirror
    /// the standard Activate-endpoint response.
    /// </summary>
    /// <exception cref="BankTransferReservationException">
    /// Thrown with ReasonCode:
    ///   "not_found"              — reference code unknown
    ///   "already_confirmed"      — reservation was already confirmed
    ///   "already_expired"        — reservation expired before admin confirmed
    ///   "subscription_active"    — parent activated via another route in the meantime
    /// </exception>
    public async Task<SubscriptionActivated_V1> ConfirmAsync(
        string referenceCode,
        string adminSubjectIdEncrypted,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(referenceCode))
        {
            throw new BankTransferReservationException(
                "invalid_reference", "ReferenceCode is required.");
        }
        if (string.IsNullOrWhiteSpace(adminSubjectIdEncrypted))
        {
            throw new BankTransferReservationException(
                "invalid_admin", "AdminSubjectIdEncrypted is required.");
        }

        var canonical = BankTransferReferenceCodeGenerator.Canonicalise(referenceCode);
        var doc = await _store.GetByReferenceCodeAsync(canonical, ct).ConfigureAwait(false);
        if (doc is null)
        {
            throw new BankTransferReservationException(
                "not_found", $"No reservation with reference code '{canonical}'.");
        }

        switch (doc.Status)
        {
            case BankTransferReservationStatus.Confirmed:
                throw new BankTransferReservationException(
                    "already_confirmed",
                    $"Reservation {canonical} was already confirmed at {doc.ConfirmedAt}.");
            case BankTransferReservationStatus.Expired:
                throw new BankTransferReservationException(
                    "already_expired",
                    $"Reservation {canonical} expired at {doc.ExpiredAt}; parent must create a new one.");
        }

        // Parent-state guard: another route may have activated the sub
        // between reservation and admin confirm (e.g., parent gave up
        // waiting and paid via Bit). If so, deny the confirm — finance
        // will need to refund the bank transfer manually.
        var aggregate = await _subscriptionStore.LoadAsync(doc.ParentSubjectIdEncrypted, ct)
            .ConfigureAwait(false);
        if (aggregate.State.Status == SubscriptionStatus.Active)
        {
            throw new BankTransferReservationException(
                "subscription_active",
                "Parent subscription was activated via another route; refund the bank transfer manually.");
        }

        var now = _clock.GetUtcNow();
        var paymentTxnId = $"{PaymentTxnPrefix}{canonical}";
        var activation = SubscriptionCommands.Activate(
            currentState: aggregate.State,
            parentSubjectIdEncrypted: doc.ParentSubjectIdEncrypted,
            primaryStudentSubjectIdEncrypted: doc.PrimaryStudentSubjectIdEncrypted,
            tier: doc.Tier,
            cycle: BillingCycle.Annual,
            paymentTransactionIdEncrypted: paymentTxnId,
            activatedAt: now);

        // Append + apply on the aggregate, then mark the reservation
        // confirmed. Order matters: if the aggregate append fails, we
        // MUST NOT mark the reservation confirmed — otherwise the
        // reservation appears resolved while no activation occurred.
        await _subscriptionStore.AppendAsync(doc.ParentSubjectIdEncrypted, activation, ct)
            .ConfigureAwait(false);
        aggregate.Apply(activation);

        doc.Status = BankTransferReservationStatus.Confirmed;
        doc.ConfirmedAt = now;
        doc.ConfirmedByAdminSubjectIdEncrypted = adminSubjectIdEncrypted;
        await _store.SaveAsync(doc, ct).ConfigureAwait(false);

        return activation;
    }

    /// <summary>
    /// Transition every Pending reservation with ExpiresAt ≤ now into
    /// Expired. Returns the count of reservations expired. The
    /// subscription aggregate is not touched — the parent simply loses
    /// their reservation. They can create a new one any time.
    /// </summary>
    public async Task<int> ExpirePastDueAsync(CancellationToken ct)
    {
        var now = _clock.GetUtcNow();
        var expiring = await _store.ListExpiringAtOrBeforeAsync(now, ct)
            .ConfigureAwait(false);
        if (expiring.Count == 0) return 0;

        int expired = 0;
        foreach (var doc in expiring)
        {
            if (doc.Status != BankTransferReservationStatus.Pending) continue;
            doc.Status = BankTransferReservationStatus.Expired;
            doc.ExpiredAt = now;
            await _store.SaveAsync(doc, ct).ConfigureAwait(false);
            expired++;
        }
        return expired;
    }

    /// <summary>
    /// Look up a reservation by reference code (canonicalised). Returns null
    /// if unknown. Used by the parent-side status endpoint and by admin
    /// inspection.
    /// </summary>
    public Task<BankTransferReservationDocument?> GetAsync(
        string referenceCode, CancellationToken ct)
    {
        var canonical = BankTransferReferenceCodeGenerator.Canonicalise(referenceCode ?? "");
        return _store.GetByReferenceCodeAsync(canonical, ct);
    }

    /// <summary>List every Pending reservation. Admin reconciliation dashboard.</summary>
    public Task<IReadOnlyList<BankTransferReservationDocument>> ListPendingAsync(
        CancellationToken ct) =>
        _store.ListPendingAsync(ct);
}
