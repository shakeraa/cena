// =============================================================================
// Cena Platform — DiagnosisHintLevelAdjuster tests (RDY-063 Phase 2b)
//
// Tests every rule in the adjuster against the 7-category ontology.
// These are the confidence-band contract — any rule adjustment must
// update the matching test explicitly. No hidden drift.
// =============================================================================

using Cena.Actors.Diagnosis;

namespace Cena.Actors.Tests.Diagnosis;

public class DiagnosisHintLevelAdjusterTests
{
    private readonly IHintLevelAdjuster _adjuster = new DiagnosisHintLevelAdjuster();
    private const float MinConf = 0.65f;

    private static StuckDiagnosis Diag(
        StuckType primary,
        float confidence,
        StuckScaffoldStrategy strategy = StuckScaffoldStrategy.Unspecified) =>
        new(
            Primary: primary,
            PrimaryConfidence: confidence,
            Secondary: StuckType.Unknown,
            SecondaryConfidence: 0f,
            SuggestedStrategy: strategy,
            FocusChapterId: null,
            ShouldInvolveTeacher: false,
            Source: StuckDiagnosisSource.Heuristic,
            ClassifierVersion: "v1.0.0-test",
            DiagnosedAt: DateTimeOffset.UtcNow,
            LatencyMs: 5,
            SourceReasonCode: "test");

    [Fact]
    public void NullDiagnosis_KeepsOriginalLevel()
    {
        var r = _adjuster.Adjust(2, 3, diagnosis: null, MinConf);
        Assert.False(r.Changed);
        Assert.Equal(2, r.AdjustedLevel);
        Assert.Equal("adjuster.no_signal", r.ReasonCode);
    }

    [Fact]
    public void UnknownType_KeepsOriginalLevel()
    {
        var r = _adjuster.Adjust(2, 3, Diag(StuckType.Unknown, 0.9f), MinConf);
        Assert.False(r.Changed);
    }

    [Fact]
    public void BelowMinConfidence_KeepsOriginalLevel()
    {
        var r = _adjuster.Adjust(2, 3, Diag(StuckType.MetaStuck, 0.5f), MinConf);
        Assert.False(r.Changed);
        Assert.Equal(2, r.AdjustedLevel);
    }

    [Fact]
    public void MetaStuck_ClampsToLevel1()
    {
        var r = _adjuster.Adjust(3, 3, Diag(StuckType.MetaStuck, 0.8f), MinConf);
        Assert.True(r.Changed);
        Assert.Equal(1, r.AdjustedLevel);
        Assert.Equal("adjuster.meta_stuck_clamp_to_1", r.ReasonCode);
    }

    [Fact]
    public void Motivational_ClampsToLevel1()
    {
        var r = _adjuster.Adjust(2, 3, Diag(StuckType.Motivational, 0.75f), MinConf);
        Assert.True(r.Changed);
        Assert.Equal(1, r.AdjustedLevel);
    }

    [Fact]
    public void Motivational_AlreadyAtLevel1_DoesNotReportChange()
    {
        var r = _adjuster.Adjust(1, 3, Diag(StuckType.Motivational, 0.75f), MinConf);
        Assert.False(r.Changed);
        Assert.Equal(1, r.AdjustedLevel);
    }

    [Fact]
    public void Misconception_BumpsLevel1ToLevel2()
    {
        var r = _adjuster.Adjust(1, 3, Diag(StuckType.Misconception, 0.8f), MinConf);
        Assert.True(r.Changed);
        Assert.Equal(2, r.AdjustedLevel);
    }

    [Fact]
    public void Misconception_KeepsLevel2And3()
    {
        var r2 = _adjuster.Adjust(2, 3, Diag(StuckType.Misconception, 0.8f), MinConf);
        Assert.False(r2.Changed);
        Assert.Equal(2, r2.AdjustedLevel);

        var r3 = _adjuster.Adjust(3, 3, Diag(StuckType.Misconception, 0.8f), MinConf);
        Assert.False(r3.Changed);
        Assert.Equal(3, r3.AdjustedLevel);
    }

    [Fact]
    public void Strategic_ClampsLevel3ToLevel2()
    {
        var r = _adjuster.Adjust(3, 3, Diag(StuckType.Strategic, 0.8f), MinConf);
        Assert.True(r.Changed);
        Assert.Equal(2, r.AdjustedLevel);
    }

    [Fact]
    public void Strategic_KeepsLevel1And2()
    {
        var r1 = _adjuster.Adjust(1, 3, Diag(StuckType.Strategic, 0.8f), MinConf);
        Assert.False(r1.Changed);

        var r2 = _adjuster.Adjust(2, 3, Diag(StuckType.Strategic, 0.8f), MinConf);
        Assert.False(r2.Changed);
    }

    [Fact]
    public void Encoding_Recall_Procedural_KeepOriginalLevel()
    {
        foreach (var t in new[] { StuckType.Encoding, StuckType.Recall, StuckType.Procedural })
        {
            var r = _adjuster.Adjust(2, 3, Diag(t, 0.8f), MinConf);
            Assert.False(r.Changed);
            Assert.Equal(2, r.AdjustedLevel);
        }
    }

    [Fact]
    public void NeverExceedsMaxLevel()
    {
        // Misconception tries to bump to 2 but maxLevel is 1 → clamp to 1.
        var r = _adjuster.Adjust(1, 1, Diag(StuckType.Misconception, 0.9f), MinConf);
        Assert.Equal(1, r.AdjustedLevel);
        Assert.True(r.AdjustedLevel <= 1);
    }

    [Fact]
    public void NeverGoesBelow1()
    {
        var r = _adjuster.Adjust(0, 3, Diag(StuckType.MetaStuck, 0.9f), MinConf);
        Assert.Equal(1, r.AdjustedLevel);
    }

    [Fact]
    public void InvalidRequestedLevel_IsClamped()
    {
        // Requested level 5 with maxLevel 3 → clamped to 3 first, then
        // Strategic rule clamps from 3 to 2.
        var r = _adjuster.Adjust(5, 3, Diag(StuckType.Strategic, 0.8f), MinConf);
        Assert.Equal(2, r.AdjustedLevel);
        Assert.Equal(3, r.OriginalLevel);  // clamped before adjustment
    }
}
