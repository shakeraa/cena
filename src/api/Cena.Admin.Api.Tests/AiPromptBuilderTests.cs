// =============================================================================
// Cena Platform — AiPromptBuilder unit tests (PRR-304)
//
// Pin the prompt-shaping invariants that AiPromptBuilder.BuildPrompt was
// extracted from AiGenerationService.cs for. The extract is behaviour-
// preserving by inspection; these tests pin it so future edits cannot
// silently regress on:
//
//   • required header sections (count + Bloom + difficulty + language)
//   • the [SOURCE-AS-CREATIVE-SEED] guardrail emission (ADR-0059 §15.5)
//     — must add the do-not-copy block AND strip the marker from the body
//   • difficulty band rendering: single-point vs range
//   • optional sections (Topic, Context, Image, StyleContext, StyleImage)
//   • the tool-use trailer
//
// These are pure-function tests; no DI, no I/O, no provider mocks.
// =============================================================================

using Cena.Admin.Api;
using Xunit;

namespace Cena.Admin.Api.Tests;

public class AiPromptBuilderTests
{
    private static AiProviderConfig DefaultConfig => new(
        Provider: AiProvider.Anthropic,
        ApiKey: "test-key",
        ModelId: "claude-test",
        Temperature: 0.7f,
        BaseUrl: null,
        ApiVersion: null,
        IsEnabled: true);

    private static AiGenerateRequest BaselineRequest(
        string? context = null,
        string? topic = null,
        float minDiff = 0.5f,
        float maxDiff = 0.5f,
        string lang = "en",
        string? imageBase64 = null,
        string? styleContext = null,
        string? styleImageBase64 = null) =>
        new(
            Subject: "Math",
            Topic: topic,
            Grade: "11",
            BloomsLevel: 3,
            MinDifficulty: minDiff,
            MaxDifficulty: maxDiff,
            Language: lang,
            Context: context,
            ImageBase64: imageBase64,
            FileName: null,
            StyleContext: styleContext,
            StyleImageBase64: styleImageBase64,
            StyleFileName: null,
            Count: 5);

    [Fact]
    public void Header_includes_count_subject_grade_bloom_and_language()
    {
        var prompt = AiPromptBuilder.BuildPrompt(BaselineRequest(topic: "Algebra"), DefaultConfig);

        Assert.Contains("Generate 5 multiple-choice question(s)", prompt);
        Assert.Contains("- Subject: Math", prompt);
        Assert.Contains("- Topic: Algebra", prompt);
        Assert.Contains("- Grade/Level: 11", prompt);
        Assert.Contains("- Bloom's Taxonomy Level: 3 (Apply)", prompt);
        Assert.Contains("- Language: English", prompt);
    }

    [Fact]
    public void Topic_line_omitted_when_topic_null_or_empty()
    {
        var promptNull = AiPromptBuilder.BuildPrompt(BaselineRequest(topic: null), DefaultConfig);
        var promptEmpty = AiPromptBuilder.BuildPrompt(BaselineRequest(topic: ""), DefaultConfig);

        Assert.DoesNotContain("- Topic:", promptNull);
        Assert.DoesNotContain("- Topic:", promptEmpty);
    }

    [Theory]
    [InlineData(1, "Remember")]
    [InlineData(2, "Understand")]
    [InlineData(3, "Apply")]
    [InlineData(4, "Analyze")]
    [InlineData(5, "Evaluate")]
    [InlineData(6, "Create")]
    [InlineData(0, "Unknown")]
    [InlineData(7, "Unknown")]
    public void Bloom_label_maps_levels_correctly(int level, string expectedLabel)
    {
        var req = BaselineRequest() with { BloomsLevel = level };
        var prompt = AiPromptBuilder.BuildPrompt(req, DefaultConfig);

        Assert.Contains($"Bloom's Taxonomy Level: {level} ({expectedLabel})", prompt);
    }

    [Theory]
    [InlineData("en", "English")]
    [InlineData("he", "Hebrew")]
    [InlineData("ar", "Arabic")]
    [InlineData("fr", "fr")]      // unknown code falls through verbatim
    public void Language_label_maps_language_codes(string code, string expectedLabel)
    {
        var prompt = AiPromptBuilder.BuildPrompt(BaselineRequest(lang: code), DefaultConfig);
        Assert.Contains($"- Language: {expectedLabel}", prompt);
    }

    [Fact]
    public void Difficulty_renders_single_target_when_min_equals_max()
    {
        var prompt = AiPromptBuilder.BuildPrompt(
            BaselineRequest(minDiff: 0.55f, maxDiff: 0.55f), DefaultConfig);

        Assert.Contains("- Target Difficulty: 0.55 (0=easy, 1=hard)", prompt);
        Assert.DoesNotContain("- Difficulty Range:", prompt);
        Assert.DoesNotContain("Distribute the", prompt);
    }

    [Fact]
    public void Difficulty_renders_range_with_distribute_hint_when_band_is_wide()
    {
        var prompt = AiPromptBuilder.BuildPrompt(
            BaselineRequest(minDiff: 0.30f, maxDiff: 0.70f), DefaultConfig);

        Assert.Contains("- Difficulty Range: 0.30 to 0.70 (0=easy, 1=hard)", prompt);
        Assert.Contains("Distribute the 5 question(s) evenly", prompt);
        Assert.DoesNotContain("- Target Difficulty:", prompt);
    }

