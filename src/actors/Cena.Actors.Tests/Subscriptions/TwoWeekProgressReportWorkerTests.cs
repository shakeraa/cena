// =============================================================================
// Cena Platform — TwoWeekProgressReportWorker tests (EPIC-PRR-I PRR-295)
//
// Covers the pure-function candidate-selection kernel so the worker's
// window + idempotency semantics are locked without a Marten container.
// The downstream IParentDigestDispatcher + Marten sent-marker are
// integration-tested end-to-end elsewhere.
// =============================================================================

using Cena.Actors.Subscriptions;
using Cena.Actors.Subscriptions.Events;
using Xunit;

namespace Cena.Actors.Tests.Subscriptions;

public class TwoWeekProgressReportWorkerTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 5, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Activation_exactly_14_days_old_is_a_candidate()
    {
        var activation = BuildActivation(
            parentId: "enc::parent::1",
            activatedAt: Now.AddDays(-14));

        var selected = TwoWeekProgressReportWorker.SelectCandidates(
            new[] { activation },
            new HashSet<string>(),
            Now).ToList();

        Assert.Single(selected);
        Assert.Equal("enc::parent::1", selected[0].ParentSubjectIdEncrypted);
    }

    [Fact]
    public void Activation_younger_than_14_days_is_skipped()
    {
        var activation = BuildActivation(
            parentId: "enc::parent::2",
            activatedAt: Now.AddDays(-7));

        var selected = TwoWeekProgressReportWorker.SelectCandidates(
            new[] { activation },
            new HashSet<string>(),
            Now).ToList();

        Assert.Empty(selected);
    }

    [Fact]
    public void Activation_older_than_21_days_is_skipped()
    {
        // Outside the retry window — the sent-marker should have caught
        // it by now, or we've missed our chance to send the two-week
        // report without being stale.
        var activation = BuildActivation(
            parentId: "enc::parent::3",
            activatedAt: Now.AddDays(-30));

        var selected = TwoWeekProgressReportWorker.SelectCandidates(
            new[] { activation },
            new HashSet<string>(),
            Now).ToList();

        Assert.Empty(selected);
    }

    [Fact]
    public void Already_sent_parents_are_filtered_out()
    {
        var activation = BuildActivation(
            parentId: "enc::parent::dup",
            activatedAt: Now.AddDays(-15));
        var sent = new HashSet<string>(StringComparer.Ordinal) { "enc::parent::dup" };

        var selected = TwoWeekProgressReportWorker.SelectCandidates(
            new[] { activation }, sent, Now).ToList();

        Assert.Empty(selected);
    }

    [Fact]
    public void Mixed_batch_only_yields_eligible_unsent_candidates()
    {
        // 4 activations: one too young (7d), one eligible (15d), one
        // already-sent (16d), one too old (30d). Only the 15d one
        // should come through.
        var activations = new[]
        {
            BuildActivation("enc::p::young", Now.AddDays(-7)),
            BuildActivation("enc::p::eligible", Now.AddDays(-15)),
            BuildActivation("enc::p::sent", Now.AddDays(-16)),
            BuildActivation("enc::p::old", Now.AddDays(-30)),
        };
        var sent = new HashSet<string>(StringComparer.Ordinal) { "enc::p::sent" };

        var selected = TwoWeekProgressReportWorker.SelectCandidates(
            activations, sent, Now).ToList();

        Assert.Single(selected);
        Assert.Equal("enc::p::eligible", selected[0].ParentSubjectIdEncrypted);
    }

    [Fact]
    public void Activation_in_retry_window_boundary_is_caught()
    {
        // Exactly at the lower bound (-21d) and exactly at the upper
        // bound (-14d) are both eligible — boundary test so a future
        // change to < vs <= is caught loudly.
        var boundary21 = BuildActivation("enc::p::21", Now.AddDays(-21));
        var boundary14 = BuildActivation("enc::p::14", Now.AddDays(-14));

        var selected = TwoWeekProgressReportWorker.SelectCandidates(
            new[] { boundary21, boundary14 },
            new HashSet<string>(),
            Now).ToList();

        Assert.Equal(2, selected.Count);
    }

    private static SubscriptionActivated_V1 BuildActivation(
        string parentId, DateTimeOffset activatedAt) =>
        new(
            ParentSubjectIdEncrypted: parentId,
            PrimaryStudentSubjectIdEncrypted: parentId + "::primary",
            Tier: SubscriptionTier.Premium,
            Cycle: BillingCycle.Monthly,
            GrossAmountAgorot: 24_900L,
            PaymentTransactionIdEncrypted: "txn_" + parentId,
            ActivatedAt: activatedAt,
            RenewsAt: activatedAt.AddDays(30));
}
