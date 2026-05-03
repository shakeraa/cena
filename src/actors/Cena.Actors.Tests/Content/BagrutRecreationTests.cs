// =============================================================================
// RDY-072 Phase 1A — BagrutRecreationAggregate workflow tests.
// =============================================================================

using Cena.Actors.Content;
using Xunit;

namespace Cena.Actors.Tests.Content;

public class BagrutRecreationAggregateTests
{
    private static BagrutRecreationAggregate New() => new(
        recreationId: "rec-1",
        reference: new MinistryReference(
            MinistryCode: "805",
            ExamYear: 2024,
            MoedSlug: "summer",
            QuestionNumber: "3a",
            TopicSlug: "derivatives"),
        fingerprint: new GenerationFingerprint(
            ModelId: "claude-opus-4-7",
            PromptTemplateHash: "hash",
            TemperatureE4: 7000,
            CandidateIndex: 0,
            GeneratedAtUtc: DateTimeOffset.UtcNow));

    [Fact]
    public void Starts_in_draft_state()
    {
        var r = New();
        Assert.Equal(RecreationReviewState.Draft, r.State);
        Assert.False(r.IsApprovedForProduction);
    }

    [Fact]
    public void Happy_path_draft_to_approved()
    {
        var r = New();
        r.Submit();
        Assert.Equal(RecreationReviewState.Submitted, r.State);
        r.RecordCasVerification(new CasVerificationResult(
            true, "cas-hash", "sympy-1.12", TimeSpan.FromMilliseconds(120)));
        Assert.Equal(RecreationReviewState.CasVerified, r.State);
        r.StartExpertReview();
        Assert.Equal(RecreationReviewState.ExpertReviewing, r.State);
        r.RecordExpertDecision(new ExpertReviewDecision(
            "amjad", true, "matches Ministry difficulty band.", DateTimeOffset.UtcNow));
        Assert.Equal(RecreationReviewState.Approved, r.State);
        Assert.True(r.IsApprovedForProduction);
    }

    [Fact]
    public void Cas_failure_rejects_the_candidate_outright()
    {
        var r = New();
        r.Submit();
        r.RecordCasVerification(new CasVerificationResult(
            false, "cas-hash", "sympy-1.12", TimeSpan.FromMilliseconds(90)));
        Assert.Equal(RecreationReviewState.Rejected, r.State);
        Assert.False(r.IsApprovedForProduction);
        Assert.Contains("CAS", r.RevisionReason);
    }

    [Fact]
    public void Expert_rejection_with_notes_goes_to_needs_revision()
    {
        var r = New();
        r.Submit();
        r.RecordCasVerification(new CasVerificationResult(
            true, "cas-hash", "sympy-1.12", TimeSpan.FromMilliseconds(120)));
        r.StartExpertReview();
        r.RecordExpertDecision(new ExpertReviewDecision(
            "amjad", false, "difficulty is too low.", DateTimeOffset.UtcNow));
        Assert.Equal(RecreationReviewState.NeedsRevision, r.State);
        Assert.Equal("difficulty is too low.", r.RevisionReason);
    }

    [Fact]
    public void Expert_rejection_without_notes_is_terminal_rejection()
    {
        var r = New();
        r.Submit();
        r.RecordCasVerification(new CasVerificationResult(
            true, "cas-hash", "sympy-1.12", TimeSpan.FromMilliseconds(120)));
        r.StartExpertReview();
        r.RecordExpertDecision(new ExpertReviewDecision(
            "amjad", false, "", DateTimeOffset.UtcNow));
        Assert.Equal(RecreationReviewState.Rejected, r.State);
    }

    [Fact]
    public void Cannot_skip_states()
    {
        var r = New();
        Assert.Throws<InvalidOperationException>(() => r.StartExpertReview());
        Assert.Throws<InvalidOperationException>(() =>
            r.RecordExpertDecision(new ExpertReviewDecision(
                "amjad", true, "", DateTimeOffset.UtcNow)));
        Assert.Throws<InvalidOperationException>(() =>
            r.RecordCasVerification(new CasVerificationResult(
                true, "h", "v", TimeSpan.Zero)));
    }

    [Fact]
    public void IsApproved_only_true_after_full_happy_path()
    {
        var r = New();
        r.Submit();
        r.RecordCasVerification(new CasVerificationResult(true, "h", "v", TimeSpan.Zero));
        Assert.False(r.IsApprovedForProduction); // CAS-verified is not enough
        r.StartExpertReview();
        Assert.False(r.IsApprovedForProduction); // under review is not enough
        r.RecordExpertDecision(new ExpertReviewDecision("amjad", true, "ok", DateTimeOffset.UtcNow));
        Assert.True(r.IsApprovedForProduction);
    }
}

public class RecreationDisclosureTests
{
    private static BagrutRecreationAggregate Approved()
    {
        var r = new BagrutRecreationAggregate(
            "rec-1",
            new MinistryReference("805", 2024, "summer", "3a", "derivatives"),
            new GenerationFingerprint("claude", "h", 7000, 0, DateTimeOffset.UtcNow));
        r.Submit();
        r.RecordCasVerification(new CasVerificationResult(true, "h", "v", TimeSpan.Zero));
        r.StartExpertReview();
        r.RecordExpertDecision(new ExpertReviewDecision("amjad", true, "ok", DateTimeOffset.UtcNow));
        return r;
    }

    [Fact]
    public void Public_attribution_never_claims_Ministry_publication()
    {
        var d = RecreationDisclosure.ForApproved(Approved());
        Assert.Contains("Inspired by", d.PublicAttribution);
        Assert.Contains("2024", d.PublicAttribution);
        Assert.Contains("summer", d.PublicAttribution);
        // Must NOT claim Ministry authorship / official publication.
        Assert.DoesNotContain("Ministry-published", d.PublicAttribution);
        Assert.DoesNotContain("official", d.PublicAttribution, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Internal_reference_carries_enough_for_audit()
    {
        var d = RecreationDisclosure.ForApproved(Approved());
        Assert.Contains("805", d.InternalReferenceNote);
        Assert.Contains("CAS-verified", d.InternalReferenceNote);
        Assert.Contains("expert-approved by amjad", d.InternalReferenceNote);
    }

    [Fact]
    public void Cannot_emit_disclosure_for_non_approved()
    {
        var r = new BagrutRecreationAggregate(
            "rec-1",
            new MinistryReference("805", 2024, "summer", "3a", "derivatives"),
            new GenerationFingerprint("claude", "h", 7000, 0, DateTimeOffset.UtcNow));
        Assert.Throws<InvalidOperationException>(() => RecreationDisclosure.ForApproved(r));
    }
}
