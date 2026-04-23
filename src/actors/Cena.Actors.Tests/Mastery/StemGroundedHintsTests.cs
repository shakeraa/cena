// =============================================================================
// Cena Platform — StemGroundedHintRouter + HintLeakDetector tests (PRR-262)
//
// Two invariants under test:
//   1. In hidden mode, only StemGrounded variant is eligible. Full-variant
//      hints MUST NOT reach the student — the router returns a
//      reveal-required reason instead. (The persona-educator blocker.)
//   2. The leak detector flags the common leak shapes across en/he/ar:
//      option-letter markers + option-content echoes. Not proof-of-safety
//      but catches structural leaks deterministically.
// =============================================================================

using Cena.Actors.Mastery;
using Cena.Infrastructure.Documents;
using Xunit;

namespace Cena.Actors.Tests.Mastery;

public class StemGroundedHintsTests
{
    // ---- Router: empty + null ---------------------------------------------

    [Fact]
    public void Pick_null_hint_list_returns_NoHintsAuthored()
    {
        var r = StemGroundedHintRouter.Pick(
            authoredHints: null,
            requestedLevel: 1,
            locale: "en",
            optionsAreVisibleToStudent: true);
        Assert.Null(r.PickedHint);
        Assert.Equal(StemGroundedHintRouter.NoHintsAuthoredReason, r.ReasonCode);
    }

    [Fact]
    public void Pick_empty_hint_list_returns_NoHintsAuthored()
    {
        var r = StemGroundedHintRouter.Pick(
            authoredHints: Array.Empty<AuthoredHint>(),
            requestedLevel: 1,
            locale: "en",
            optionsAreVisibleToStudent: false);
        Assert.Null(r.PickedHint);
        Assert.Equal(StemGroundedHintRouter.NoHintsAuthoredReason, r.ReasonCode);
    }

    // ---- Router: visible mode honours both variants -----------------------

    [Fact]
    public void Pick_visible_mode_returns_StemGrounded_hint_when_available()
    {
        var hints = new[]
        {
            new AuthoredHint(1, HintVariant.StemGrounded, "Recall the factoring rule.", "en"),
        };
        var r = StemGroundedHintRouter.Pick(hints, 1, "en", optionsAreVisibleToStudent: true);
        Assert.NotNull(r.PickedHint);
        Assert.Equal(HintVariant.StemGrounded, r.PickedHint!.Variant);
        Assert.Null(r.ReasonCode);
    }

    [Fact]
    public void Pick_visible_mode_returns_Full_hint_when_no_StemGrounded_at_level()
    {
        var hints = new[]
        {
            new AuthoredHint(1, HintVariant.Full, "Option A is the factored form.", "en"),
        };
        var r = StemGroundedHintRouter.Pick(hints, 1, "en", optionsAreVisibleToStudent: true);
        Assert.NotNull(r.PickedHint);
        Assert.Equal(HintVariant.Full, r.PickedHint!.Variant);
    }

    // ---- Router: hidden mode NEVER returns Full variant --------------------

    [Fact]
    public void Pick_hidden_mode_rejects_Full_variant_and_returns_RevealRequired()
    {
        // The critical no-leak property. A student in hidden_reveal asks for
        // an L1 hint; only a Full-variant hint exists at L1; the router MUST
        // return RevealRequired, not the Full hint.
        var hints = new[]
        {
            new AuthoredHint(1, HintVariant.Full, "Option A references (x-3)(x+2).", "en"),
        };
        var r = StemGroundedHintRouter.Pick(hints, 1, "en", optionsAreVisibleToStudent: false);
        Assert.Null(r.PickedHint);
        Assert.Equal(StemGroundedHintRouter.RevealRequiredReason, r.ReasonCode);
    }

    [Fact]
    public void Pick_hidden_mode_returns_StemGrounded_when_both_variants_exist()
    {
        var hints = new[]
        {
            new AuthoredHint(1, HintVariant.StemGrounded, "Think about the factoring rule.", "en"),
            new AuthoredHint(1, HintVariant.Full, "Option B is the factored form.", "en"),
        };
        var r = StemGroundedHintRouter.Pick(hints, 1, "en", optionsAreVisibleToStudent: false);
        Assert.NotNull(r.PickedHint);
        Assert.Equal(HintVariant.StemGrounded, r.PickedHint!.Variant);
    }

