// =============================================================================
// Cena Platform — AgeBandPolicy unit tests (prr-052)
//
// Exhaustive per-band tests for the central age-band policy seam. Covers:
//
//   1. Under13: parent sees everything, no veto, safety always visible,
//      student does NOT see parent view.
//   2. Teen13to15: transparency right (student sees same as parent) but
//      no veto authority.
//   3. Teen16to17: full transparency + veto on non-safety purposes;
//      safety flags remain ParentCanSee=true regardless.
//   4. Adult: default parent visibility is false (fresh grant required)
//      but student retains veto + transparency.
//   5. Safety-category key detection (IsSafetyCategoryKey).
//   6. Institute policy override (InstitutePolicyAllowsVeto=false strips
//      StudentCanVeto on every field).
//   7. Existing veto on a purpose is respected when the band permits
//      veto; ignored when it doesn't.
// =============================================================================

using Cena.Actors.Consent;

namespace Cena.Actors.Tests.Consent;

public sealed class AgeBandPolicyTests
{
    // ── Under13 ──────────────────────────────────────────────────────────

    [Fact]
    public void Under13_ParentSeesAll_NoVetoAllowed()
    {
        var input = new VisibilityPolicyInput(
            SubjectBand: AgeBand.Under13,
            VetoedPurposes: new HashSet<ConsentPurpose>(),
            InstituteId: "inst-A");

        var output = AgeBandPolicy.EvaluateDashboard(input);

        Assert.False(output.StudentCanSeeParentView);
        Assert.False(output.StudentHasAnyVetoRight);
        Assert.All(output.Fields, f =>
        {
            Assert.False(f.StudentCanVeto, $"Under13 must not veto '{f.FieldKey}'.");
            Assert.False(f.StudentSeesSameAsParent,
                $"Under13 has no transparency right, even for '{f.FieldKey}'.");
        });
        // Safety categories still visible to parent.
        var atRisk = output.Fields.Single(f =>
            f.FieldKey == nameof(SafetyVisibilityCategory.AtRiskSignal));
        Assert.True(atRisk.ParentCanSee);
        Assert.Equal(ParentVisibilityKind.SafetyCategory, atRisk.Kind);
    }

    [Theory]
    [InlineData(ConsentPurpose.ParentDigest)]
    [InlineData(ConsentPurpose.MisconceptionDetection)]
    [InlineData(ConsentPurpose.AiAssistance)]
    public void Under13_CannotVetoAnyPurpose(ConsentPurpose purpose)
    {
        Assert.False(AgeBandPolicy.CanStudentVetoPurpose(AgeBand.Under13, purpose));
    }

    // ── Teen13to15 ───────────────────────────────────────────────────────

    [Fact]
    public void Teen13to15_SeesParentView_NoVetoAuthority()
    {
        var input = new VisibilityPolicyInput(
            SubjectBand: AgeBand.Teen13to15,
            VetoedPurposes: new HashSet<ConsentPurpose>(),
            InstituteId: "inst-A");

        var output = AgeBandPolicy.EvaluateDashboard(input);

        Assert.True(output.StudentCanSeeParentView, "13+ has the transparency right.");
        Assert.False(output.StudentHasAnyVetoRight, "13–15 has no veto authority.");
        Assert.All(output.Fields, f =>
        {
            Assert.False(f.StudentCanVeto,
                $"Teen13to15 must not be able to veto '{f.FieldKey}'.");
            Assert.True(f.StudentSeesSameAsParent,
                $"Teen13to15 must see what parent sees for '{f.FieldKey}'.");
        });
    }

    // ── Teen16to17 ───────────────────────────────────────────────────────

    [Fact]
    public void Teen16to17_CanVetoConsentPurposes_SafetyFlagsImmune()
    {
        var input = new VisibilityPolicyInput(
            SubjectBand: AgeBand.Teen16to17,
            VetoedPurposes: new HashSet<ConsentPurpose>(),
            InstituteId: "inst-A");

        var output = AgeBandPolicy.EvaluateDashboard(input);

        Assert.True(output.StudentCanSeeParentView);
        Assert.True(output.StudentHasAnyVetoRight);

        foreach (var field in output.Fields)
        {
            if (field.Kind == ParentVisibilityKind.SafetyCategory)
            {
                Assert.False(field.StudentCanVeto,
                    $"Safety category '{field.FieldKey}' must NEVER be vetoable (duty-of-care).");
                Assert.True(field.ParentCanSee,
                    $"Safety category '{field.FieldKey}' must remain parent-visible.");
            }
            else
            {
                Assert.True(field.StudentCanVeto,
                    $"Teen16to17 must be able to veto consent purpose '{field.FieldKey}'.");
            }
        }
    }

    [Fact]
    public void Teen16to17_ExistingVeto_StripsParentVisibility()
    {
        var input = new VisibilityPolicyInput(
            SubjectBand: AgeBand.Teen16to17,
            VetoedPurposes: new HashSet<ConsentPurpose> { ConsentPurpose.ParentDigest },
            InstituteId: "inst-A");

        var output = AgeBandPolicy.EvaluateDashboard(input);

        var digest = output.Fields.Single(f =>
            f.FieldKey == nameof(ConsentPurpose.ParentDigest));
        Assert.False(digest.ParentCanSee, "Vetoed purpose must not be parent-visible.");
        Assert.True(digest.StudentCanVeto, "Still vetoable (idempotent).");

        // At-risk safety category still visible even though a purpose veto
        // exists — they are separate rails.
        var atRisk = output.Fields.Single(f =>
            f.FieldKey == nameof(SafetyVisibilityCategory.AtRiskSignal));
        Assert.True(atRisk.ParentCanSee);
    }

