// =============================================================================
// MST-002 Tests: Bayesian Knowledge Tracing engine
// =============================================================================

using Cena.Actors.Mastery;

namespace Cena.Actors.Tests.Mastery;

public sealed class BktTracerTests
{
    private static readonly BktParameters DefaultParams = BktParameters.Default;

    [Fact]
    public void BktUpdate_CorrectAnswer_IncreasesMastery()
    {
        var pL = 0.50f;
        var updated = BktTracer.Update(pL, isCorrect: true, DefaultParams);

        Assert.True(updated > pL, $"Expected > {pL}, got {updated}");
        // Updated 2026-04-20 per ADR-0039 (Koedinger defaults pLearn=0.15, pSlip=0.10, pGuess=0.15).
        // Worked example: P(L|correct) = 0.90*0.50 / (0.90*0.50 + 0.15*0.50) = 0.8571
        // P(L_next) = 0.8571 + (1-0.8571)*0.15 = 0.8786
        Assert.InRange(updated, 0.87f, 0.89f);
    }

    [Fact]
    public void BktUpdate_IncorrectAnswer_DecreasesMastery()
    {
        var pL = 0.80f;
        var updated = BktTracer.Update(pL, isCorrect: false, DefaultParams);

        Assert.True(updated < pL, $"Expected < {pL}, got {updated}");
    }

    [Fact]
    public void BktUpdate_FromZero_CorrectGuess_StillLow()
    {
        var pL = 0.01f;
        var updated = BktTracer.Update(pL, isCorrect: true, DefaultParams);

        Assert.True(updated < 0.30f, $"Guessing student should stay low, got {updated}");
    }

    [Fact]
    public void BktUpdate_HighMastery_SlipDoesNotCrash()
    {
        var pL = 0.99f;
        var updated = BktTracer.Update(pL, isCorrect: false, DefaultParams);

        Assert.InRange(updated, 0.001f, 0.999f);
    }

    [Fact]
    public void BktUpdate_OutputClamped()
    {
        // Even extreme inputs stay in [0.001, 0.999]
        var low = BktTracer.Update(0.001f, isCorrect: false, DefaultParams);
        Assert.True(low >= 0.001f);

        var high = BktTracer.Update(0.999f, isCorrect: true, DefaultParams);
        Assert.True(high <= 0.999f);
    }

    [Fact]
    public void BktUpdate_ZeroAllocation()
    {
        var pL = 0.50f;
        var p = DefaultParams;

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10_000; i++)
        {
            pL = BktTracer.Update(pL, i % 2 == 0, p);
        }
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void BktParameters_Default_IsValid()
    {
        Assert.True(BktParameters.Default.IsValid);
    }

    [Fact]
    public void BktParameters_Invalid_SlipPlusGuess()
    {
        var invalid = new BktParameters(0.1f, 0.2f, 0.6f, 0.6f);
        Assert.False(invalid.IsValid);
    }

    [Fact]
    public void UpdateState_ReturnNewStateWithUpdatedMastery()
    {
        var state = new ConceptMasteryState { MasteryProbability = 0.50f };
        var updated = BktTracer.UpdateState(state, isCorrect: true, DefaultParams);

        Assert.True(updated.MasteryProbability > 0.50f);
        Assert.Equal(0, updated.AttemptCount); // BKT doesn't touch counters
    }
}