    [Fact]
    public void Pick_hidden_mode_different_level_StemGrounded_does_not_substitute()
    {
        // Requesting L2 hint; hidden mode; only L1 StemGrounded and L2 Full
        // exist. Router must NOT silently serve L1 just because it's
        // stem-grounded — caller asked for L2. Router must NOT serve L2
        // Full because we're in hidden mode. Outcome: RevealRequired.
        var hints = new[]
        {
            new AuthoredHint(1, HintVariant.StemGrounded, "L1 stem hint.", "en"),
            new AuthoredHint(2, HintVariant.Full, "L2 full hint referencing Option C.", "en"),
        };
        var r = StemGroundedHintRouter.Pick(hints, 2, "en", optionsAreVisibleToStudent: false);
        Assert.Null(r.PickedHint);
        Assert.Equal(StemGroundedHintRouter.RevealRequiredReason, r.ReasonCode);
    }

    [Fact]
    public void Pick_visible_mode_at_missing_level_returns_NoHintsAuthored()
    {
        var hints = new[]
        {
            new AuthoredHint(1, HintVariant.StemGrounded, "L1.", "en"),
        };
        var r = StemGroundedHintRouter.Pick(hints, 2, "en", optionsAreVisibleToStudent: true);
        Assert.Null(r.PickedHint);
        Assert.Equal(StemGroundedHintRouter.NoHintsAuthoredReason, r.ReasonCode);
    }

    // ---- Router: locale preference -----------------------------------------

    [Fact]
    public void Pick_prefers_requested_locale_when_available()
    {
        var hints = new[]
        {
            new AuthoredHint(1, HintVariant.StemGrounded, "English L1.", "en"),
            new AuthoredHint(1, HintVariant.StemGrounded, "רמז בעברית.", "he"),
            new AuthoredHint(1, HintVariant.StemGrounded, "تلميح بالعربية.", "ar"),
        };
        var r = StemGroundedHintRouter.Pick(hints, 1, "he", optionsAreVisibleToStudent: false);
        Assert.NotNull(r.PickedHint);
        Assert.Equal("he", r.PickedHint!.Locale);
    }

    [Fact]
    public void Pick_falls_back_to_English_when_preferred_locale_missing()
    {
        var hints = new[]
        {
            new AuthoredHint(1, HintVariant.StemGrounded, "English L1.", "en"),
        };
        var r = StemGroundedHintRouter.Pick(hints, 1, "he", optionsAreVisibleToStudent: false);
        Assert.NotNull(r.PickedHint);
        Assert.Equal("en", r.PickedHint!.Locale);
    }

    [Fact]
    public void Pick_falls_back_to_any_locale_when_English_also_missing()
    {
        var hints = new[]
        {
            new AuthoredHint(1, HintVariant.StemGrounded, "רמז בעברית.", "he"),
        };
        var r = StemGroundedHintRouter.Pick(hints, 1, "ar", optionsAreVisibleToStudent: false);
        Assert.NotNull(r.PickedHint);
        Assert.Equal("he", r.PickedHint!.Locale);
    }

    [Fact]
    public void Pick_locale_matching_is_case_insensitive()
    {
        var hints = new[]
        {
            new AuthoredHint(1, HintVariant.StemGrounded, "HE", "HE"),
        };
        var r = StemGroundedHintRouter.Pick(hints, 1, "he", optionsAreVisibleToStudent: false);
        Assert.NotNull(r.PickedHint);
    }

    // ---- Leak detector -----------------------------------------------------

    [Theory]
    [InlineData("Option A is correct because...")]
    [InlineData("The answer B follows from...")]
    [InlineData("Choice C is the right move.")]
    [InlineData("option (D) — correct.")]
    public void LeakDetector_flags_English_option_letter_markers(string hint)
    {
        var result = HintLeakDetector.Detect(hint, optionTexts: null);
        Assert.True(result.HasLeak, $"Expected leak flag for: {hint}");
    }