    [Fact]
    public void Teen13to15_VetoedPurpose_IsIgnored_BandHasNoVetoAuthority()
    {
        // Defensive: if a veto event somehow landed for a Teen13to15,
        // the policy refuses to honour it (the event should not have
        // been produced in the first place; the command handler guards
        // against it).
        var input = new VisibilityPolicyInput(
            SubjectBand: AgeBand.Teen13to15,
            VetoedPurposes: new HashSet<ConsentPurpose> { ConsentPurpose.ParentDigest },
            InstituteId: "inst-A");

        var output = AgeBandPolicy.EvaluateDashboard(input);

        var digest = output.Fields.Single(f =>
            f.FieldKey == nameof(ConsentPurpose.ParentDigest));
        Assert.True(digest.ParentCanSee,
            "Teen13to15 veto must be ignored — band has no veto authority.");
    }

    // ── Adult ────────────────────────────────────────────────────────────

    [Fact]
    public void Adult_DefaultParentInvisible_StudentVetoAvailable()
    {
        var input = new VisibilityPolicyInput(
            SubjectBand: AgeBand.Adult,
            VetoedPurposes: new HashSet<ConsentPurpose>(),
            InstituteId: "inst-A");

        var output = AgeBandPolicy.EvaluateDashboard(input);

        Assert.True(output.StudentCanSeeParentView);
        Assert.True(output.StudentHasAnyVetoRight);

        // Every consent purpose defaults to not-visible (fresh grant
        // required) for Adult.
        var consentFields = output.Fields
            .Where(f => f.Kind == ParentVisibilityKind.ConsentPurpose)
            .ToList();
        Assert.All(consentFields, f =>
            Assert.False(f.ParentCanSee,
                $"Adult default must hide '{f.FieldKey}' until a fresh grant is recorded."));
    }

    // ── Institute policy override ────────────────────────────────────────

    [Fact]
    public void InstitutePolicyStripsVetoRights_AllFieldsShowStudentCanVetoFalse()
    {
        var input = new VisibilityPolicyInput(
            SubjectBand: AgeBand.Teen16to17,
            VetoedPurposes: new HashSet<ConsentPurpose>(),
            InstituteId: "inst-strict",
            InstitutePolicyAllowsVeto: false);

        var output = AgeBandPolicy.EvaluateDashboard(input);

        Assert.False(output.StudentHasAnyVetoRight,
            "Institute-policy override disables aggregate veto flag.");
        Assert.All(output.Fields, f => Assert.False(f.StudentCanVeto,
            $"Institute-policy override must strip veto from '{f.FieldKey}'."));
    }

    // ── Safety-category detection ────────────────────────────────────────

    [Theory]
    [InlineData("AtRiskSignal", true)]
    [InlineData("AttendanceSignal", true)]
    [InlineData("ParentDigest", false)]
    [InlineData("MisconceptionDetection", false)]
    [InlineData("bogus-key", false)]
    [InlineData("", false)]
    public void IsSafetyCategoryKey_RecognizesCanonicalNames(string key, bool expected)
    {
        var got = AgeBandPolicy.IsSafetyCategoryKey(key, out _);
        Assert.Equal(expected, got);
    }

    // ── CanStudentVetoPurpose pure-function table drive ──────────────────

    [Theory]
    [InlineData(AgeBand.Under13,    false)]
    [InlineData(AgeBand.Teen13to15, false)]
    [InlineData(AgeBand.Teen16to17, true)]
    [InlineData(AgeBand.Adult,      true)]
    public void CanStudentVetoPurpose_PerBand(AgeBand band, bool expected)
    {
        foreach (var purpose in AgeBandPolicy.ParentDashboardPurposes)
        {
            Assert.Equal(expected, AgeBandPolicy.CanStudentVetoPurpose(band, purpose));
        }
    }

    // ── Safety categories are always in the field list ───────────────────

    [Theory]
    [InlineData(AgeBand.Under13)]
    [InlineData(AgeBand.Teen13to15)]
    [InlineData(AgeBand.Teen16to17)]
    [InlineData(AgeBand.Adult)]
    public void SafetyCategoriesAlwaysListed_EveryBand(AgeBand band)
    {
        var input = new VisibilityPolicyInput(
            SubjectBand: band,
            VetoedPurposes: new HashSet<ConsentPurpose>(),
            InstituteId: "inst-A");

        var output = AgeBandPolicy.EvaluateDashboard(input);

        foreach (var category in AgeBandPolicy.AlwaysVisibleSafetyCategories)
        {
            var found = output.Fields.SingleOrDefault(f => f.FieldKey == category.ToString());
            Assert.NotNull(found);
            Assert.Equal(ParentVisibilityKind.SafetyCategory, found!.Kind);
            Assert.True(found.ParentCanSee,
                $"Safety category '{category}' must be parent-visible at {band}.");
            Assert.False(found.StudentCanVeto);
        }
    }
}
