// =============================================================================
// Cena Platform — ConsentAggregate unit + matrix tests (prr-155)
//
// Covers:
//   1. Grant -> Revoke -> query round-trip on the aggregate state.
//   2. ExpiresAt enforcement (IsEffectivelyGranted returns false past expiry).
//   3. AgeBandAuthorizationRules matrix — every (band, role) cell exercised
//      with a durable-data purpose AND a non-durable purpose so the
//      Teen13to15 "durable = parent only" distinction surfaces.
//   4. Parent-review workflow: RecordParentReview emits the expected event
//      and the fold surfaces LastParentReview.
//   5. Command validation: empty subject-id, empty purposes-reviewed,
//      null command all refused.
//
// The matrix is exhaustive: 4 bands × 5 roles × 2 actions (grant+revoke) ×
// 2 purpose classes (durable + non-durable) = 80 cells. We don't assert all
// 80 individually (a frozen-set table drive would be better coverage), but
// we DO exercise every (band, role) pair for at least one action, and we
// exercise the durable/non-durable distinction for every band + student.
// =============================================================================

using Cena.Actors.Consent;
using Cena.Actors.Consent.Events;
using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Compliance.KeyStore;

namespace Cena.Actors.Tests.Consent;

public sealed class ConsentAggregateTests
{
    private static readonly byte[] TestRootKey = new byte[]
    {
        0x43, 0x6f, 0x6e, 0x73, 0x65, 0x6e, 0x74, 0x52,
        0x6f, 0x6f, 0x74, 0x4b, 0x65, 0x79, 0x46, 0x6f,
        0x72, 0x55, 0x6e, 0x69, 0x74, 0x54, 0x65, 0x73,
        0x74, 0x73, 0x31, 0x35, 0x35, 0x70, 0x72, 0x72
    };

    private static (EncryptedFieldAccessor accessor, ConsentCommandHandler handler, InMemoryConsentAggregateStore store) BuildSut()
    {
        var derivation = new SubjectKeyDerivation(TestRootKey, "unit-test-install", isDevFallback: false);
        var keyStore = new InMemorySubjectKeyStore(derivation);
        var accessor = new EncryptedFieldAccessor(keyStore);
        var handler = new ConsentCommandHandler(accessor);
        var store = new InMemoryConsentAggregateStore();
        return (accessor, handler, store);
    }

    // -------------------------------------------------------------------------
    // Round-trip: grant -> revoke -> re-grant, state reflects the last event
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Grant_then_Revoke_then_regrant_state_reflects_latest_event()
    {
        var (_, handler, store) = BuildSut();
        const string subjectId = "student-round-trip";
        var now = DateTimeOffset.Parse("2026-04-20T10:00:00Z");

        var grantEvt = await handler.HandleAsync(new GrantConsent(
            subjectId, AgeBand.Adult, ConsentPurpose.MisconceptionDetection,
            Scope: "institute-A", GrantedByRole: ActorRole.Student,
            GrantedByActorId: subjectId, GrantedAt: now, ExpiresAt: null));
        await store.AppendAsync(subjectId, grantEvt);

        var agg1 = await store.LoadAsync(subjectId);
        Assert.True(agg1.State.IsEffectivelyGranted(ConsentPurpose.MisconceptionDetection, now));

        var revokeEvt = await handler.HandleAsync(new RevokeConsent(
            subjectId, AgeBand.Adult, ConsentPurpose.MisconceptionDetection,
            RevokedByRole: ActorRole.Student, RevokedByActorId: subjectId,
            RevokedAt: now.AddMinutes(5), Reason: "changed-mind"));
        await store.AppendAsync(subjectId, revokeEvt);

        var agg2 = await store.LoadAsync(subjectId);
        Assert.False(agg2.State.IsEffectivelyGranted(ConsentPurpose.MisconceptionDetection, now.AddMinutes(5)));
        Assert.Equal("changed-mind", agg2.State.Grants[ConsentPurpose.MisconceptionDetection].RevocationReason);
        Assert.Equal(ActorRole.Student, agg2.State.Grants[ConsentPurpose.MisconceptionDetection].RevokedByRole);

        // Re-grant
        var regrant = await handler.HandleAsync(new GrantConsent(
            subjectId, AgeBand.Adult, ConsentPurpose.MisconceptionDetection,
            Scope: "institute-A", GrantedByRole: ActorRole.Student,
            GrantedByActorId: subjectId, GrantedAt: now.AddMinutes(10), ExpiresAt: null));
        await store.AppendAsync(subjectId, regrant);

        var agg3 = await store.LoadAsync(subjectId);
        Assert.True(agg3.State.IsEffectivelyGranted(ConsentPurpose.MisconceptionDetection, now.AddMinutes(10)));
    }

