// =============================================================================
// PRR-322f-audit / 2026-04-30 — GenerateVariantsJobStrategy provenance tests
//
// Pins the contract that BuildVariantCreateRequest populates the right
// fields on CreateQuestionRequest. The strategy was previously losing
// provenance four ways (D1-D4 in the audit-generate-variants thread):
//
//   D1. ModelTemperature: null  — discarded by BatchGenerateResponse
//   D2. RawModelOutput:   null  — same
//   D3. PromptText: synthetic "variant-of:{...}" string — supposed to be
//       the actual LLM prompt (AiGenerationState.PromptText), instead held
//       a provenance breadcrumb. Labels-match-data violation.
//   D4. SourceDocId / SourceUrl / SourceFilename / OriginalText: all null
//       — the Bagrut draft IS the source for these variants but
//       QuestionProvenanceState was empty.
//
// These pin-tests fail fast if any of those defects regress.
// =============================================================================

using Cena.Admin.Api.Ingestion;
using Cena.Api.Contracts.Admin.QualityGate;
using Cena.Api.Contracts.Admin.QuestionBank;
using Cena.Infrastructure.Documents;

namespace Cena.Admin.Api.Tests.Ingestion;

public sealed class GenerateVariantsBuildRequestTests
{
    private static AiGeneratedQuestion MakeQuestion(
        string stem = "Solve x^2 = 4",
        string? topic = "algebra.quadratic_equations",
        int bloom = 3,
        float difficulty = 0.5f) =>
        new(
            Stem: stem,
            Options: new[]
            {
                new AiGeneratedOption("A", "x = 2",  IsCorrect: true,  DistractorRationale: null),
                new AiGeneratedOption("B", "x = -2", IsCorrect: false, DistractorRationale: "missing-sign"),
                new AiGeneratedOption("C", "x = 4",  IsCorrect: false, DistractorRationale: "didnt-square-root"),
                new AiGeneratedOption("D", "x = 0",  IsCorrect: false, DistractorRationale: "trivial"),
            },
            Topic: topic,
            BloomsLevel: bloom,
            Difficulty: difficulty,
            Explanation: "Take square root of both sides.");

    private static QualityGateResult MakeGateResult() =>
        new(
            QuestionId: "qg-x",
            Scores: new DimensionScores(80, 80, 80, 80, 80, 80, 80, 80),
            CompositeScore: 80f,
            Decision: GateDecision.AutoApproved,
            Violations: Array.Empty<QualityViolation>(),
            EvaluatedAt: DateTimeOffset.UtcNow);

    private static BatchGenerateResult MakeBatchResult(AiGeneratedQuestion? q = null) =>
        new(
            Question: q ?? MakeQuestion(),
            QualityGate: MakeGateResult(),
            PassedQualityGate: true,
            CasOutcome: "Verified",
            CasEngine: "sympy",
            CasFailureReason: null);

    private static BatchGenerateResponse MakeBatch(
        string promptUsed = "[SOURCE-AS-CREATIVE-SEED]\nIntegrate x dx",
        float temperature = 0.7f,
        string rawOutput = "{\"questions\":[{\"stem\":\"Solve x^2 = 4\",...}]}",
        string modelUsed = "claude-haiku-3-5") =>
        new(
            Success: true,
            Results: new[] { MakeBatchResult() },
            TotalGenerated: 1,
            PassedQualityGate: 1,
            NeedsReview: 0,
            AutoRejected: 0,
            ModelUsed: modelUsed,
            Error: null,
            DroppedForCasFailure: 0,
            CasDropReasons: null,
            PromptUsed: promptUsed,
            TemperatureUsed: temperature,
            RawOutput: rawOutput);

    private static BagrutDraftPayloadDocument MakeDraft(
        string draftId = "draft-math-5u-2026-035582-p3",
        string examCode = "math-5u-2026-035582",
        string pdfId = "pdf-abc-123",
        int page = 3,
        string prompt = "חשב את האינטגרל של x dx") =>
        new()
        {
            Id              = draftId,
            ExamCode        = examCode,
            SourcePdfId     = pdfId,
            SourcePage      = page,
            Prompt          = prompt,
            LatexContent    = @"\int x \,dx",
            ExtractionConfidence = 0.9,
            CreatedAt       = DateTimeOffset.UtcNow,
        };

    private static GenerateVariantsJobPayload MakePayload(
        string draftId = "draft-math-5u-2026-035582-p3") =>
        new(
            DraftId: draftId,
            Count: 5,
            Subject: "math",
            Topic: "calculus.integrals_intro",
            Grade: "12",
            BloomsLevel: 3,
            MinDifficulty: 0.4f,
            MaxDifficulty: 0.7f,
            Language: "he");

    // ------------------------------------------------------------------
    // D1+D2: AI-generation provenance (Temperature + RawOutput) lands on
    // the variant doc instead of being discarded.
    // ------------------------------------------------------------------

    [Fact]
    public void BuildVariantCreateRequest_Records_LLM_Temperature()
    {
        var req = GenerateVariantsJobStrategy.BuildVariantCreateRequest(
            MakeBatchResult(),
            MakeBatch(temperature: 0.42f),
            MakeDraft(),
            MakePayload());

        Assert.Equal(0.42f, req.ModelTemperature);
    }

    [Fact]
    public void BuildVariantCreateRequest_Records_Raw_Model_Output()
    {
        const string raw = "{\"questions\":[...real json from Anthropic...]}";
        var req = GenerateVariantsJobStrategy.BuildVariantCreateRequest(
            MakeBatchResult(),
            MakeBatch(rawOutput: raw),
            MakeDraft(),
            MakePayload());

        Assert.Equal(raw, req.RawModelOutput);
    }