    [Fact]
    public void Difficulty_treats_sub_001_delta_as_single_target()
    {
        // ADR-0059 §15.5: tiny floating drift on identical bands shouldn't
        // accidentally flip the prompt into "range with distribute" wording.
        var prompt = AiPromptBuilder.BuildPrompt(
            BaselineRequest(minDiff: 0.500f, maxDiff: 0.5009f), DefaultConfig);

        Assert.Contains("- Target Difficulty:", prompt);
        Assert.DoesNotContain("- Difficulty Range:", prompt);
    }

    [Fact]
    public void Requirements_block_always_emitted_with_curriculum_alignment()
    {
        var prompt = AiPromptBuilder.BuildPrompt(BaselineRequest(), DefaultConfig);

        Assert.Contains("Requirements:", prompt);
        Assert.Contains("exactly 4 options (A, B, C, D)", prompt);
        Assert.Contains("Exactly one correct answer", prompt);
        Assert.Contains("misconception (provide rationale)", prompt);
        Assert.Contains("Bagrut curriculum standards", prompt);
        Assert.Contains("Hebrew/Arabic student populations", prompt);
    }

    [Fact]
    public void Context_renders_as_basis_when_no_seed_marker()
    {
        var ctx = "Past paper excerpt: a quadratic in x.";
        var prompt = AiPromptBuilder.BuildPrompt(BaselineRequest(context: ctx), DefaultConfig);

        Assert.Contains("Context/Source material (use this as the basis", prompt);
        Assert.Contains(ctx, prompt);
        Assert.DoesNotContain("CREATIVE SEED", prompt);
        Assert.DoesNotContain("[SOURCE-AS-CREATIVE-SEED]", prompt);
    }

    [Fact]
    public void Context_emits_creative_seed_guardrail_when_marker_present()
    {
        // ADR-0059 §15.5: the creative-seed marker MUST trigger the do-not-copy
        // guardrail block AND the marker itself must be stripped from the body
        // the LLM sees.
        const string body = "Find the roots of x^2 + 5x + 6 = 0.";
        var ctx = "[SOURCE-AS-CREATIVE-SEED] " + body;

        var prompt = AiPromptBuilder.BuildPrompt(BaselineRequest(context: ctx), DefaultConfig);

        Assert.Contains("CREATIVE SEED", prompt);
        Assert.Contains("Test the SAME skill", prompt);
        Assert.Contains("DIFFERENT scenario, DIFFERENT numbers", prompt);
        Assert.Contains("DO NOT reuse the source wording verbatim", prompt);
        Assert.Contains("DO NOT copy figure/diagram captions verbatim", prompt);
        Assert.Contains("Source (do not copy):", prompt);
        Assert.Contains(body, prompt);

        // Marker stripped from the body the LLM sees.
        Assert.DoesNotContain("[SOURCE-AS-CREATIVE-SEED]", prompt);
        // Non-seed wording is suppressed.
        Assert.DoesNotContain("Context/Source material (use this as the basis", prompt);
    }

    [Fact]
    public void Image_attachment_line_emitted_when_image_present()
    {
        var prompt = AiPromptBuilder.BuildPrompt(
            BaselineRequest(imageBase64: "deadbeef"), DefaultConfig);

        Assert.Contains("A question/source image has been attached.", prompt);
    }

    [Fact]
    public void Image_attachment_line_omitted_when_image_null()
    {
        var prompt = AiPromptBuilder.BuildPrompt(BaselineRequest(), DefaultConfig);
        Assert.DoesNotContain("A question/source image has been attached.", prompt);
    }

    [Fact]
    public void Style_block_renders_text_when_style_context_present()
    {
        var prompt = AiPromptBuilder.BuildPrompt(
            BaselineRequest(styleContext: "Match terse Ministry phrasing."), DefaultConfig);

        Assert.Contains("STYLE REFERENCE", prompt);
        Assert.Contains("Match terse Ministry phrasing.", prompt);
    }

    [Fact]
    public void Style_block_renders_image_attachment_when_style_image_present()
    {
        var prompt = AiPromptBuilder.BuildPrompt(
            BaselineRequest(styleImageBase64: "cafebabe"), DefaultConfig);

        Assert.Contains("STYLE REFERENCE", prompt);
        Assert.Contains("style reference image has been attached", prompt);
    }

    [Fact]
    public void Style_block_omitted_when_neither_style_field_present()
    {
        var prompt = AiPromptBuilder.BuildPrompt(BaselineRequest(), DefaultConfig);
        Assert.DoesNotContain("STYLE REFERENCE", prompt);
    }

    [Fact]
    public void Tool_use_trailer_always_present()
    {
        var prompt = AiPromptBuilder.BuildPrompt(BaselineRequest(), DefaultConfig);
        Assert.Contains("Use the generate_questions tool", prompt);
    }

    [Fact]
    public void Empty_context_string_does_not_emit_basis_block()
    {
        // null/empty parity — both branches should suppress the section.
        var promptEmpty = AiPromptBuilder.BuildPrompt(
            BaselineRequest(context: ""), DefaultConfig);

        Assert.DoesNotContain("Context/Source material", promptEmpty);
        Assert.DoesNotContain("CREATIVE SEED", promptEmpty);
    }
}