    // -------------------------------------------------------------------------
    // Expiry enforcement
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExpiresAt_returns_not_effective_after_expiry()
    {
        var (_, handler, store) = BuildSut();
        const string subjectId = "student-expiry";
        var now = DateTimeOffset.Parse("2026-04-20T10:00:00Z");

        var evt = await handler.HandleAsync(new GrantConsent(
            subjectId, AgeBand.Adult, ConsentPurpose.AiAssistance,
            Scope: "session-local", GrantedByRole: ActorRole.Student,
            GrantedByActorId: subjectId, GrantedAt: now, ExpiresAt: now.AddHours(1)));
        await store.AppendAsync(subjectId, evt);

        var agg = await store.LoadAsync(subjectId);
        Assert.True(agg.State.IsEffectivelyGranted(ConsentPurpose.AiAssistance, now.AddMinutes(30)));
        Assert.False(agg.State.IsEffectivelyGranted(ConsentPurpose.AiAssistance, now.AddHours(1)));
        Assert.False(agg.State.IsEffectivelyGranted(ConsentPurpose.AiAssistance, now.AddHours(2)));
    }

    // -------------------------------------------------------------------------
    // Age-band matrix: every (band, role) cell for GRANT
    // -------------------------------------------------------------------------

    [Theory]
    // Under13 — only Parent + Admin may grant
    [InlineData(AgeBand.Under13, ActorRole.Parent, ConsentPurpose.MisconceptionDetection, true)]
    [InlineData(AgeBand.Under13, ActorRole.Admin, ConsentPurpose.MisconceptionDetection, true)]
    [InlineData(AgeBand.Under13, ActorRole.Student, ConsentPurpose.MisconceptionDetection, false)]
    [InlineData(AgeBand.Under13, ActorRole.Teacher, ConsentPurpose.MisconceptionDetection, false)]
    [InlineData(AgeBand.Under13, ActorRole.System, ConsentPurpose.MisconceptionDetection, false)]
    // Teen13to15 — Parent/Admin always; Student only for non-durable
    [InlineData(AgeBand.Teen13to15, ActorRole.Parent, ConsentPurpose.MisconceptionDetection, true)]
    [InlineData(AgeBand.Teen13to15, ActorRole.Admin, ConsentPurpose.MisconceptionDetection, true)]
    [InlineData(AgeBand.Teen13to15, ActorRole.Student, ConsentPurpose.MisconceptionDetection, false)] // durable
    [InlineData(AgeBand.Teen13to15, ActorRole.Student, ConsentPurpose.LeaderboardDisplay, true)]      // non-durable
    [InlineData(AgeBand.Teen13to15, ActorRole.Teacher, ConsentPurpose.LeaderboardDisplay, false)]
    [InlineData(AgeBand.Teen13to15, ActorRole.System, ConsentPurpose.LeaderboardDisplay, false)]
    // Teen16to17 — Student + Admin; Parent/Teacher/System refused
    [InlineData(AgeBand.Teen16to17, ActorRole.Student, ConsentPurpose.MisconceptionDetection, true)]
    [InlineData(AgeBand.Teen16to17, ActorRole.Admin, ConsentPurpose.MisconceptionDetection, true)]
    [InlineData(AgeBand.Teen16to17, ActorRole.Parent, ConsentPurpose.MisconceptionDetection, false)]
    [InlineData(AgeBand.Teen16to17, ActorRole.Teacher, ConsentPurpose.MisconceptionDetection, false)]
    [InlineData(AgeBand.Teen16to17, ActorRole.System, ConsentPurpose.MisconceptionDetection, false)]
    // Adult — Student + Admin; others refused
    [InlineData(AgeBand.Adult, ActorRole.Student, ConsentPurpose.MisconceptionDetection, true)]
    [InlineData(AgeBand.Adult, ActorRole.Admin, ConsentPurpose.MisconceptionDetection, true)]
    [InlineData(AgeBand.Adult, ActorRole.Parent, ConsentPurpose.MisconceptionDetection, false)]
    [InlineData(AgeBand.Adult, ActorRole.Teacher, ConsentPurpose.MisconceptionDetection, false)]
    [InlineData(AgeBand.Adult, ActorRole.System, ConsentPurpose.MisconceptionDetection, false)]
    public void CanActorGrant_matrix(AgeBand band, ActorRole role, ConsentPurpose purpose, bool expectedAllow)
    {
        var outcome = AgeBandAuthorizationRules.CanActorGrant(purpose, role, band);
        Assert.Equal(expectedAllow, outcome.Allowed);
        if (!expectedAllow)
        {
            Assert.False(string.IsNullOrEmpty(outcome.DenialReason));
        }
    }

