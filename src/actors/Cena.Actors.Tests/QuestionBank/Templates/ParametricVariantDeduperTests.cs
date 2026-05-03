// =============================================================================
// Cena Platform — ParametricVariantDeduper tests (prr-200)
// =============================================================================

using Cena.Actors.QuestionBank.Templates;

namespace Cena.Actors.Tests.QuestionBank.Templates;

public sealed class ParametricVariantDeduperTests
{
    private static ParametricVariant Variant(string stem, string answer, params (string id, string text)[] distractors) =>
        new(
            TemplateId: "t1",
            TemplateVersion: 1,
            Seed: 0,
            SlotValues: Array.Empty<ParametricSlotValue>(),
            RenderedStem: stem,
            CanonicalAnswer: answer,
            Distractors: distractors
                .Select(d => new ParametricDistractor(d.id, d.text, null))
                .ToArray());

    [Fact]
    public void TryAdmit_AcceptsFirstSeen_AndRejectsDuplicates()
    {
        var d = new ParametricVariantDeduper();
        Assert.True(d.TryAdmit(Variant("Solve: 2x+3=7", "2")));
        Assert.False(d.TryAdmit(Variant("Solve: 2x+3=7", "2")));
        Assert.Equal(1, d.UniqueCount);
    }

    [Fact]
    public void TryAdmit_WhitespaceDifferences_AreSameCanonicalForm()
    {
        var d = new ParametricVariantDeduper();
        Assert.True(d.TryAdmit(Variant("Solve:  2x+3=7", "2")));
        Assert.False(d.TryAdmit(Variant("Solve: 2x+3=7", "2")));
    }

    [Fact]
    public void TryAdmit_DifferentAnswer_IsNewCanonicalForm()
    {
        var d = new ParametricVariantDeduper();
        Assert.True(d.TryAdmit(Variant("Q", "2")));
        Assert.True(d.TryAdmit(Variant("Q", "3")));
        Assert.Equal(2, d.UniqueCount);
    }

    [Fact]
    public void TryAdmit_DistractorSetMatters_RegardlessOfOrder()
    {
        var d = new ParametricVariantDeduper();
        Assert.True(d.TryAdmit(Variant("Q", "2", ("m1", "a"), ("m2", "b"))));
        // Same distractor set (order reversed) — canonical form is identical.
        Assert.False(d.TryAdmit(Variant("Q", "2", ("m2", "b"), ("m1", "a"))));
        // Adding a new distractor text — new canonical form.
        Assert.True(d.TryAdmit(Variant("Q", "2", ("m1", "a"), ("m2", "b"), ("m3", "c"))));
    }

    [Fact]
    public void Hash_Is_StableAcrossCalls()
    {
        var v = Variant("Solve: 2x+3=7", "2", ("m1", "zero"));
        var h1 = ParametricVariantDeduper.ComputeCanonicalHash(v);
        var h2 = ParametricVariantDeduper.ComputeCanonicalHash(v);
        Assert.Equal(h1, h2);
        Assert.Equal(64, h1.Length); // SHA-256 hex
    }
}
