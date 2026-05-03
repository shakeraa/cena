// =============================================================================
// PRR-031 — MathAriaLabels tests.
//
// Covers:
//   - Arithmetic operators in en / ar / he
//   - \frac, \sqrt, \int, \sum, \lim structures
//   - Greek letters including \Delta vs \delta (PRR-032 driver)
//   - ^2, ^3, ^{n+1} superscript rules (squared / cubed / explicit)
//   - Subscripts
//   - Trig with Arab-sector abbreviations (sin→جا, cos→جتا, tan→ظا)
//   - The canonical sample from the task body: \\frac{x+1}{2} in he/ar
//     must produce NON-English aria-label text.
// =============================================================================

using Cena.Infrastructure.Accessibility;

namespace Cena.Infrastructure.Tests.Accessibility;

public class MathAriaLabelsTests
{
    // ── English baseline ──────────────────────────────────────────────────

    [Fact]
    public void English_SimpleAddition_SpeaksPlus()
    {
        Assert.Equal("x plus 1", MathAriaLabels.ToAriaLabel("x+1", "en"));
    }

    [Fact]
    public void English_Fraction_UsesFractionKeyword()
    {
        var label = MathAriaLabels.ToAriaLabel(@"\frac{x+1}{2}", "en");
        Assert.Contains("the fraction", label);
        Assert.Contains("over", label);
        Assert.Contains("end fraction", label);
        Assert.Contains("plus", label);
    }

    [Fact]
    public void English_SquaredIsSpecialCased()
    {
        Assert.Equal("x squared", MathAriaLabels.ToAriaLabel("x^2", "en"));
    }

    [Fact]
    public void English_CubedIsSpecialCased()
    {
        Assert.Equal("x cubed", MathAriaLabels.ToAriaLabel("x^3", "en"));
    }

    [Fact]
    public void English_ArbitraryPower_UsesToThePowerOf()
    {
        var label = MathAriaLabels.ToAriaLabel("x^{n+1}", "en");
        Assert.Contains("to the power of", label);
        Assert.Contains("n", label);
        Assert.Contains("plus", label);
    }

    [Fact]
    public void English_SqrtIsSpoken()
    {
        var label = MathAriaLabels.ToAriaLabel(@"\sqrt{x+1}", "en");
        Assert.Contains("the square root of", label);
        Assert.Contains("end root", label);
    }

    [Fact]
    public void English_TrigFunctions()
    {
        Assert.Contains("sine", MathAriaLabels.ToAriaLabel(@"\sin(x)", "en"));
        Assert.Contains("cosine", MathAriaLabels.ToAriaLabel(@"\cos(x)", "en"));
        Assert.Contains("tangent", MathAriaLabels.ToAriaLabel(@"\tan(x)", "en"));
    }

    [Fact]
    public void English_InequalityOperators()
    {
        Assert.Contains("less than or equal to", MathAriaLabels.ToAriaLabel(@"x \leq 3", "en"));
        Assert.Contains("greater than or equal to", MathAriaLabels.ToAriaLabel(@"x \geq 3", "en"));
        Assert.Contains("not equal to", MathAriaLabels.ToAriaLabel(@"x \neq 0", "en"));
    }

    [Fact]
    public void English_PlusMinus()
    {
        Assert.Contains("plus or minus", MathAriaLabels.ToAriaLabel(@"\pm 3", "en"));
    }

    [Fact]
    public void English_IntegralSumLimit()
    {
        Assert.Contains("integral of", MathAriaLabels.ToAriaLabel(@"\int f(x)", "en"));
        Assert.Contains("sum of", MathAriaLabels.ToAriaLabel(@"\sum_{i=0}^{n} x_i", "en"));
        Assert.Contains("limit", MathAriaLabels.ToAriaLabel(@"\lim_{x \to 0} f(x)", "en"));
    }

    [Fact]
    public void English_GreekDeltaLowerVsUpper()
    {
        var lower = MathAriaLabels.ToAriaLabel(@"\delta", "en");
        var upper = MathAriaLabels.ToAriaLabel(@"\Delta", "en");
        Assert.Equal("delta", lower);
        Assert.Equal("capital delta", upper);
        Assert.NotEqual(lower, upper);
    }

    [Fact]
    public void English_Subscript()
    {
        var label = MathAriaLabels.ToAriaLabel("x_{1}", "en");
        Assert.Contains("sub", label);
        Assert.Contains("1", label);
    }

    // ── Hebrew ────────────────────────────────────────────────────────────

    [Fact]
    public void Hebrew_FractionUsesHebrewTerms()
    {
        var label = MathAriaLabels.ToAriaLabel(@"\frac{x+1}{2}", "he");
        // Task-defined assertion: output must NOT match English label.
        Assert.DoesNotContain("over", label);
        Assert.DoesNotContain("plus", label);
        Assert.DoesNotContain("the fraction", label);
        // Must contain Hebrew keywords:
        Assert.Contains("השבר", label);       // "the fraction"
        Assert.Contains("חלקי", label);       // "over"
        Assert.Contains("ועוד", label);       // "plus"
        Assert.Contains("סוף השבר", label);   // "end fraction"
    }

    [Fact]
    public void Hebrew_Squared()
    {
        Assert.Equal("x בריבוע", MathAriaLabels.ToAriaLabel("x^2", "he"));
    }

    [Fact]
    public void Hebrew_Trig()
    {
        Assert.Contains("סינוס", MathAriaLabels.ToAriaLabel(@"\sin(x)", "he"));
        Assert.Contains("קוסינוס", MathAriaLabels.ToAriaLabel(@"\cos(x)", "he"));
    }

