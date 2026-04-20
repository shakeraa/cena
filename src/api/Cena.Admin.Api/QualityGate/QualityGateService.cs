// =============================================================================
// Cena Platform -- Quality Gate Service
// Orchestrates Stage 1 (structural) + Stage 2 (LLM-based) scoring.
// Stage 2 uses Anthropic Haiku (cheaper assessment model) for FactualAccuracy, LanguageQuality,
// and PedagogicalQuality dimensions. Falls back to defaults if LLM unavailable.
// =============================================================================

using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using Anthropic.Models.Messages;
using Cena.Infrastructure.Documents;
using Cena.Infrastructure.Llm;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.QualityGate;

public interface IQualityGateService
{
    /// <summary>
    /// Evaluate a question through the quality gate pipeline.
    /// Stage 1: structural validation (sync). Stage 2: LLM-based scoring (async).
    /// </summary>
    Task<QualityGateResult> EvaluateAsync(QualityGateInput input);
}

// ADR-0045: 0-100 rubric scoring across FactualAccuracy / LanguageQuality /
// PedagogicalQuality via Haiku tool-use (temp=0.1, 1024 tokens). Deliberately
// pinned to tier 2 — this evaluator runs on every AI-generated question and
// must not drift to Sonnet cost. New routing row:
// contracts/llm/routing-config.yaml §task_routing.quality_gate.
[TaskRouting("tier2", "quality_gate")]
public sealed class QualityGateService : IQualityGateService
{
    private readonly QualityGateThresholds _thresholds;
    private readonly IConfiguration? _configuration;
    private readonly ILogger<QualityGateService>? _logger;
    // RDY-034 §13: when present, the gate sources FactualAccuracy from the
    // persisted QuestionCasBinding for math/physics subjects. Optional so
    // existing seed paths and unit tests that don't have Marten still work.
    private readonly IDocumentStore? _store;

    // Lazily created Anthropic client for LLM-based scoring
    private AnthropicClient? _client;
    private string? _lastApiKey;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public QualityGateService(
        QualityGateThresholds? thresholds = null,
        IConfiguration? configuration = null,
        ILogger<QualityGateService>? logger = null,
        IDocumentStore? store = null)
    {
        _thresholds = thresholds ?? QualityGateThresholds.Default;
        _configuration = configuration;
        _logger = logger;
        _store = store;
    }

    public async Task<QualityGateResult> EvaluateAsync(QualityGateInput input)
    {
        var allViolations = new List<QualityViolation>();

        // Stage 1: Structural Validation
        var (structuralScore, structuralViolations) = StructuralValidator.Validate(input);
        allViolations.AddRange(structuralViolations);

        // Stage 1: Stem Clarity
        var (stemScore, stemViolations) = StemClarityScorer.Score(input);
        allViolations.AddRange(stemViolations);

        // Stage 1: Distractor Quality
        var (distractorScore, distractorViolations) = DistractorQualityScorer.Score(input);
        allViolations.AddRange(distractorViolations);

        // Stage 1: Bloom's Alignment
        var (bloomScore, bloomViolations) = BloomAlignmentScorer.Score(input);
        allViolations.AddRange(bloomViolations);

        // Stage 1: Glossary Term Check (RDY-027)
        var (glossaryScore, glossaryViolations) = GlossaryTermChecker.Check(input);
        allViolations.AddRange(glossaryViolations);

        // Stage 2: LLM-based scoring for FactualAccuracy, LanguageQuality, PedagogicalQuality
        var (llmFactualAccuracy, languageQuality, pedagogicalQuality) =
            await EvaluateWithLlmAsync(input);

        // RDY-034 §13: For math/physics subjects, the CAS binding is the
        // authoritative source for FactualAccuracy. Heuristic LLM scoring is
        // ignored — we don't want a chatty LLM to overrule the oracle.
        //   - Verified                → 100
        //   - OverriddenByOperator    → 90 (intentional human override; flagged)
        //   - Unverifiable            → fall back to LLM score
        //   - Failed                  → 0  (forces AutoRejected)
        //   - Missing binding entirely → fall back to LLM score (legacy data)
        int factualAccuracy = llmFactualAccuracy;
        var binding = await TryLoadBindingAsync(input.QuestionId);
        if (IsMathOrPhysicsSubject(input.Subject) && binding is not null)
        {
            factualAccuracy = binding.Status switch
            {
                CasBindingStatus.Verified => 100,
                CasBindingStatus.OverriddenByOperator => 90,
                CasBindingStatus.Failed => 0,
                CasBindingStatus.Unverifiable => llmFactualAccuracy,
                _ => llmFactualAccuracy
            };
            _logger?.LogDebug(
                "[QG_FACTUAL_FROM_BINDING] questionId={Qid} bindingStatus={Status} factual={Score}",
                input.QuestionId, binding.Status, factualAccuracy);
        }

        // Blend glossary coverage into LanguageQuality (30% glossary, 70% LLM)
        // This rewards questions that use canonical terminology from the glossary.
        int blendedLanguageQuality = (int)Math.Round(0.70 * languageQuality + 0.30 * glossaryScore);
        blendedLanguageQuality = Math.Clamp(blendedLanguageQuality, 0, 100);

        int culturalSensitivity = 80; // Default until dedicated cultural sensitivity LLM stage

        var scores = new DimensionScores(
            FactualAccuracy: factualAccuracy,
            LanguageQuality: blendedLanguageQuality,
            PedagogicalQuality: pedagogicalQuality,
            DistractorQuality: distractorScore,
            StemClarity: stemScore,
            BloomAlignment: bloomScore,
            StructuralValidity: structuralScore,
            CulturalSensitivity: culturalSensitivity);

        float composite = ComputeComposite(scores);
        var decision = DetermineDecision(scores, composite);

        return new QualityGateResult(
            QuestionId: input.QuestionId,
            Scores: scores,
            CompositeScore: composite,
            Decision: decision,
            Violations: allViolations,
            EvaluatedAt: DateTimeOffset.UtcNow);
    }

