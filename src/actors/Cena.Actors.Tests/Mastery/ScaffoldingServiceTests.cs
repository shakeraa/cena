// =============================================================================
// MST-011 Tests: Scaffolding level determiner
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Tests.Mastery;

public sealed class ScaffoldingServiceTests
{
    [Theory]
    [InlineData(0.15f, 0.60f, ScaffoldingLevel.Full)]       // low mastery + weak prereqs
    [InlineData(0.15f, 0.85f, ScaffoldingLevel.Partial)]    // low mastery + strong prereqs
    [InlineData(0.35f, 0.90f, ScaffoldingLevel.Partial)]    // developing + strong prereqs
    [InlineData(0.55f, 0.90f, ScaffoldingLevel.HintsOnly)]  // mid mastery
    [InlineData(0.75f, 0.95f, ScaffoldingLevel.None)]        // proficient
    [InlineData(0.92f, 1.00f, ScaffoldingLevel.None)]        // mastered
    public void DetermineLevel_CorrectMapping(float mastery, float psi, ScaffoldingLevel expected)
    {
        var level = ScaffoldingService.DetermineLevel(mastery, psi);
        Assert.Equal(expected, level);
    }

    [Fact]
    public void DetermineLevel_BoundaryAt70_TransitionsToNone()
    {
        Assert.Equal(ScaffoldingLevel.HintsOnly,
            ScaffoldingService.DetermineLevel(0.69f, 0.90f));
        Assert.Equal(ScaffoldingLevel.None,
            ScaffoldingService.DetermineLevel(0.70f, 0.90f));
    }

    [Fact]
    public void GetMetadata_FullScaffolding_ShowsWorkedExample()
    {
        var meta = ScaffoldingService.GetScaffoldingMetadata(ScaffoldingLevel.Full);

        Assert.True(meta.ShowWorkedExample);
        Assert.True(meta.ShowHintButton);
        Assert.Equal(3, meta.MaxHints);
        Assert.True(meta.RevealAnswer);
        Assert.Equal("worked-example", meta.PromptVariant);
    }

    [Fact]
    public void GetMetadata_PartialScaffolding()
    {
        var meta = ScaffoldingService.GetScaffoldingMetadata(ScaffoldingLevel.Partial);

        Assert.False(meta.ShowWorkedExample);
        Assert.True(meta.ShowHintButton);
        Assert.Equal(2, meta.MaxHints);
        Assert.True(meta.RevealAnswer);
    }

    [Fact]
    public void GetMetadata_HintsOnly()
    {
        var meta = ScaffoldingService.GetScaffoldingMetadata(ScaffoldingLevel.HintsOnly);

        Assert.False(meta.ShowWorkedExample);
        Assert.True(meta.ShowHintButton);
        Assert.Equal(1, meta.MaxHints);
        Assert.False(meta.RevealAnswer);
    }

    [Fact]
    public void GetMetadata_NoScaffolding_NoHelp()
    {
        var meta = ScaffoldingService.GetScaffoldingMetadata(ScaffoldingLevel.None);

        Assert.False(meta.ShowWorkedExample);
        Assert.False(meta.ShowHintButton);
        Assert.Equal(0, meta.MaxHints);
        Assert.False(meta.RevealAnswer);
    }
}