    [Fact]
    public void LeakDetector_flags_Hebrew_option_letter_marker()
    {
        var result = HintLeakDetector.Detect("אפשרות א היא הנכונה.", optionTexts: null);
        Assert.True(result.HasLeak);
    }

    [Fact]
    public void LeakDetector_flags_Arabic_option_letter_marker()
    {
        var result = HintLeakDetector.Detect("الخيار أ هو الصحيح.", optionTexts: null);
        Assert.True(result.HasLeak);
    }

    [Fact]
    public void LeakDetector_flags_option_content_echo()
    {
        var options = new[] { "(x-3)(x+2)", "x²-x-6", "(x+3)(x-2)", "x²+x-6" };
        // Hint echoes the content of option index 1 verbatim (short option
        // caught by the whole-option rule, length-independent).
        var result = HintLeakDetector.Detect(
            "Recall: the expanded form x²-x-6 comes from...",
            optionTexts: options);
        Assert.True(result.HasLeak);
        Assert.Contains(result.LeakReasons,
            r => r.StartsWith("option_whole_match:") || r.StartsWith("option_content_echo:"));
    }

    [Fact]
    public void LeakDetector_flags_long_substring_partial_paraphrase()
    {
        // Option is long enough (>= MinEchoLength). Hint contains a
        // non-whole-option substring ≥ MinEchoLength chars. Caught by 3b.
        var options = new[] { "the factored form of the quadratic polynomial" };
        var result = HintLeakDetector.Detect(
            "Remember: a quadratic polynomial splits into two linear factors.",
            optionTexts: options);
        Assert.True(result.HasLeak);
    }

    [Fact]
    public void LeakDetector_does_not_flag_short_coincidental_substrings()
    {
        var options = new[] { "x-3", "x+2" }; // short (< MinEchoLength)
        var result = HintLeakDetector.Detect(
            "Factor the trinomial by splitting the linear term.",
            optionTexts: options);
        Assert.False(result.HasLeak);
    }

    [Fact]
    public void LeakDetector_passes_clean_stem_grounded_hint()
    {
        var options = new[] { "(x-3)(x+2)", "x²-x-6", "(x+3)(x-2)", "x²+x-6" };
        var result = HintLeakDetector.Detect(
            "Recall that a trinomial x² + bx + c factors when you find two numbers that multiply to c and add to b.",
            optionTexts: options);
        Assert.False(result.HasLeak);
    }

    [Fact]
    public void LeakDetector_empty_hint_reports_no_leak()
    {
        var result = HintLeakDetector.Detect("", optionTexts: null);
        Assert.False(result.HasLeak);
    }

    [Fact]
    public void LeakDetector_whitespace_hint_reports_no_leak()
    {
        var result = HintLeakDetector.Detect("   ", optionTexts: null);
        Assert.False(result.HasLeak);
    }

    [Fact]
    public void LeakDetector_case_insensitive_English_marker_match()
    {
        var result = HintLeakDetector.Detect("OPTION A is clearly the answer.", optionTexts: null);
        Assert.True(result.HasLeak);
    }

    [Fact]
    public void LeakDetector_multiple_leaks_listed()
    {
        var options = new[] { "the factored form 2x+6" };
        var result = HintLeakDetector.Detect(
            "Option A is the factored form 2x+6.",
            optionTexts: options);
        // Should flag both english_option_letter:A AND option_whole_match:index_0.
        Assert.True(result.LeakReasons.Count >= 2);
    }

    // ---- QuestionDocument integration --------------------------------------

    [Fact]
    public void QuestionDocument_AuthoredHints_defaults_null_for_legacy_items()
    {
        // Forward-compat: existing seeded QuestionDocument items without
        // an AuthoredHints field deserialise to null. Router treats null
        // as "no hints authored" without crashing.
        var doc = new QuestionDocument { Id = "q-legacy" };
        Assert.Null(doc.AuthoredHints);

        var r = StemGroundedHintRouter.Pick(
            doc.AuthoredHints,
            requestedLevel: 1,
            locale: "en",
            optionsAreVisibleToStudent: false);
        Assert.Null(r.PickedHint);
        Assert.Equal(StemGroundedHintRouter.NoHintsAuthoredReason, r.ReasonCode);
    }
}
