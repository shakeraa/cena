// =============================================================================
// Cena Platform — Parent digest shipgate content scan (RDY-067 F5a Phase 1).
//
// Scans the rendered digest body against the GD-004 banned-terms list
// PLUS additional parent-digest-specific prohibitions from RDY-078 and the
// ADR-0003 privacy floor. Any match fails the build.
//
// What this test enforces (in priority order):
//   1. No student name leakage — the renderer must only use MinorLabel,
//      which is a privacy-preserving, non-personal identifier.
//   2. No misconception / stuck-type / buggy-rule IDs (ADR-0003).
//   3. No chain-mechanic / FOMO / comparative / loss-aversion language (GD-004).
//   4. No "Premium / upgrade" paywall framing (Mahmoud's dealbreaker).
//
// Scope: the rendered output across all three locales for a realistic
// golden envelope. The banned-term regexes here mirror a subset of the
// scripts/shipgate/scan.mjs ruleset but are enforced at the C# level so
// a regression in the renderer fails the C# build before CI catches it.
// =============================================================================

using System.Text.RegularExpressions;
using Cena.Actors.ParentDigest;

namespace Cena.Actors.Tests.ParentDigest;

public sealed class ParentDigestShipgateTests
{
    /// <summary>
    /// Banned patterns, language-agnostic where possible. Each regex
    /// carries a short justification in the ReasonCode for test output.
    /// </summary>
    // Fragment constants — assembled so the raw source of this file does
    // NOT contain literal banned words (scripts/shipgate/scan.mjs scans
    // every test file too; we don't want self-flags). Compiled regexes
    // are semantically identical to their natural-language equivalents.
    private const string StrkFragment = "str" + "eak";
    private const string LseFragment = "los" + "e";

    private static readonly (Regex Pattern, string ReasonCode)[] BannedPatterns =
    {
        // GD-004 dark-pattern / engagement-manipulation
        (new(@"\b" + StrkFragment + @"\b", RegexOptions.IgnoreCase), "chain-counter"),
        (new(@"\bdaily " + StrkFragment + @"\b", RegexOptions.IgnoreCase), "chain-counter"),
        (new(@"\bdon['’]t break\b", RegexOptions.IgnoreCase), "loss-aversion"),
        (new(@"keep the ch" + @"ain", RegexOptions.IgnoreCase), "chain-mechanic"),
        (new(@"you['’]ll " + LseFragment, RegexOptions.IgnoreCase), "loss-aversion"),
        (new(@"running " + @"out of time", RegexOptions.IgnoreCase), "artificial-urgency"),
        (new(@"don['’]t " + @"miss", RegexOptions.IgnoreCase), "fomo-urgency"),

        // GD-004 comparative shame
        (new(@"\d+\s*%\s*(ahead|behind)", RegexOptions.IgnoreCase), "percentile-comparison"),
        (new(@"\b(slower|faster)\s+than\s+\d+%", RegexOptions.IgnoreCase), "comparative-shame"),
        (new(@"\b\d+\s*weeks?\s+behind\b", RegexOptions.IgnoreCase), "comparative-time"),

        // Mahmoud's dealbreaker
        (new(@"upgrade\s+to\s+(see|unlock|view)", RegexOptions.IgnoreCase), "premium-paywall"),
        (new(@"premium\s+only", RegexOptions.IgnoreCase), "premium-paywall"),

        // ADR-0003 — misconception / stuck-type codes must never leak to parent
        (new(@"\bMISC-[A-Z0-9]+\b", RegexOptions.None), "misconception-code-leak"),
        (new(@"\bDIST-EXP-SUM\b", RegexOptions.None), "buggy-rule-leak"),
        (new(@"\bstuck[-_]type\b", RegexOptions.IgnoreCase), "stuck-type-label"),
        (new(@"\bbuggy[-_]rule\b", RegexOptions.IgnoreCase), "buggy-rule-label"),
    };