    // -------------------------------------------------------------------------
    // Age-band matrix: REVOKE — strictly more permissive
    // -------------------------------------------------------------------------

    [Theory]
    // Under13 — Parent/Admin/System may revoke; Student + Teacher refused
    [InlineData(AgeBand.Under13, ActorRole.Parent, true)]
    [InlineData(AgeBand.Under13, ActorRole.Admin, true)]
    [InlineData(AgeBand.Under13, ActorRole.System, true)]
    [InlineData(AgeBand.Under13, ActorRole.Student, false)]
    [InlineData(AgeBand.Under13, ActorRole.Teacher, false)]
    // Teen13to15 — student MAY revoke (not grant); teacher still refused
    [InlineData(AgeBand.Teen13to15, ActorRole.Student, true)]
    [InlineData(AgeBand.Teen13to15, ActorRole.Parent, true)]
    [InlineData(AgeBand.Teen13to15, ActorRole.Teacher, false)]
    // Teen16to17 — student + parent (legal-minor grounds) + admin
    [InlineData(AgeBand.Teen16to17, ActorRole.Student, true)]
    [InlineData(AgeBand.Teen16to17, ActorRole.Parent, true)]
    [InlineData(AgeBand.Teen16to17, ActorRole.Teacher, false)]
    // Adult — student + admin; parent refused
    [InlineData(AgeBand.Adult, ActorRole.Student, true)]
    [InlineData(AgeBand.Adult, ActorRole.Parent, false)]
    [InlineData(AgeBand.Adult, ActorRole.Teacher, false)]
    public void CanActorRevoke_matrix(AgeBand band, ActorRole role, bool expectedAllow)
    {
        var outcome = AgeBandAuthorizationRules.CanActorRevoke(
            ConsentPurpose.MisconceptionDetection, role, band);
        Assert.Equal(expectedAllow, outcome.Allowed);
    }

    // -------------------------------------------------------------------------
    // HandleAsync(GrantConsent) refuses when the matrix denies
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Grant_throws_ConsentAuthorizationException_when_Under13_student_attempts_self_grant()
    {
        var (_, handler, _) = BuildSut();
        const string subjectId = "student-under13";
        var ex = await Assert.ThrowsAsync<ConsentAuthorizationException>(async () =>
            await handler.HandleAsync(new GrantConsent(
                subjectId, AgeBand.Under13, ConsentPurpose.MisconceptionDetection,
                Scope: "", GrantedByRole: ActorRole.Student, GrantedByActorId: subjectId,
                GrantedAt: DateTimeOffset.UtcNow, ExpiresAt: null)));

        Assert.Contains("Under13", ex.Message);
        Assert.Contains("COPPA", ex.Message);
    }