    // ── Stage 2: LLM-Based Scoring ──

    private async Task<(int FactualAccuracy, int LanguageQuality, int PedagogicalQuality)>
        EvaluateWithLlmAsync(QualityGateInput input)
    {
        var apiKey = _configuration?["Anthropic:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger?.LogDebug("No Anthropic API key configured; using default quality gate scores");
            return (80, 80, 75);
        }

        try
        {
            var client = GetOrCreateClient(apiKey);

            var questionText = FormatQuestionForReview(input);

            var rubricPrompt = $"""
                You are a quality evaluator for Israeli Bagrut exam questions.
                Evaluate the following question on three dimensions, scoring each 0-100:

                1. FactualAccuracy: Is the content factually correct? Is the marked correct answer actually correct? Are the distractors plausible but clearly wrong?
                2. LanguageQuality: Is the {LangLabel(input.Language)} clear, natural, and grammatically correct? Is terminology appropriate for the subject?
                3. PedagogicalQuality: Is this question appropriate for {input.Grade ?? "Bagrut"} level? Does it test the claimed Bloom's level {input.ClaimedBloomLevel}? Is the difficulty appropriate?

                Question to evaluate:
                {questionText}

                Use the score_question tool to return your scores.
                """;

            var scoreSchema = InputSchema.FromRawUnchecked(
                JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""
                {
                    "type": "object",
                    "properties": {
                        "factualAccuracy": { "type": "integer", "minimum": 0, "maximum": 100 },
                        "languageQuality": { "type": "integer", "minimum": 0, "maximum": 100 },
                        "pedagogicalQuality": { "type": "integer", "minimum": 0, "maximum": 100 },
                        "reasoning": { "type": "string" }
                    },
                    "required": ["factualAccuracy", "languageQuality", "pedagogicalQuality"]
                }
                """)!);

            var tool = new Tool
            {
                Name = "score_question",
                Description = "Score a question on factual accuracy, language quality, and pedagogical quality",
                InputSchema = scoreSchema
            };

            var response = await client.Messages.Create(new MessageCreateParams
            {
                Model = "claude-haiku-4-5-20260101",  // Haiku: cheaper model for assessment tasks (routing-config section 2)
                MaxTokens = 1024,
                Temperature = 0.1f, // Low temperature for consistent scoring (routing-config: answer_evaluation)
                System = new List<TextBlockParam>
                {
                    new TextBlockParam
                    {
                        Text = "You are a Bagrut exam quality evaluator. Score questions objectively on a 0-100 scale.",
                        CacheControl = new CacheControlEphemeral()
                    }
                },
                Messages = new List<MessageParam>
                {
                    new MessageParam { Role = "user", Content = rubricPrompt }
                },
                Tools = new List<ToolUnion> { tool },
                ToolChoice = new ToolChoiceTool { Name = "score_question" },
            });

            foreach (var block in response.Content)
            {
                if (block.TryPickToolUse(out var toolUse) && toolUse.Name == "score_question")
                {
                    var factual = toolUse.Input.TryGetValue("factualAccuracy", out var fEl)
                        ? fEl.GetInt32() : 80;
                    var language = toolUse.Input.TryGetValue("languageQuality", out var lEl)
                        ? lEl.GetInt32() : 80;
                    var pedagogical = toolUse.Input.TryGetValue("pedagogicalQuality", out var pEl)
                        ? pEl.GetInt32() : 75;

                    // Clamp to valid range
                    factual = Math.Clamp(factual, 0, 100);
                    language = Math.Clamp(language, 0, 100);
                    pedagogical = Math.Clamp(pedagogical, 0, 100);

                    _logger?.LogInformation(
                        "LLM quality gate scores for {QuestionId}: factual={Factual}, language={Language}, pedagogical={Pedagogical}",
                        input.QuestionId, factual, language, pedagogical);

                    return (factual, language, pedagogical);
                }
            }

            _logger?.LogWarning("LLM quality gate returned no tool_use block for {QuestionId}", input.QuestionId);
            return (80, 80, 75);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "LLM quality gate failed for {QuestionId}; using defaults", input.QuestionId);
            return (80, 80, 75);
        }
    }

    private AnthropicClient GetOrCreateClient(string apiKey)
    {
        if (_client is not null && _lastApiKey == apiKey)
            return _client;

        _client = new AnthropicClient(new ClientOptions
        {
            ApiKey = apiKey,
            MaxRetries = 0,
        });
        _lastApiKey = apiKey;
        return _client;
    }

    private static string FormatQuestionForReview(QualityGateInput input)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Subject: {input.Subject}");
        sb.AppendLine($"Language: {input.Language}");
        sb.AppendLine($"Grade: {input.Grade ?? "N/A"}");
        sb.AppendLine($"Bloom's Level: {input.ClaimedBloomLevel}");
        sb.AppendLine($"Difficulty: {input.ClaimedDifficulty:F2}");
        sb.AppendLine();
        sb.AppendLine($"Stem: {input.Stem}");
        sb.AppendLine();

        for (int i = 0; i < input.Options.Count; i++)
        {
            var opt = input.Options[i];
            var marker = opt.IsCorrect ? " [CORRECT]" : "";
            sb.AppendLine($"  {opt.Label}) {opt.Text}{marker}");
            if (!string.IsNullOrEmpty(opt.DistractorRationale))
                sb.AppendLine($"     Rationale: {opt.DistractorRationale}");
        }

        return sb.ToString();
    }

    private static string LangLabel(string lang) => lang switch
    {
        "he" => "Hebrew", "ar" => "Arabic", "en" => "English", _ => lang
    };

    // ── Composite & Decision (unchanged) ──

    private float ComputeComposite(DimensionScores s)
    {
        return 0.15f * s.FactualAccuracy
             + 0.10f * s.LanguageQuality
             + 0.10f * s.PedagogicalQuality
             + 0.15f * s.DistractorQuality
             + 0.15f * s.StemClarity
             + 0.10f * s.BloomAlignment
             + 0.20f * s.StructuralValidity
             + 0.05f * s.CulturalSensitivity;
    }

    private GateDecision DetermineDecision(DimensionScores s, float composite)
    {
        if (s.StructuralValidity < _thresholds.StructuralValidityReject) return GateDecision.AutoRejected;
        if (s.FactualAccuracy < _thresholds.FactualAccuracyReject) return GateDecision.AutoRejected;
        if (s.CulturalSensitivity < _thresholds.CulturalSensitivityHardGate) return GateDecision.AutoRejected;
        if (s.DistractorQuality < _thresholds.DistractorQualityReject) return GateDecision.AutoRejected;
        if (s.StemClarity < _thresholds.StemClarityReject) return GateDecision.AutoRejected;
        if (s.BloomAlignment < _thresholds.BloomAlignmentReject) return GateDecision.AutoRejected;

        if (composite < _thresholds.CompositeReject) return GateDecision.AutoRejected;

        if (s.StructuralValidity >= _thresholds.StructuralValidityApprove
            && s.FactualAccuracy >= _thresholds.FactualAccuracyApprove
            && s.LanguageQuality >= _thresholds.LanguageQualityApprove
            && s.PedagogicalQuality >= _thresholds.PedagogicalQualityApprove
            && s.DistractorQuality >= _thresholds.DistractorQualityApprove
            && s.StemClarity >= _thresholds.StemClarityApprove
            && s.BloomAlignment >= _thresholds.BloomAlignmentApprove
            && composite >= _thresholds.CompositeApprove)
        {
            return GateDecision.AutoApproved;
        }

        return GateDecision.NeedsReview;
    }

    // RDY-034 §13: Best-effort binding lookup. Never throws — falls back to
    // null on Marten errors so the gate continues to function in degraded mode.
    private async Task<QuestionCasBinding?> TryLoadBindingAsync(string questionId)
    {
        if (_store is null || string.IsNullOrEmpty(questionId)) return null;
        try
        {
            await using var session = _store.QuerySession();
            return await session.LoadAsync<QuestionCasBinding>(questionId);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex,
                "[QG_BINDING_LOOKUP_FAILED] questionId={Qid}; falling back to LLM factual score",
                questionId);
            return null;
        }
    }

    private static bool IsMathOrPhysicsSubject(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject)) return false;
        return subject.Equals("math", StringComparison.OrdinalIgnoreCase)
            || subject.Equals("mathematics", StringComparison.OrdinalIgnoreCase)
            || subject.Equals("maths", StringComparison.OrdinalIgnoreCase)
            || subject.Equals("physics", StringComparison.OrdinalIgnoreCase)
            || subject.Equals("chemistry", StringComparison.OrdinalIgnoreCase);
    }
}
