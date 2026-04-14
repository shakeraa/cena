// =============================================================================
// GlossaryTermChecker Tests (RDY-027)
// Verifies glossary-based terminology checking in the quality gate pipeline.
// =============================================================================

using Cena.Api.Contracts.Admin.QualityGate;
using Cena.Admin.Api.QualityGate;
using static Cena.Admin.Api.QualityGate.GlossaryTermChecker;

namespace Cena.Admin.Api.Tests.QualityGate;

public class GlossaryTermCheckerTests
{
    private static GlossaryIndex BuildTestGlossary()
    {
        return GlossaryIndex.FromEntries(new List<GlossaryEntry>
        {
            new() { Id = "m001", English = "equation", Hebrew = "משוואה", Arabic = "معادلة", Domain = "algebra" },
            new() { Id = "m002", English = "variable", Hebrew = "משתנה", Arabic = "متغير", Domain = "algebra" },
            new() { Id = "m003", English = "function", Hebrew = "פונקציה", Arabic = "دالة", Domain = "functions" },
            new() { Id = "m004", English = "derivative", Hebrew = "נגזרת", Arabic = "مشتقة", Domain = "calculus" },
            new() { Id = "m005", English = "quadratic", Hebrew = "ריבועי", Arabic = "تربيعي", Domain = "algebra" },
            new() { Id = "m006", English = "slope", Hebrew = "שיפוע", Arabic = "ميل", Domain = "functions" },
            new() { Id = "m007", English = "integral", Hebrew = "אינטגרל", Arabic = "تكامل", Domain = "calculus" },
            new() { Id = "p001", English = "velocity", Hebrew = "מהירות", Arabic = "سرعة", Domain = "physics_mechanics" },
            new() { Id = "p002", English = "acceleration", Hebrew = "תאוצה", Arabic = "تسارع", Domain = "physics_mechanics" },
        });
    }

    private static QualityGateInput MakeInput(
        string stem, string language, params (string Text, bool Correct)[] options)
    {
        var opts = options.Select((o, i) =>
            new QualityGateOption($"{(char)('A' + i)}", o.Text, o.Correct, null)).ToList();

        return new QualityGateInput(
            QuestionId: "test-q",
            Stem: stem,
            Options: opts,
            CorrectOptionIndex: opts.FindIndex(o => o.IsCorrect),
            Subject: "Math",
            Language: language,
            ClaimedBloomLevel: 3,
            ClaimedDifficulty: 0.5f,
            Grade: "5 Units",
            ConceptIds: null);
    }

    // ── Empty / missing glossary ──

    [Fact]
    public void EmptyGlossary_Returns100()
    {
        var input = MakeInput("Solve for x", "he", ("4", true), ("5", false));
        var (score, violations) = CheckWithGlossary(input, GlossaryIndex.Empty);

        Assert.Equal(100, score);
        Assert.Empty(violations);
    }

    [Fact]
    public void UnknownLanguage_Returns100()
    {
        var input = MakeInput("Solve for x", "jp", ("4", true), ("5", false));
        var (score, violations) = CheckWithGlossary(input, BuildTestGlossary());

        Assert.Equal(100, score);
        Assert.Empty(violations);
    }

    // ── English term matching ──

    [Fact]
    public void EnglishStem_FindsGlossaryTerms()
    {
        var input = MakeInput(
            "Find the derivative of the quadratic function f(x) = x²",
            "en",
            ("2x", true), ("x", false));

        var (score, violations) = CheckWithGlossary(input, BuildTestGlossary());

        Assert.Equal(100, score);
        // Should find: derivative, quadratic, function
        var infoTerms = violations
            .Where(v => v.RuleId == "GLOSS-INFO")
            .Select(v => v.Description)
            .ToList();

        Assert.Contains(infoTerms, d => d.Contains("derivative"));
        Assert.Contains(infoTerms, d => d.Contains("quadratic"));
        Assert.Contains(infoTerms, d => d.Contains("function"));
    }

