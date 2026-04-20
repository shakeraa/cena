// =============================================================================
// Cena Platform — ClaudeStuckClassifierLlm (RDY-063 Phase 1)
//
// Production LLM path using the Anthropic SDK with Claude Haiku.
// Prompt is structured with a stable, cacheable system prefix
// (ontology + rules) and a dynamic user suffix (the StuckContext JSON
// with PII already scrubbed). This ordering keeps token-generation
// latency low once prompt caching warms up.
//
// Returns a structured JSON response parsed into StuckType enum values.
// Unknown labels, malformed JSON, rate-limits, and transient errors all
// surface as LlmClassificationResult.Invalid — callers fall back to the
// heuristic path.
// =============================================================================

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Models.Messages;
using Cena.Infrastructure.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Diagnosis;

// ADR-0026: Stuck classification is a short structured extraction from a
// StuckContext JSON. Matches the tier-2 (Haiku) pattern per
// contracts/llm/routing-config.yaml §2 (task_routing.stagnation_analysis /
// error_classification family). Low temperature, small max_tokens.
// prr-046: finops cost-center "stuck-classification". Shadow-mode classifier.
// ADR-0047: prompt is a structured JSON payload built from anonymised
// StuckContext (studentAnonId, not studentId; textScrubbed field for
// question text; numeric mastery/advancement signals). No raw student
// free-text or profile PII reaches this seam — the scrub is structural
// at the payload-build boundary (BuildUserPayload) rather than regex.
[TaskRouting("tier2", "stagnation_analysis")]
[FeatureTag("stuck-classification")]
[PiiPreScrubbed("Prompt is a structured JSON payload of anonymised StuckContext (studentAnonId, textScrubbed, numeric mastery/advancement). No student free-text or profile PII reaches this seam. See BuildUserPayload.")]
public sealed class ClaudeStuckClassifierLlm : IStuckClassifierLlm
{
    private readonly AnthropicClient _client;
    private readonly StuckClassifierOptions _options;
    private readonly ILogger<ClaudeStuckClassifierLlm> _logger;
    private readonly ILlmCostMetric? _featureCost;

    // Haiku 4.5 typical pricing (USD per 1M tokens). Kept local rather
    // than in config so we can reason about cost at compile time; update
    // if Anthropic publishes new numbers.
    private const double HaikuInputPerMillion = 1.00;
    private const double HaikuOutputPerMillion = 5.00;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ClaudeStuckClassifierLlm(
        AnthropicClient client,
        IOptions<StuckClassifierOptions> options,
        ILogger<ClaudeStuckClassifierLlm> logger,
        ILlmCostMetric? featureCost = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _featureCost = featureCost;
    }