    [Fact]
    public void Hebrew_LessThanOrEqual()
    {
        Assert.Contains("קטן או שווה ל", MathAriaLabels.ToAriaLabel(@"x \leq 3", "he"));
    }

    [Fact]
    public void Hebrew_Infinity()
    {
        Assert.Contains("אינסוף", MathAriaLabels.ToAriaLabel(@"\infty", "he"));
    }

    // ── Arabic ────────────────────────────────────────────────────────────

    [Fact]
    public void Arabic_FractionUsesArabicTerms()
    {
        var label = MathAriaLabels.ToAriaLabel(@"\frac{x+1}{2}", "ar");
        Assert.DoesNotContain("over", label);
        Assert.DoesNotContain("plus", label);
        Assert.DoesNotContain("the fraction", label);
        Assert.Contains("الكسر", label);        // "the fraction"
        Assert.Contains("على", label);          // "over"
        Assert.Contains("زائد", label);         // "plus"
        Assert.Contains("نهاية الكسر", label);  // "end fraction"
    }

    [Fact]
    public void Arabic_Trig_UsesArabSectorAbbreviations()
    {
        // Arab-sector convention: sin=جا, cos=جتا, tan=ظا
        Assert.Contains("جا", MathAriaLabels.ToAriaLabel(@"\sin(x)", "ar"));
        Assert.Contains("جتا", MathAriaLabels.ToAriaLabel(@"\cos(x)", "ar"));
        Assert.Contains("ظا", MathAriaLabels.ToAriaLabel(@"\tan(x)", "ar"));
    }

    [Fact]
    public void Arabic_Sqrt()
    {
        var label = MathAriaLabels.ToAriaLabel(@"\sqrt{25}", "ar");
        Assert.Contains("الجذر التربيعي", label);
        Assert.Contains("25", label);
    }

    [Fact]
    public void Arabic_PlusMinus()
    {
        Assert.Contains("زائد أو ناقص", MathAriaLabels.ToAriaLabel(@"\pm 3", "ar"));
    }

    [Fact]
    public void Arabic_Infinity()
    {
        Assert.Contains("ما لا نهاية", MathAriaLabels.ToAriaLabel(@"\infty", "ar"));
    }

    [Fact]
    public void Arabic_DeltaForPhysics()
    {
        // Physics uses \Delta x (change-in-x) a lot; make sure the uppercase
        // form gets an Arabic label.
        var label = MathAriaLabels.ToAriaLabel(@"\Delta x", "ar");
        Assert.Contains("دلتا كبيرة", label);
    }

    // ── Edge cases ────────────────────────────────────────────────────────

    [Fact]
    public void EmptyOrNull_ReturnsEmpty()
    {
        Assert.Equal("", MathAriaLabels.ToAriaLabel(null, "en"));
        Assert.Equal("", MathAriaLabels.ToAriaLabel("", "en"));
        Assert.Equal("", MathAriaLabels.ToAriaLabel("   ", "en"));
    }

    [Fact]
    public void UnknownLocale_FallsBackToEnglish()
    {
        var en = MathAriaLabels.ToAriaLabel("x+1", "en");
        var zh = MathAriaLabels.ToAriaLabel("x+1", "zh");
        Assert.Equal(en, zh);
    }

    [Fact]
    public void UnknownLaTeXCommand_SpokenVerbatim_NotSilent()
    {
        // If an author writes \customfunction we should still speak something,
        // not drop the token silently.
        var label = MathAriaLabels.ToAriaLabel(@"\customfunc(x)", "en");
        Assert.Contains("customfunc", label);
    }

    [Fact]
    public void MixedFractionWithPower_ProducesStructuredLabel()
    {
        var label = MathAriaLabels.ToAriaLabel(@"\frac{x^2+1}{2x-3}", "en");
        Assert.Contains("the fraction", label);
        Assert.Contains("squared", label);
        Assert.Contains("over", label);
        Assert.Contains("end fraction", label);
        Assert.Contains("minus", label);
    }

    // ── Task-body canonical example ───────────────────────────────────────

    [Fact]
    public void Canonical_FracXPlus1Over2_InHebrew_IsNotEnglish()
    {
        // Task prr-031 DoD: "KaTeX-rendered \frac{x+1}{2} for locale=he →
        // aria-label matches 'חצי של איקס פלוס אחת' (or similar) — test
        // asserts non-English label".
        //
        // We don't lock the exact wording because screen-reader speech rules
        // evolve with user testing. We lock the invariant: the label contains
        // Hebrew math keywords and does NOT contain English ones.
        var label = MathAriaLabels.ToAriaLabel(@"\frac{x+1}{2}", "he");

        Assert.DoesNotContain("over", label);
        Assert.DoesNotContain("plus", label);
        Assert.DoesNotContain("fraction", label);

        Assert.Contains("השבר", label);
        Assert.Contains("חלקי", label);
        Assert.Contains("ועוד", label);
    }

    [Fact]
    public void Canonical_FracXPlus1Over2_InArabic_IsNotEnglish()
    {
        var label = MathAriaLabels.ToAriaLabel(@"\frac{x+1}{2}", "ar");

        Assert.DoesNotContain("over", label);
        Assert.DoesNotContain("plus", label);
        Assert.DoesNotContain("fraction", label);

        Assert.Contains("الكسر", label);
        Assert.Contains("على", label);
        Assert.Contains("زائد", label);
    }
}