    // ------------------------------------------------------------------
    // D3: PromptText holds the actual LLM prompt, NOT a synthetic
    // "variant-of:{...}" string.
    // ------------------------------------------------------------------

    [Fact]
    public void BuildVariantCreateRequest_PromptText_Is_Real_Prompt_Not_Synthetic_Breadcrumb()
    {
        const string realPrompt = "[SOURCE-AS-CREATIVE-SEED]\nחשב את האינטגרל של x dx\n\nGenerate 5 competency-equivalent...";
        var req = GenerateVariantsJobStrategy.BuildVariantCreateRequest(
            MakeBatchResult(),
            MakeBatch(promptUsed: realPrompt),
            MakeDraft(),
            MakePayload());

        Assert.Equal(realPrompt, req.PromptText);
        // Regression guard: the previous code stuffed a "variant-of:{id}"
        // marker into PromptText. That marker MUST NOT appear in the
        // prompt field — it belongs in source-provenance fields instead.
        Assert.DoesNotContain("variant-of:", req.PromptText, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // D4: source-provenance fields populated correctly from the Bagrut
    // draft (was null/null/null/null pre-audit).
    // ------------------------------------------------------------------

    [Fact]
    public void BuildVariantCreateRequest_SourceDocId_Is_DraftId()
    {
        var req = GenerateVariantsJobStrategy.BuildVariantCreateRequest(
            MakeBatchResult(),
            MakeBatch(),
            MakeDraft(),
            MakePayload(draftId: "draft-math-5u-2026-035582-p3"));

        Assert.Equal("draft-math-5u-2026-035582-p3", req.SourceDocId);
    }

    [Fact]
    public void BuildVariantCreateRequest_SourceUrl_Carries_Pdf_Id_With_Scheme()
    {
        var req = GenerateVariantsJobStrategy.BuildVariantCreateRequest(
            MakeBatchResult(),
            MakeBatch(),
            MakeDraft(pdfId: "pdf-abc-123"),
            MakePayload());

        // Scheme prefix matters for downstream parsing — pick a stable
        // shape and stick to it. "bagrut-pdf:{id}" is the convention.
        Assert.Equal("bagrut-pdf:pdf-abc-123", req.SourceUrl);
    }

    [Fact]
    public void BuildVariantCreateRequest_SourceFilename_Echoes_Exam_And_Page()
    {
        var req = GenerateVariantsJobStrategy.BuildVariantCreateRequest(
            MakeBatchResult(),
            MakeBatch(),
            MakeDraft(examCode: "math-5u-2026-035582", page: 3),
            MakePayload());

        Assert.Equal("math-5u-2026-035582-page3.pdf", req.SourceFilename);
    }

    [Fact]
    public void BuildVariantCreateRequest_OriginalText_Holds_Source_Prompt_For_Replay()
    {
        const string sourcePrompt = "חשב את האינטגרל של x dx";
        var req = GenerateVariantsJobStrategy.BuildVariantCreateRequest(
            MakeBatchResult(),
            MakeBatch(),
            MakeDraft(prompt: sourcePrompt),
            MakePayload());

        Assert.Equal(sourcePrompt, req.OriginalText);
    }

    // ------------------------------------------------------------------
    // Sanity: non-provenance fields still flow through (the audit didn't
    // touch these but a careless refactor in BuildVariantCreateRequest
    // could regress them; pin one as a smoke).
    // ------------------------------------------------------------------

    [Fact]
    public void BuildVariantCreateRequest_Threads_Question_And_Payload_Fields()
    {
        var q = MakeQuestion(stem: "Find dy/dx of y = x^2", topic: "calculus.derivative_rules", bloom: 4, difficulty: 0.6f);
        var req = GenerateVariantsJobStrategy.BuildVariantCreateRequest(
            MakeBatchResult(q),
            MakeBatch(modelUsed: "claude-sonnet-4-6"),
            MakeDraft(),
            MakePayload());

        Assert.Equal("ai-generated",                 req.SourceType);
        Assert.Equal("Find dy/dx of y = x^2",        req.Stem);
        Assert.Equal("calculus.derivative_rules",    req.Topic);
        Assert.Equal(4,                              req.BloomsLevel);
        Assert.Equal(0.6f,                           req.Difficulty);
        Assert.Equal("math",                         req.Subject);
        Assert.Equal("12",                           req.Grade);
        Assert.Equal("he",                           req.Language);
        Assert.Equal("claude-sonnet-4-6",            req.ModelId);
        Assert.Equal(4,                              req.Options.Count);
    }

    // ------------------------------------------------------------------
    // Difficulty clamp safety — the helper does Math.Clamp(..., 0..1) on
    // the underlying decimal. A bug there would let LLMs ship out-of-
    // band difficulty values.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(-0.5f, 0f)]   // negative → 0
    [InlineData(0.0f,  0f)]
    [InlineData(0.5f,  0.5f)]
    [InlineData(1.0f,  1f)]
    [InlineData(2.5f,  1f)]   // > 1 → 1
    public void BuildVariantCreateRequest_Clamps_Difficulty_To_0_1(float input, float expected)
    {
        var q = MakeQuestion(difficulty: input);
        var req = GenerateVariantsJobStrategy.BuildVariantCreateRequest(
            MakeBatchResult(q),
            MakeBatch(),
            MakeDraft(),
            MakePayload());

        Assert.Equal(expected, req.Difficulty);
    }
}