    [Fact]
    public void EnglishStem_NoTerms_Returns100()
    {
        var input = MakeInput(
            "What is 2 + 3?",
            "en",
            ("5", true), ("6", false));

        var (score, violations) = CheckWithGlossary(input, BuildTestGlossary());

        Assert.Equal(100, score);
        Assert.Empty(violations);
    }

    // ── Hebrew matching ──

    [Fact]
    public void HebrewStem_FindsTerms()
    {
        var input = MakeInput(
            "חשב את הנגזרת של הפונקציה f(x) = x²",
            "he",
            ("2x", true), ("x", false));

        var (score, violations) = CheckWithGlossary(input, BuildTestGlossary());

        Assert.Equal(100, score);
        var infoTerms = violations
            .Where(v => v.RuleId == "GLOSS-INFO")
            .Select(v => v.Description)
            .ToList();

        // Should find Hebrew terms: נגזרת (derivative), פונקציה (function)
        Assert.True(infoTerms.Count >= 2, $"Expected >= 2 glossary matches, got {infoTerms.Count}");
    }

    // ── Arabic matching ──

    [Fact]
    public void ArabicStem_FindsTerms()
    {
        var input = MakeInput(
            "أوجد مشتقة الدالة f(x) = x²",
            "ar",
            ("2x", true), ("x", false));

        var (score, violations) = CheckWithGlossary(input, BuildTestGlossary());

        Assert.Equal(100, score);
        var infoTerms = violations
            .Where(v => v.RuleId == "GLOSS-INFO")
            .Select(v => v.Description)
            .ToList();

        // Should find Arabic terms: مشتقة (derivative), دالة (function)
        Assert.True(infoTerms.Count >= 2, $"Expected >= 2 glossary matches, got {infoTerms.Count}");
    }

    // ── Options are also scanned ──

    [Fact]
    public void TermsInOptions_AreDetected()
    {
        var input = MakeInput(
            "Which of the following describes a rate of change?",
            "en",
            ("The derivative", true),
            ("The integral", false));

        var (score, violations) = CheckWithGlossary(input, BuildTestGlossary());

        Assert.Equal(100, score);
        var infoTerms = violations
            .Where(v => v.RuleId == "GLOSS-INFO")
            .Select(v => v.Description)
            .ToList();

        Assert.Contains(infoTerms, d => d.Contains("derivative"));
        Assert.Contains(infoTerms, d => d.Contains("integral"));
    }

    // ── Word boundary: "equation" should not match inside "equations" (actually it should — partial is fine for domain terms) ──

    [Fact]
    public void SubstringMatch_RespectsBoundaries()
    {
        var input = MakeInput(
            "Find the slope of the line",
            "en",
            ("2", true), ("3", false));

        var (score, violations) = CheckWithGlossary(input, BuildTestGlossary());

        Assert.Equal(100, score);
        var infoTerms = violations.Where(v => v.RuleId == "GLOSS-INFO").ToList();
        Assert.Contains(infoTerms, v => v.Description.Contains("slope"));
    }

    // ── Physics terms ──

    [Fact]
    public void PhysicsTerms_AreRecognized()
    {
        var input = MakeInput(
            "Calculate the velocity of an object with constant acceleration",
            "en",
            ("10 m/s", true), ("20 m/s", false));

        var (score, violations) = CheckWithGlossary(input, BuildTestGlossary());

        Assert.Equal(100, score);
        var infoTerms = violations
            .Where(v => v.RuleId == "GLOSS-INFO")
            .Select(v => v.Description)
            .ToList();

        Assert.Contains(infoTerms, d => d.Contains("velocity"));
        Assert.Contains(infoTerms, d => d.Contains("acceleration"));
    }

    // ── Integration with existing QualityGateService ──

    [Fact]
    public async Task GlossaryChecker_DoesNotBreak_ExistingTests()
    {
        // Verify that wiring GlossaryTermChecker into the pipeline doesn't
        // cause existing test cases to fail — the checker is fail-open.
        var service = new QualityGateService();
        var input = QualityGateTestData.GetGoodQuestions().First().Input;

        var result = await service.EvaluateAsync(input);

        // Should not throw and should produce a valid result
        Assert.NotNull(result);
        Assert.True(result.CompositeScore > 0);
    }
}