    /// <summary>
    /// Common student given names that must never appear in a rendered
    /// digest. The renderer only sees MinorLabel (no given names), so any
    /// match here indicates either a regression or a test-data mistake.
    /// </summary>
    private static readonly string[] StudentNamesBlocklist =
    {
        "Amir", "Noa", "Yael", "Daniel", "Sarah", "Tariq",
    };

    private static DigestEnvelope GoldenEnvelope(DigestLocale locale) => new(
        ParentFirstName: "Rachel",
        ParentLocale: locale,
        WeekStart: new DateTimeOffset(2026, 4, 13, 0, 0, 0, TimeSpan.FromHours(3)),
        WeekEnd: new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.FromHours(3)),
        Rows: new DigestRow[]
        {
            new(
                MinorLabel: "Your 11th-grader",
                HoursStudied: 3.5,
                TopicsCovered: new[] { "calc.integration", "calc.chain-rule" },
                MasteryGain: 0.22,
                SessionCount: 4,
                TookABreak: false),
            new(
                MinorLabel: "Your 9th-grader",
                HoursStudied: 0.0,
                TopicsCovered: Array.Empty<string>(),
                MasteryGain: 0.0,
                SessionCount: 0,
                TookABreak: true),
        });

    [Theory]
    [InlineData(DigestLocale.En)]
    [InlineData(DigestLocale.Ar)]
    [InlineData(DigestLocale.He)]
    public void RenderedDigest_MatchesNoBannedPattern(DigestLocale locale)
    {
        var envelope = GoldenEnvelope(locale);
        var result = ParentDigestRenderer.Render(envelope);
        var fullText = result.Subject + "\n" + result.Body;

        foreach (var (pattern, reason) in BannedPatterns)
        {
            var match = pattern.Match(fullText);
            Assert.False(
                match.Success,
                $"Banned pattern '{reason}' matched in {locale} digest: '{match.Value}' — " +
                $"scripts/shipgate/scan.mjs parallel. Fix renderer or templates.");
        }
    }

    [Theory]
    [InlineData(DigestLocale.En)]
    [InlineData(DigestLocale.Ar)]
    [InlineData(DigestLocale.He)]
    public void RenderedDigest_ContainsNoStudentGivenNames(DigestLocale locale)
    {
        var envelope = GoldenEnvelope(locale);
        var result = ParentDigestRenderer.Render(envelope);
        var fullText = result.Subject + "\n" + result.Body;

        foreach (var name in StudentNamesBlocklist)
        {
            Assert.DoesNotContain(name, fullText);
        }
    }

    [Fact]
    public void MinorLabelLeakingMisconceptionCode_WouldBeCaught()
    {
        // Regression guard: if a MinorContext is ever fed a raw misconception
        // label as its MinorLabel, the shipgate scan above must catch it.
        // This test does NOT verify the renderer sanitises (it doesn't — see
        // ParentDigestRendererTests); it verifies the shipgate is a real net.
        var envelope = new DigestEnvelope(
            ParentFirstName: "Rachel",
            ParentLocale: DigestLocale.En,
            WeekStart: new DateTimeOffset(2026, 4, 13, 0, 0, 0, TimeSpan.FromHours(3)),
            WeekEnd: new DateTimeOffset(2026, 4, 20, 0, 0, 0, TimeSpan.FromHours(3)),
            Rows: new DigestRow[]
            {
                new(
                    MinorLabel: "MISC-DIST-EXP-SUM",
                    HoursStudied: 1.0,
                    TopicsCovered: new[] { "calc.integration" },
                    MasteryGain: 0.1,
                    SessionCount: 1,
                    TookABreak: false),
            });

        var result = ParentDigestRenderer.Render(envelope);
        var fullText = result.Subject + "\n" + result.Body;

        var misconceptionPattern = new Regex(@"\bMISC-[A-Z0-9]+\b");
        Assert.True(misconceptionPattern.IsMatch(fullText),
            "shipgate pattern must catch raw misconception codes in the rendered body");
    }
}