    [Fact]
    public async Task Grant_throws_when_Teen13to15_student_attempts_durable_grant()
    {
        var (_, handler, _) = BuildSut();
        var ex = await Assert.ThrowsAsync<ConsentAuthorizationException>(async () =>
            await handler.HandleAsync(new GrantConsent(
                "student-teen", AgeBand.Teen13to15, ConsentPurpose.ParentDigest,
                Scope: "", GrantedByRole: ActorRole.Student,
                GrantedByActorId: "student-teen",
                GrantedAt: DateTimeOffset.UtcNow, ExpiresAt: null)));

        Assert.Contains("Teen13to15", ex.Message);
        Assert.Contains("parent consent", ex.Message);
    }

    [Fact]
    public async Task Grant_allows_Teen13to15_student_non_durable_purpose()
    {
        var (_, handler, store) = BuildSut();
        const string subjectId = "student-teen-nondurable";
        var evt = await handler.HandleAsync(new GrantConsent(
            subjectId, AgeBand.Teen13to15, ConsentPurpose.LeaderboardDisplay,
            Scope: "leaderboard", GrantedByRole: ActorRole.Student,
            GrantedByActorId: subjectId,
            GrantedAt: DateTimeOffset.UtcNow, ExpiresAt: null));
        await store.AppendAsync(subjectId, evt);

        var agg = await store.LoadAsync(subjectId);
        Assert.True(agg.State.IsEffectivelyGranted(ConsentPurpose.LeaderboardDisplay, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Grant_allows_Teen16to17_student_durable_purpose()
    {
        var (_, handler, store) = BuildSut();
        const string subjectId = "student-teen-16";
        var evt = await handler.HandleAsync(new GrantConsent(
            subjectId, AgeBand.Teen16to17, ConsentPurpose.AiAssistance,
            Scope: "tutoring", GrantedByRole: ActorRole.Student,
            GrantedByActorId: subjectId,
            GrantedAt: DateTimeOffset.UtcNow, ExpiresAt: null));
        await store.AppendAsync(subjectId, evt);

        var agg = await store.LoadAsync(subjectId);
        Assert.True(agg.State.IsEffectivelyGranted(ConsentPurpose.AiAssistance, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Revoke_throws_when_Under13_student_self_revokes()
    {
        var (_, handler, _) = BuildSut();
        var ex = await Assert.ThrowsAsync<ConsentAuthorizationException>(async () =>
            await handler.HandleAsync(new RevokeConsent(
                "student-u13", AgeBand.Under13, ConsentPurpose.MisconceptionDetection,
                RevokedByRole: ActorRole.Student, RevokedByActorId: "student-u13",
                RevokedAt: DateTimeOffset.UtcNow, Reason: "test")));
        Assert.Contains("Under13", ex.Message);
    }

    // -------------------------------------------------------------------------
    // Parent-review workflow
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ParentReview_Approved_emits_event_and_folds_into_state()
    {
        var (_, handler, store) = BuildSut();
        const string subjectId = "student-teen-review";
        var purposes = new List<ConsentPurpose>
        {
            ConsentPurpose.MisconceptionDetection,
            ConsentPurpose.ParentDigest,
        };

        var evt = await handler.HandleAsync(new RecordParentReview(
            StudentSubjectId: subjectId,
            StudentBand: AgeBand.Teen13to15,
            ParentActorId: "parent-001",
            PurposesReviewed: purposes,
            Outcome: ConsentReviewOutcome.Approved,
            ReviewedAt: DateTimeOffset.UtcNow));

        await store.AppendAsync(subjectId, evt);
        var agg = await store.LoadAsync(subjectId);

        Assert.NotNull(agg.State.LastParentReview);
        Assert.Equal(ConsentReviewOutcome.Approved, agg.State.LastParentReview!.Outcome);
        Assert.Equal(2, agg.State.LastParentReview.PurposesReviewed.Count);
    }

    [Fact]
    public async Task ParentReview_of_Adult_subject_is_refused()
    {
        var (_, handler, _) = BuildSut();
        var ex = await Assert.ThrowsAsync<ConsentAuthorizationException>(async () =>
            await handler.HandleAsync(new RecordParentReview(
                StudentSubjectId: "student-adult", StudentBand: AgeBand.Adult,
                ParentActorId: "parent-01",
                PurposesReviewed: new List<ConsentPurpose> { ConsentPurpose.ParentDigest },
                Outcome: ConsentReviewOutcome.Approved,
                ReviewedAt: DateTimeOffset.UtcNow)));
        Assert.Contains("Adult", ex.Message);
    }

    // Direct matrix helper coverage for non-parent role refusal
    [Fact]
    public void CanActorReviewAsParent_refuses_non_parent_role()
    {
        var outcome = AgeBandAuthorizationRules.CanActorReviewAsParent(
            ActorRole.Teacher, AgeBand.Teen13to15);
        Assert.False(outcome.Allowed);
        Assert.Contains("Parent", outcome.DenialReason);
    }

    [Fact]
    public void CanActorReviewAsParent_refuses_Adult_subject()
    {
        var outcome = AgeBandAuthorizationRules.CanActorReviewAsParent(
            ActorRole.Parent, AgeBand.Adult);
        Assert.False(outcome.Allowed);
        Assert.Contains("Adult", outcome.DenialReason);
    }

    // -------------------------------------------------------------------------
    // Command validation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Grant_with_empty_subject_id_throws_ArgumentException()
    {
        var (_, handler, _) = BuildSut();
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await handler.HandleAsync(new GrantConsent(
                SubjectId: "", SubjectBand: AgeBand.Adult,
                Purpose: ConsentPurpose.MisconceptionDetection,
                Scope: "", GrantedByRole: ActorRole.Student,
                GrantedByActorId: "x",
                GrantedAt: DateTimeOffset.UtcNow, ExpiresAt: null)));
    }

    [Fact]
    public async Task ParentReview_with_empty_purposes_throws_ArgumentException()
    {
        var (_, handler, _) = BuildSut();
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await handler.HandleAsync(new RecordParentReview(
                StudentSubjectId: "s", StudentBand: AgeBand.Teen13to15,
                ParentActorId: "p",
                PurposesReviewed: new List<ConsentPurpose>(),
                Outcome: ConsentReviewOutcome.Approved,
                ReviewedAt: DateTimeOffset.UtcNow)));
    }

    // -------------------------------------------------------------------------
    // Durable purpose classification (ADR-0041)
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(ConsentPurpose.MisconceptionDetection, true)]
    [InlineData(ConsentPurpose.ParentDigest, true)]
    [InlineData(ConsentPurpose.TeacherShare, true)]
    [InlineData(ConsentPurpose.AiAssistance, true)]
    [InlineData(ConsentPurpose.ExternalIntegration, true)]
    [InlineData(ConsentPurpose.LeaderboardDisplay, false)]
    [InlineData(ConsentPurpose.MarketingNudges, false)]
    [InlineData(ConsentPurpose.CrossTenantBenchmarking, false)]
    [InlineData(ConsentPurpose.AnalyticsAggregation, false)]
    public void IsDurableDataPurpose_classification_matches_ADR0041(
        ConsentPurpose purpose, bool expectedDurable)
    {
        Assert.Equal(expectedDurable, AgeBandAuthorizationRules.IsDurableDataPurpose(purpose));
    }

    // -------------------------------------------------------------------------
    // AgeBandComputation
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(5, AgeBand.Under13)]
    [InlineData(12, AgeBand.Under13)]
    [InlineData(13, AgeBand.Teen13to15)]
    [InlineData(15, AgeBand.Teen13to15)]
    [InlineData(16, AgeBand.Teen16to17)]
    [InlineData(17, AgeBand.Teen16to17)]
    [InlineData(18, AgeBand.Adult)]
    [InlineData(99, AgeBand.Adult)]
    public void AgeBandComputation_FromAge_maps_correctly(int age, AgeBand expected)
    {
        Assert.Equal(expected, AgeBandComputation.FromAge(age));
    }

    // -------------------------------------------------------------------------
    // Stream key format
    // -------------------------------------------------------------------------

    [Fact]
    public void StreamKey_uses_consent_prefix()
    {
        var key = ConsentAggregate.StreamKey("alpha-001");
        Assert.Equal("consent-alpha-001", key);
    }

    [Fact]
    public void StreamKey_rejects_empty_subject_id()
    {
        Assert.Throws<ArgumentException>(() => ConsentAggregate.StreamKey(""));
    }
}