    public async Task<LlmClassificationResult> ClassifyAsync(
        StuckContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            var userPayload = BuildUserPayload(ctx);

            var response = await _client.Messages.Create(new MessageCreateParams
            {
                Model = _options.LlmModel,
                MaxTokens = _options.MaxOutputTokens,
                System = SystemPrompt,
                Temperature = 0,
                Messages = new List<MessageParam>
                {
                    new() { Role = "user", Content = userPayload }
                }
            }, ct);

            sw.Stop();

            var text = string.Join("", response.Content
                .Select(b => b.Value)
                .OfType<TextBlock>()
                .Select(b => b.Text));

            var parsed = ParseResponse(text);
            if (!parsed.Success)
            {
                _logger.LogWarning(
                    "Stuck classifier LLM returned unparseable output (len={Len}) for session {SessionId}",
                    text?.Length ?? 0, ctx.SessionId);
                return LlmClassificationResult.Invalid("parse_failure", (int)sw.ElapsedMilliseconds);
            }

            var input = (int)(response.Usage?.InputTokens ?? 0);
            var output = (int)(response.Usage?.OutputTokens ?? 0);
            var cost = (input / 1_000_000.0 * HaikuInputPerMillion) +
                       (output / 1_000_000.0 * HaikuOutputPerMillion);

            // prr-046: per-feature cost tag on success path. Model_id from
            // options so the label reflects reality even if the config is
            // swapped mid-deploy.
            _featureCost?.Record(
                feature: "stuck-classification",
                tier: "tier2",
                task: "stagnation_analysis",
                modelId: _options.LlmModel,
                inputTokens: input,
                outputTokens: output);

            return parsed with
            {
                EstimatedCostUsd = cost,
                LatencyMs = (int)sw.ElapsedMilliseconds,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex,
                "Stuck classifier LLM call failed for session {SessionId} ({Ms}ms)",
                ctx.SessionId, sw.ElapsedMilliseconds);
            return LlmClassificationResult.Invalid("llm_exception", (int)sw.ElapsedMilliseconds);
        }
    }

    // Exposed for unit tests — parse logic is the primary safety-critical
    // boundary (bad JSON cannot be allowed to crash the scaffolder).
    internal static LlmClassificationResult ParseResponse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return LlmClassificationResult.Invalid("empty_response", 0);

        // The model is instructed to emit exactly a JSON object. Extract
        // the first balanced {...} substring to survive prose preamble.
        var json = ExtractFirstJsonObject(raw);
        if (json is null)
            return LlmClassificationResult.Invalid("no_json_object", 0);

        try
        {
            var dto = JsonSerializer.Deserialize<ClassificationResponseDto>(json, JsonOpts);
            if (dto is null)
                return LlmClassificationResult.Invalid("parse_null", 0);

            var primary = ParseEnum(dto.Primary);
            var secondary = ParseEnum(dto.Secondary);

            // Safety: LLM is constrained to enum values by the prompt; if
            // it drifted to "answer": "x = 3" style content, reject.
            if (HasContentLeak(raw))
                return LlmClassificationResult.Invalid("content_leak", 0);

            return new LlmClassificationResult(
                Success: true,
                Primary: primary,
                PrimaryConfidence: Clamp(dto.PrimaryConfidence),
                Secondary: secondary,
                SecondaryConfidence: Clamp(dto.SecondaryConfidence),
                RawReason: dto.ReasonCode,
                EstimatedCostUsd: 0,
                LatencyMs: 0,
                ErrorCode: null);
        }
        catch (JsonException)
        {
            return LlmClassificationResult.Invalid("json_deserialize", 0);
        }
    }

    private static string BuildUserPayload(StuckContext ctx)
    {
        // Minimal JSON serialisation of the classifier input. Fields are
        // reordered so the most-discriminative signals appear first
        // (helps the LLM despite temperature=0). No prose — the model is
        // told in the system prompt exactly what to read.
        var dto = new
        {
            sessionId = ctx.SessionId,
            studentAnonId = ctx.StudentAnonId,
            locale = ctx.Locale,
            question = new
            {
                id = ctx.Question.QuestionId,
                type = ctx.Question.QuestionType,
                difficulty = ctx.Question.QuestionDifficulty,
                chapterId = ctx.Question.ChapterId,
                learningObjectiveIds = ctx.Question.LearningObjectiveIds,
                textScrubbed = ctx.Question.CanonicalTextByLocaleScrubbed,
            },
            advancement = new
            {
                currentChapterId = ctx.Advancement.CurrentChapterId,
                currentChapterStatus = ctx.Advancement.CurrentChapterStatus,
                retention = ctx.Advancement.CurrentChapterRetention,
                mastered = ctx.Advancement.ChaptersMasteredCount,
                total = ctx.Advancement.ChaptersTotalCount,
            },
            attempts = ctx.Attempts.Select(a => new
            {
                at = a.SubmittedAt,
                input = a.LatexInputScrubbed,
                correct = a.WasCorrect,
                sincePrevSec = a.TimeSincePrevAttemptSec,
                changeRatio = a.InputChangeRatio,
                errorType = a.ErrorType,
            }).ToArray(),
            signals = new
            {
                timeOnQuestionSec = ctx.SessionSignals.TimeOnQuestionSec,
                hintsSoFar = ctx.SessionSignals.HintsRequestedSoFar,
                itemsSolved = ctx.SessionSignals.ItemsSolvedInSession,
                itemsBailed = ctx.SessionSignals.ItemsBailedInSession,
                recentAccuracy = ctx.SessionSignals.RecentAccuracy,
                responseTimeRatio = ctx.SessionSignals.ResponseTimeRatio,
            },
            asOf = ctx.AsOf,
        };
        return JsonSerializer.Serialize(dto);
    }

    // Stable, cacheable prefix. Change this string → bump
    // StuckClassifierOptions.ClassifierVersion.
    private const string SystemPrompt = """
You are a classification-only assistant for an adaptive-learning platform.
Your ONLY task is to label the *type of stuck* a student is in on a math
question, choosing from a fixed seven-category ontology. You MUST return
exactly one JSON object matching the schema below — no prose, no
LaTeX, no math claims, no answer hints, no step-by-step, nothing else.

Ontology (choose one primary and one secondary; secondary may equal
primary if there is no meaningful alternative):
  - Encoding     — student doesn't parse the question itself
  - Recall       — student can't retrieve the needed theorem/definition
  - Procedural   — student knows the procedure, stuck on a specific step
  - Strategic    — student knows tools but can't pick which to apply
  - Misconception — student is confidently wrong on a repeated pattern
  - Motivational — student could continue but isn't engaged
  - MetaStuck    — student is lost and needs regrounding
  - Unknown      — insufficient signal to commit to a label

JSON schema (return EXACTLY this shape, nothing more):
{
  "primary":             "<ontology value>",
  "primaryConfidence":   <float 0..1>,
  "secondary":           "<ontology value>",
  "secondaryConfidence": <float 0..1>,
  "reasonCode":          "<short machine tag, e.g. repeated_sign_error>"
}

Rules:
  1. Decide based ONLY on the input JSON. Do not invent signals.
  2. Your response MUST parse as JSON. No markdown fences.
  3. Never reproduce the question text, the student's attempt, or the
     answer in the output. The reasonCode must be a short snake_case
     tag, not a sentence.
  4. If signal is weak, return "Unknown" with confidence 0.
""";

    private static string? ExtractFirstJsonObject(string raw)
    {
        int start = raw.IndexOf('{');
        if (start < 0) return null;
        int depth = 0;
        bool inString = false;
        bool escape = false;
        for (int i = start; i < raw.Length; i++)
        {
            char c = raw[i];
            if (escape) { escape = false; continue; }
            if (c == '\\') { escape = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (c == '{') depth++;
            else if (c == '}') { depth--; if (depth == 0) return raw.Substring(start, i - start + 1); }
        }
        return null;
    }

    private static StuckType ParseEnum(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return StuckType.Unknown;
        return Enum.TryParse<StuckType>(s, ignoreCase: true, out var v)
            ? v
            : StuckType.Unknown;
    }

    private static float Clamp(float v) => Math.Max(0f, Math.Min(1f, v));

    // Defence-in-depth: reject LLM output that appears to contain
    // math/answer content. The prompt forbids it; this is belt-and-braces.
    private static bool HasContentLeak(string raw)
    {
        if (raw.Contains('=') && raw.IndexOf('=') > raw.IndexOf('{'))
        {
            // Any '=' inside the JSON body can signal "x = 3"-style leak.
            // The schema itself has no '=' characters, so this is safe.
            // However JSON numbers use no '=' either; Scientific-notation
            // doesn't use it. Conservative.
            int bodyStart = raw.IndexOf('{');
            int bodyEnd = raw.LastIndexOf('}');
            if (bodyStart >= 0 && bodyEnd > bodyStart)
            {
                var body = raw.Substring(bodyStart, bodyEnd - bodyStart);
                if (body.Contains('=')) return true;
            }
        }
        if (raw.Contains("\\frac") || raw.Contains("\\sqrt") || raw.Contains("\\int"))
            return true;
        return false;
    }

    private sealed record ClassificationResponseDto(
        [property: JsonPropertyName("primary")] string? Primary,
        [property: JsonPropertyName("primaryConfidence")] float PrimaryConfidence,
        [property: JsonPropertyName("secondary")] string? Secondary,
        [property: JsonPropertyName("secondaryConfidence")] float SecondaryConfidence,
        [property: JsonPropertyName("reasonCode")] string? ReasonCode);
}
