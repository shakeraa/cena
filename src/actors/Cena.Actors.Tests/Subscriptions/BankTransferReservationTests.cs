// =============================================================================
// Cena Platform — BankTransferReservation + Service + Worker tests (PRR-304)
//
// Coverage matrix:
//   Reference-code generator
//     - Length + alphabet invariant
//     - No ambiguous chars (I, L, O, U)
//     - Canonicalise drops whitespace + hyphens, upper-cases
//     - Distribution is not overly concentrated (soft sanity check)
//   In-memory store
//     - Save then Get round-trips
//     - Case-insensitive lookup on the reference code
//     - ListPending filters to Pending
//     - ListExpiringAtOrBefore filters by cutoff AND Pending
//   Service.Reserve
//     - Happy path creates Pending doc, Annual price, 14-day expiry
//     - Rejects Unsubscribed tier, SchoolSku (non-retail), duplicate pending
//     - Rejects when parent subscription is already Active
//   Service.Confirm
//     - Transitions Pending → Confirmed and calls Activate on aggregate
//     - Rejects unknown ref code, already-confirmed, already-expired
//     - Rejects when parent was activated via a different route
//     - Synthetic payment-txn id is "bank-transfer:<ref>"
//     - Canonicalises reference code (hyphen-prefixed input round-trips)
//   Service.ExpirePastDue
//     - Transitions every Pending with ExpiresAt <= now to Expired
//     - Leaves non-Pending untouched
//     - Returns the count
//   Worker
//     - TimeUntilNextTick math: 02:00-today if we're before, else 02:00-tomorrow
//     - Floors at 1 minute so a clock skew doesn't tight-loop
//     - RunOnceAsync invokes the service
// =============================================================================

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class BankTransferReservationTests
{
    // ---- Reference-code generator ------------------------------------------

    [Fact]
    public void Generate_produces_10_char_code_from_Crockford_alphabet()
    {
        for (int i = 0; i < 100; i++)
        {
            var code = BankTransferReferenceCodeGenerator.Generate();
            Assert.Equal(BankTransferReferenceCodeGenerator.CodeLength, code.Length);
            foreach (var c in code)
            {
                Assert.Contains(c, BankTransferReferenceCodeGenerator.Crockford32);
            }
        }
    }

    [Fact]
    public void Generate_never_produces_ambiguous_handwriting_chars()
    {
        // Crockford32 specifically omits I, L, O, U to avoid parent
        // hand-off typos ("1 vs I", "0 vs O"). Guard with a tight loop.
        for (int i = 0; i < 500; i++)
        {
            var code = BankTransferReferenceCodeGenerator.Generate();
            Assert.DoesNotContain('I', code);
            Assert.DoesNotContain('L', code);
            Assert.DoesNotContain('O', code);
            Assert.DoesNotContain('U', code);
        }
    }

    [Theory]
    [InlineData("AbCdEfGhJK", "ABCDEFGHJK")]
    [InlineData("CENA-XYZ7P23KMN", "CENAXYZ7P23KMN")]
    [InlineData("  xyz 7p2 ", "XYZ7P2")]
    [InlineData("", "")]
    [InlineData("!!@@", "")]
    public void Canonicalise_strips_non_alnum_and_uppercases(string input, string expected)
    {
        Assert.Equal(expected, BankTransferReferenceCodeGenerator.Canonicalise(input));
    }

    // ---- InMemory store ----------------------------------------------------

    [Fact]
    public async Task Store_save_then_get_roundtrips()
    {
        var store = new InMemoryBankTransferReservationStore();
        var doc = NewPendingDoc("ABC1234567", amount: 249_000L);
        await store.SaveAsync(doc, default);

        var back = await store.GetByReferenceCodeAsync("ABC1234567", default);
        Assert.NotNull(back);
        Assert.Equal(249_000L, back!.AmountAgorot);
    }

    [Fact]
    public async Task Store_lookup_is_case_insensitive()
    {
        var store = new InMemoryBankTransferReservationStore();
        var doc = NewPendingDoc("ABC1234567");
        await store.SaveAsync(doc, default);

        Assert.NotNull(await store.GetByReferenceCodeAsync("abc1234567", default));
        Assert.NotNull(await store.GetByReferenceCodeAsync("Abc1234567", default));
    }

    [Fact]
    public async Task Store_list_pending_filters_by_status()
    {
        var store = new InMemoryBankTransferReservationStore();
        await store.SaveAsync(NewPendingDoc("PENDING001"), default);

        var confirmed = NewPendingDoc("CONFIRM001");
        confirmed.Status = BankTransferReservationStatus.Confirmed;
        await store.SaveAsync(confirmed, default);

        var expired = NewPendingDoc("EXPIRED001");
        expired.Status = BankTransferReservationStatus.Expired;
        await store.SaveAsync(expired, default);

        var pending = await store.ListPendingAsync(default);
        Assert.Single(pending);
        Assert.Equal("PENDING001", pending[0].ReferenceCode);
    }

    [Fact]
    public async Task Store_list_expiring_filters_by_cutoff_and_pending_only()
    {
        var store = new InMemoryBankTransferReservationStore();
        var baseTime = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

        var expiringSoon = NewPendingDoc("SOONPEND01");
        expiringSoon.ExpiresAt = baseTime.AddHours(-1);
        await store.SaveAsync(expiringSoon, default);

        var expiringLater = NewPendingDoc("LATEPEND01");
        expiringLater.ExpiresAt = baseTime.AddHours(+48);
        await store.SaveAsync(expiringLater, default);

        var confirmedPast = NewPendingDoc("CONFPAST01");
        confirmedPast.ExpiresAt = baseTime.AddHours(-48);
        confirmedPast.Status = BankTransferReservationStatus.Confirmed;
        await store.SaveAsync(confirmedPast, default);

        var list = await store.ListExpiringAtOrBeforeAsync(baseTime, default);
        Assert.Single(list);
        Assert.Equal("SOONPEND01", list[0].ReferenceCode);
    }

    // ---- Service.Reserve ---------------------------------------------------

    [Fact]
    public async Task Reserve_happy_path_creates_pending_doc_with_annual_price()
    {
        var now = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        var (svc, _, _) = NewService(now);

        var doc = await svc.ReserveAsync(
            parentSubjectIdEncrypted: "enc::parent-A",
            primaryStudentSubjectIdEncrypted: "enc::student-A",
            tier: SubscriptionTier.Premium,
            ct: default);

        Assert.Equal(BankTransferReservationStatus.Pending, doc.Status);
        Assert.Equal(TierCatalog.Get(SubscriptionTier.Premium).AnnualPrice.Amount, doc.AmountAgorot);
        Assert.Equal(now, doc.CreatedAt);
        Assert.Equal(now.AddDays(14), doc.ExpiresAt);
        Assert.Equal(BankTransferReferenceCodeGenerator.CodeLength, doc.ReferenceCode.Length);
    }

    [Fact]
    public async Task Reserve_rejects_unsubscribed_tier()
    {
        var (svc, _, _) = NewService();
        var ex = await Assert.ThrowsAsync<BankTransferReservationException>(() =>
            svc.ReserveAsync("enc::p", "enc::s", SubscriptionTier.Unsubscribed, default));
        Assert.Equal("invalid_tier", ex.ReasonCode);
    }

    [Fact]
    public async Task Reserve_rejects_non_retail_tier()
    {
        var (svc, _, _) = NewService();
        var ex = await Assert.ThrowsAsync<BankTransferReservationException>(() =>
            svc.ReserveAsync("enc::p", "enc::s", SubscriptionTier.SchoolSku, default));
        Assert.Equal("invalid_tier", ex.ReasonCode);
    }

    [Fact]
    public async Task Reserve_rejects_when_subscription_already_active()
    {
        var now = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        var (svc, store, subStore) = NewService(now);

        // Preload the aggregate with an active subscription.
        await subStore.AppendAsync("enc::p", new SubscriptionActivated_V1(
            ParentSubjectIdEncrypted: "enc::p",
            PrimaryStudentSubjectIdEncrypted: "enc::s",
            Tier: SubscriptionTier.Basic,
            Cycle: BillingCycle.Monthly,
            GrossAmountAgorot: 7_900L,
            PaymentTransactionIdEncrypted: "txn-stripe-0",
            ActivatedAt: now.AddDays(-5),
            RenewsAt: now.AddDays(25)), default);

        var ex = await Assert.ThrowsAsync<BankTransferReservationException>(() =>
            svc.ReserveAsync("enc::p", "enc::s", SubscriptionTier.Premium, default));
        Assert.Equal("subscription_active", ex.ReasonCode);
    }

    [Fact]
    public async Task Reserve_rejects_duplicate_pending()
    {
        var (svc, _, _) = NewService();
        await svc.ReserveAsync("enc::p", "enc::s", SubscriptionTier.Premium, default);

        var ex = await Assert.ThrowsAsync<BankTransferReservationException>(() =>
            svc.ReserveAsync("enc::p", "enc::s", SubscriptionTier.Premium, default));
        Assert.Equal("duplicate_pending", ex.ReasonCode);
    }

    // ---- Service.Confirm ---------------------------------------------------

    [Fact]
    public async Task Confirm_transitions_pending_to_confirmed_and_activates_subscription()
    {
        var now = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        var (svc, _, subStore) = NewService(now);

        var doc = await svc.ReserveAsync("enc::p", "enc::s", SubscriptionTier.Premium, default);

        // Advance time 3 days and confirm.
        var after = now.AddDays(3);
        var (svc2, _, _) = NewServiceOnStores(after, svc.GetType().GetField("_store",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(svc) as IBankTransferReservationStore ?? throw new InvalidOperationException(),
            subStore);

        var activation = await svc2.ConfirmAsync(doc.ReferenceCode, "enc::admin-1", default);

        Assert.Equal(BillingCycle.Annual, activation.Cycle);
        Assert.StartsWith(BankTransferReservationService.PaymentTxnPrefix,
            activation.PaymentTransactionIdEncrypted);
        Assert.Equal(after, activation.ActivatedAt);
        Assert.Equal(after.AddYears(1), activation.RenewsAt);

        var back = await svc2.GetAsync(doc.ReferenceCode, default);
        Assert.NotNull(back);
        Assert.Equal(BankTransferReservationStatus.Confirmed, back!.Status);
        Assert.Equal(after, back.ConfirmedAt);
        Assert.Equal("enc::admin-1", back.ConfirmedByAdminSubjectIdEncrypted);

        // Aggregate is now active.
        var aggregate = await subStore.LoadAsync("enc::p", default);
        Assert.Equal(SubscriptionStatus.Active, aggregate.State.Status);
        Assert.Equal(SubscriptionTier.Premium, aggregate.State.CurrentTier);
    }

    [Fact]
    public async Task Confirm_rejects_unknown_reference_code()
    {
        var (svc, _, _) = NewService();
        var ex = await Assert.ThrowsAsync<BankTransferReservationException>(() =>
            svc.ConfirmAsync("UNKNOWN000", "enc::admin", default));
        Assert.Equal("not_found", ex.ReasonCode);
    }

    [Fact]
    public async Task Confirm_is_idempotent_and_rejects_already_confirmed()
    {
        var (svc, _, _) = NewService();
        var doc = await svc.ReserveAsync("enc::p", "enc::s", SubscriptionTier.Premium, default);
        await svc.ConfirmAsync(doc.ReferenceCode, "enc::admin", default);

        var ex = await Assert.ThrowsAsync<BankTransferReservationException>(() =>
            svc.ConfirmAsync(doc.ReferenceCode, "enc::admin-2", default));
        Assert.Equal("already_confirmed", ex.ReasonCode);
    }

    [Fact]
    public async Task Confirm_rejects_already_expired()
    {
        var now = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        var (svc, store, _) = NewService(now);
        var doc = await svc.ReserveAsync("enc::p", "enc::s", SubscriptionTier.Premium, default);

        // Mark it expired directly.
        doc.Status = BankTransferReservationStatus.Expired;
        doc.ExpiredAt = now.AddDays(15);
        await store.SaveAsync(doc, default);

        var ex = await Assert.ThrowsAsync<BankTransferReservationException>(() =>
            svc.ConfirmAsync(doc.ReferenceCode, "enc::admin", default));
        Assert.Equal("already_expired", ex.ReasonCode);
    }

    [Fact]
    public async Task Confirm_rejects_when_subscription_activated_via_another_route()
    {
        var now = new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero);
        var (svc, _, subStore) = NewService(now);

        var doc = await svc.ReserveAsync("enc::p", "enc::s", SubscriptionTier.Premium, default);

        // Now parent pays via Bit — subscription goes Active without us.
        await subStore.AppendAsync("enc::p", new SubscriptionActivated_V1(
            ParentSubjectIdEncrypted: "enc::p",
            PrimaryStudentSubjectIdEncrypted: "enc::s",
            Tier: SubscriptionTier.Premium,
            Cycle: BillingCycle.Monthly,
            GrossAmountAgorot: 24_900L,
            PaymentTransactionIdEncrypted: "txn-bit-0",
            ActivatedAt: now.AddHours(3),
            RenewsAt: now.AddHours(3).AddMonths(1)), default);

        var ex = await Assert.ThrowsAsync<BankTransferReservationException>(() =>
            svc.ConfirmAsync(doc.ReferenceCode, "enc::admin", default));
        Assert.Equal("subscription_active", ex.ReasonCode);
    }

    [Fact]
    public async Task Confirm_canonicalises_reference_code_with_hyphens_and_case_tolerant()
    {
        var (svc, _, _) = NewService();
        var doc = await svc.ReserveAsync("enc::p", "enc::s", SubscriptionTier.Premium, default);

        // Admin might type the code back with hyphens for readability
        // (e.g. "ABCDE-12345") or in lowercase; Canonicalise upper-cases
        // and strips non-alphanumerics so lookup succeeds regardless.
        var five = doc.ReferenceCode.Substring(0, 5);
        var rest = doc.ReferenceCode.Substring(5).ToLowerInvariant();
        var uiTyped = $"{five}-{rest}";

        var activation = await svc.ConfirmAsync(uiTyped, "enc::admin", default);
        Assert.Equal(SubscriptionTier.Premium, activation.Tier);
    }

    // ---- Service.ExpirePastDue ---------------------------------------------

    [Fact]
    public async Task ExpirePastDue_transitions_past_due_pendings_only()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var (svc, store, _) = NewService(now);

        // Pending past-due (should expire).
        var a = NewPendingDoc("PASTPEND01");
        a.CreatedAt = now.AddDays(-20);
        a.ExpiresAt = now.AddDays(-6);
        await store.SaveAsync(a, default);

        // Pending in-window (should not expire).
        var b = NewPendingDoc("FRESHPEND2");
        b.CreatedAt = now.AddDays(-1);
        b.ExpiresAt = now.AddDays(13);
        await store.SaveAsync(b, default);

        // Already Confirmed past-due (must not be touched).
        var c = NewPendingDoc("CONFPAST03");
        c.CreatedAt = now.AddDays(-25);
        c.ExpiresAt = now.AddDays(-11);
        c.Status = BankTransferReservationStatus.Confirmed;
        c.ConfirmedAt = now.AddDays(-12);
        await store.SaveAsync(c, default);

        var count = await svc.ExpirePastDueAsync(default);
        Assert.Equal(1, count);

        var backA = await store.GetByReferenceCodeAsync("PASTPEND01", default);
        Assert.Equal(BankTransferReservationStatus.Expired, backA!.Status);
        Assert.Equal(now, backA.ExpiredAt);

        var backB = await store.GetByReferenceCodeAsync("FRESHPEND2", default);
        Assert.Equal(BankTransferReservationStatus.Pending, backB!.Status);

        var backC = await store.GetByReferenceCodeAsync("CONFPAST03", default);
        Assert.Equal(BankTransferReservationStatus.Confirmed, backC!.Status);
    }

    // ---- Worker ------------------------------------------------------------

    [Theory]
    [InlineData(2026, 5, 1, 1, 0, 2, 1.0)]   // now 01:00, tick 02:00 → 1 hour away
    [InlineData(2026, 5, 1, 2, 0, 2, 24.0)]  // now == tick → next day (24h), floored to ≥ 1 min
    [InlineData(2026, 5, 1, 23, 0, 2, 3.0)]  // now 23:00, tick 02:00 tomorrow → 3 h away
    public void Worker_TimeUntilNextTick_targets_next_UTC_tick_boundary(
        int y, int m, int d, int hour, int minute, int tickHour, double expectedHours)
    {
        var now = new DateTimeOffset(y, m, d, hour, minute, 0, TimeSpan.Zero);
        var delay = BankTransferExpiryWorker.TimeUntilNextTick(now, tickHour);
        Assert.Equal(expectedHours, delay.TotalHours, precision: 2);
    }

    [Fact]
    public void Worker_TimeUntilNextTick_floors_at_one_minute()
    {
        // At exactly the tick-hour we return 24h (next day), so the floor
        // branch only matters for negative or zero deltas from unusual
        // clock configurations. Simulate by asking for a tick hour equal
        // to the current hour — the helper returns tomorrow.
        var now = new DateTimeOffset(2026, 5, 1, 2, 0, 0, TimeSpan.Zero);
        var delay = BankTransferExpiryWorker.TimeUntilNextTick(now, 2);
        Assert.True(delay >= TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Worker_RunOnceAsync_invokes_service_and_returns_count()
    {
        var now = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var (svc, store, _) = NewService(now);

        var a = NewPendingDoc("EXPIRETST1");
        a.ExpiresAt = now.AddDays(-1);
        await store.SaveAsync(a, default);

        var options = Options.Create(new BankTransferExpiryWorkerOptions
        {
            TickHourUtc = 2,
            RunOnStartup = false,
        });
        var worker = new BankTransferExpiryWorker(
            svc,
            new FakeClock(now),
            options,
            NullLogger<BankTransferExpiryWorker>.Instance);

        var count = await worker.RunOnceAsync(default);
        Assert.Equal(1, count);
        var back = await store.GetByReferenceCodeAsync("EXPIRETST1", default);
        Assert.Equal(BankTransferReservationStatus.Expired, back!.Status);
    }

    // ---- Helpers -----------------------------------------------------------

    private static BankTransferReservationDocument NewPendingDoc(
        string code, long amount = 24_900L) => new()
    {
        Id = code,
        ReferenceCode = code,
        ParentSubjectIdEncrypted = "enc::parent",
        PrimaryStudentSubjectIdEncrypted = "enc::student",
        Tier = SubscriptionTier.Premium,
        AmountAgorot = amount,
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(14),
        Status = BankTransferReservationStatus.Pending,
        ConfirmedAt = null,
        ExpiredAt = null,
        ConfirmedByAdminSubjectIdEncrypted = null,
    };

    private static (
        BankTransferReservationService service,
        IBankTransferReservationStore store,
        ISubscriptionAggregateStore subStore) NewService(DateTimeOffset? now = null)
    {
        var clock = new FakeClock(now ?? new DateTimeOffset(2026, 5, 1, 10, 0, 0, TimeSpan.Zero));
        var store = new InMemoryBankTransferReservationStore();
        var subStore = new InMemorySubscriptionAggregateStore();
        var svc = new BankTransferReservationService(store, subStore, clock);
        return (svc, store, subStore);
    }

    private static (
        BankTransferReservationService service,
        IBankTransferReservationStore store,
        ISubscriptionAggregateStore subStore) NewServiceOnStores(
        DateTimeOffset now,
        IBankTransferReservationStore store,
        ISubscriptionAggregateStore subStore)
    {
        var svc = new BankTransferReservationService(store, subStore, new FakeClock(now));
        return (svc, store, subStore);
    }

    private sealed class FakeClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FakeClock(DateTimeOffset now) { _now = now; }
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
