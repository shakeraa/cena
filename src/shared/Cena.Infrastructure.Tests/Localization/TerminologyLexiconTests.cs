// =============================================================================
// RDY-068b: TerminologyLexicon parser tests
//
// Proves the loader round-trips the DRAFT → PROF_AMJAD_REVIEW → LOCKED
// lifecycle correctly and handles the exact Markdown shape of
// docs/content/arabic-math-lexicon.md.
// =============================================================================

using Cena.Infrastructure.Localization;
using Xunit;

namespace Cena.Infrastructure.Tests.Localization;

public class TerminologyLexiconTests
{
    private const string SampleDoc = """
# Arabic Math Lexicon — Levantine Register

Preamble markdown.

### 1. Algebra — foundations

| Arabic | Hebrew | English | Status | Notes |
|--------|--------|---------|--------|-------|
| متغير | משתנה | variable | DRAFT | Standard across registers |
| متعدد الحدود | פולינום | polynomial | LOCKED | Levantine-preferred |
| حد (pl. حدود) | איבר (pl. איברים) | term (in a polynomial) | PROF_AMJAD_REVIEW | plural shift |

### 2. Functions

| Arabic | Hebrew | English | Status | Notes |
|--------|--------|---------|--------|-------|
| دالة | פונקציה | function | DRAFT | |
| نقطة تقاطع | נקודת חיתוך | intersection point | LOCKED | |
""";

    private static TerminologyLexicon Load() =>
        TerminologyLexicon.LoadFromLines(SampleDoc.Split('\n'));

    [Fact]
    public void Parses_all_five_terms()
    {
        var lex = Load();
        Assert.Equal(5, lex.AllTerms.Count);
    }

    [Theory]
    [InlineData("variable", TerminologyStatus.Draft, "Algebra — foundations")]
    [InlineData("polynomial", TerminologyStatus.Locked, "Algebra — foundations")]
    [InlineData("term", TerminologyStatus.ProfAmjadReview, "Algebra — foundations")]
    [InlineData("function", TerminologyStatus.Draft, "Functions")]
    [InlineData("intersection point", TerminologyStatus.Locked, "Functions")]
    public void Status_and_topic_family_parse_correctly(
        string english, TerminologyStatus expected, string expectedFamily)
    {
        var lex = Load();
        var t = lex.GetByEnglish(english);
        Assert.NotNull(t);
        Assert.Equal(expected, t!.Status);
        Assert.Equal(expectedFamily, t.TopicFamily);
    }

    [Fact]
    public void English_lookup_is_case_insensitive()
    {
        var lex = Load();
        Assert.NotNull(lex.GetByEnglish("POLYNOMIAL"));
        Assert.NotNull(lex.GetByEnglish("Polynomial"));
        Assert.NotNull(lex.GetByEnglish("polynomial"));
    }

    [Fact]
    public void English_lookup_strips_parenthetical_disambiguator()
    {
        // Row says: `term (in a polynomial)`; GetByEnglish("term") hits it.
        var lex = Load();
        Assert.NotNull(lex.GetByEnglish("term"));
    }

    [Fact]
    public void With_status_filters()
    {
        var lex = Load();
        Assert.Equal(2, lex.WithStatus(TerminologyStatus.Draft).Count());
        Assert.Equal(1, lex.WithStatus(TerminologyStatus.ProfAmjadReview).Count());
        Assert.Equal(2, lex.WithStatus(TerminologyStatus.Locked).Count());
    }

    [Fact]
    public void Is_fully_locked_is_false_when_any_term_is_not_locked()
    {
        var lex = Load();
        Assert.False(lex.IsFullyLocked);
    }

    [Fact]
    public void Is_fully_locked_is_true_when_every_term_is_locked()
    {
        var lex = TerminologyLexicon.LoadFromLines("""
### 1. Algebra

| Arabic | Hebrew | English | Status | Notes |
|--------|--------|---------|--------|-------|
| متغير | משתנה | variable | LOCKED | |
""".Split('\n'));
        Assert.True(lex.IsFullyLocked);
    }

    [Fact]
    public void Missing_keys_return_null()
    {
        var lex = Load();
        Assert.Null(lex.GetByEnglish("eigenvector"));
        Assert.Null(lex.GetByEnglish(""));
        Assert.Null(lex.GetByEnglish(null!));
    }

    [Fact]
    public void Header_row_and_separator_row_are_not_parsed_as_terms()
    {
        var lex = Load();
        // `Status` is the header text; `--------` is the separator.
        // Neither should appear as a term.
        Assert.DoesNotContain(lex.AllTerms, t => t.English.Equals("English", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lex.AllTerms, t => t.English.Contains("---"));
    }

    [Fact]
    public void Canonical_lexicon_file_loads_without_throwing()
    {
        // Guard against structural drift in the real doc.
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..", "..",
            "docs", "content", "arabic-math-lexicon.md");
        path = Path.GetFullPath(path);
        if (!File.Exists(path))
        {
            // Test may run with a different working dir in CI; skip
            // gracefully rather than fail with a path-based false negative.
            return;
        }

        var lex = TerminologyLexicon.LoadFromFile(path);
        // We expect > 20 terms in the current doc; bump if that changes.
        Assert.True(lex.AllTerms.Count >= 20,
            $"Canonical lexicon parsed only {lex.AllTerms.Count} terms; "
            + "the parser may have regressed.");
    }

    [Fact]
    public void LoadFromFile_throws_for_missing_path()
    {
        Assert.Throws<FileNotFoundException>(() =>
            TerminologyLexicon.LoadFromFile("/nonexistent/lexicon.md"));
    }
}
