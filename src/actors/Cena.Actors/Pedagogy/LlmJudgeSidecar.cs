// =============================================================================
// Cena Platform — LLM Judge Sidecar HTTP adapter (RDY-074 Phase 1B)
//
// Concrete ILlmJudge implementation that talks to an HTTP judge
// service. Phase 1B ships the client + circuit-breaker latency budget;
// the sidecar itself (docker/llm-judge-sidecar/) + the 200-item
// ground-truth labeled set land in Phase 1C with Dr. Nadia's sign-off.
//
// Hard contracts carried from RDY-074 Phase 1A:
//   - ExplainRequest.StudentExplanationEphemeral MUST NOT be logged,
//     persisted, or forwarded except to the judge for the single
//     JudgeAsync call (ADR-0003).
//   - Result carries a rubric category but NEVER quotes the student's
//     text — the judge returns buckets, not transcripts.
//   - Circuit-breaker refuses dispatch when p99 latency budget
//     exceeded for the configured window (RDY-074 accept §3).
// =============================================================================

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Pedagogy;

public sealed class LlmJudgeSidecarOptions
{
    public const string SectionName = "LlmJudgeSidecar";

    /// <summary>Base URL of the judge sidecar, e.g. <c>http://localhost:7101</c>.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Per-request latency budget. Default 2.5s per RDY-074 §3.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMilliseconds(2500);

    /// <summary>
    /// When TRUE, the adapter returns Unavailable immediately without
    /// any HTTP call — used by the circuit-breaker when the sidecar
    /// has been misbehaving.
    /// </summary>
    public bool CircuitOpen { get; set; }

    public bool IsComplete => !string.IsNullOrWhiteSpace(BaseUrl);
}

public sealed class LlmJudgeSidecar : ILlmJudge
{
    private readonly LlmJudgeSidecarOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<LlmJudgeSidecar> _logger;

    public LlmJudgeSidecar(
        IOptions<LlmJudgeSidecarOptions> options,
        HttpClient http,
        ILogger<LlmJudgeSidecar> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _http = http;
        _logger = logger;

        if (_options.IsComplete && _http.BaseAddress is null)
            _http.BaseAddress = new Uri(_options.BaseUrl!);
        _http.Timeout = _options.RequestTimeout;
    }

    public string Backend => "llm-sidecar";

    public async Task<JudgmentResult> JudgeAsync(
        ExplainRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_options.IsComplete)
            return JudgmentResult.Unavailable("sidecar-not-configured");

        if (_options.CircuitOpen)
            return JudgmentResult.Unavailable("circuit-open");

        var started = DateTimeOffset.UtcNow;
        try
        {
            // Wire DTO kept small + separate from the domain type so
            // the sidecar's API surface can evolve without churning
            // the call sites.
            var wire = new JudgeRequestWire(
                ConceptSlug: request.ConceptSlug,
                ExpectedRulePlainLanguage: request.ExpectedRulePlainLanguage,
                StudentExplanationEphemeral: request.StudentExplanationEphemeral,
                Locale: request.Locale);

            using var response = await _http.PostAsJsonAsync("/judge", wire, ct);
            var latency = DateTimeOffset.UtcNow - started;

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return JudgmentResult.Unavailable("rate-limited");
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable)
                return JudgmentResult.Unavailable("sidecar-unavailable");
            if (!response.IsSuccessStatusCode)
                return JudgmentResult.Unavailable($"http-{(int)response.StatusCode}");

            var decoded = await response.Content.ReadFromJsonAsync<JudgeResponseWire>(ct);
            if (decoded is null)
                return JudgmentResult.Unavailable("decode-failed");

            return new JudgmentResult(
                Judgment: ParseJudgment(decoded.Judgment),
                Category: ParseCategory(decoded.Category),
                Bucket: ChooseBucket(ParseJudgment(decoded.Judgment)),
                JudgeLatency: latency,
                JudgeBackend: Backend);
        }
        catch (TaskCanceledException)
        {
            // Latency budget exceeded OR caller cancelled. Either way
            // the circuit-breaker layer (phase 1C) should notice +
            // flip CircuitOpen after N timeouts in a window.
            _logger.LogWarning(
                "[RDY-074] Judge timeout after {Ms}ms for concept={Concept}",
                (int)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
                request.ConceptSlug);
            return JudgmentResult.Unavailable("timeout");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(
                ex,
                "[RDY-074] Judge HTTP error for concept={Concept}",
                request.ConceptSlug);
            return JudgmentResult.Unavailable("http-error");
        }
    }

    internal static ExplainJudgment ParseJudgment(string? raw) => raw?.ToLowerInvariant() switch
    {
        "pass" => ExplainJudgment.Pass,
        "partial" or "partial-pass" => ExplainJudgment.PartialPass,
        "retry" => ExplainJudgment.Retry,
        _ => ExplainJudgment.Unavailable,
    };

    internal static ExplainRubricCategory ParseCategory(string? raw) => raw switch
    {
        "StatesRuleCorrectly" => ExplainRubricCategory.StatesRuleCorrectly,
        "StatesRuleButMisnames" => ExplainRubricCategory.StatesRuleButMisnames,
        "RestatesProblemWithoutRule" => ExplainRubricCategory.RestatesProblemWithoutRule,
        "GivesNumericAnswerOnly" => ExplainRubricCategory.GivesNumericAnswerOnly,
        "OffTopic" => ExplainRubricCategory.OffTopic,
        _ => ExplainRubricCategory.Unknown,
    };

    internal static CopyBucket ChooseBucket(ExplainJudgment judgment) => judgment switch
    {
        ExplainJudgment.Pass => CopyBucket.Celebrate,
        ExplainJudgment.PartialPass => CopyBucket.NudgeMissing,
        ExplainJudgment.Retry => CopyBucket.Redirect,
        _ => CopyBucket.SilentSkip,
    };

    private sealed record JudgeRequestWire(
        [property: JsonPropertyName("conceptSlug")] string ConceptSlug,
        [property: JsonPropertyName("expectedRule")] string ExpectedRulePlainLanguage,
        [property: JsonPropertyName("studentExplanation")] string StudentExplanationEphemeral,
        [property: JsonPropertyName("locale")] string Locale);

    private sealed class JudgeResponseWire
    {
        [JsonPropertyName("judgment")]
        public string? Judgment { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }
    }
}
