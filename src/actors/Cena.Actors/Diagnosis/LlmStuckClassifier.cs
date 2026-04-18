// =============================================================================
// Cena Platform — LlmStuckClassifier (RDY-063 Phase 1)
//
// Thin adapter: calls IStuckClassifierLlm, converts the structured
// result to StuckDiagnosis, picks a scaffolding strategy from the
// primary label, and tags the source. Does not make any routing or
// gating decisions of its own — that is the hybrid composer's job.
// =============================================================================

using Cena.Actors.RateLimit;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagnosis;

public sealed class LlmStuckClassifier : IStuckTypeClassifier
{
    private readonly IStuckClassifierLlm _llm;
    private readonly ICostCircuitBreaker? _circuitBreaker;
    private readonly StuckClassifierOptions _options;
    private readonly ILogger<LlmStuckClassifier> _logger;

    public LlmStuckClassifier(
        IStuckClassifierLlm llm,
        StuckClassifierOptions options,
        ILogger<LlmStuckClassifier> logger,
        ICostCircuitBreaker? circuitBreaker = null)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _circuitBreaker = circuitBreaker;
    }

    public async Task<StuckDiagnosis> DiagnoseAsync(StuckContext ctx, CancellationToken ct = default)
    {
        // Hard short-circuit when the breaker is open — never burn budget.
        if (_circuitBreaker is not null && await _circuitBreaker.IsOpenAsync(ct))
        {
            return StuckDiagnosis.Unknown(
                _options.ClassifierVersion,
                StuckDiagnosisSource.CircuitBreaker,
                latencyMs: 0,
                reasonCode: "llm.circuit_open",
                at: ctx.AsOf);
        }

        var result = await _llm.ClassifyAsync(ctx, ct);

        if (!result.Success)
        {
            _logger.LogDebug(
                "LlmStuckClassifier: LLM unavailable ({Error}), returning Unknown for session {Session}",
                result.ErrorCode, ctx.SessionId);
            return StuckDiagnosis.Unknown(
                _options.ClassifierVersion,
                DetermineSource(result.ErrorCode),
                result.LatencyMs,
                reasonCode: "llm." + (result.ErrorCode ?? "unknown_error"),
                at: ctx.AsOf);
        }

        // Record the spend against the per-call budget. We record even if
        // the classifier returned Unknown — we paid for the call either
        // way.
        if (_circuitBreaker is not null && _options.PerCallCostUsd > 0)
        {
            try
            {
                await _circuitBreaker.RecordSpendAsync(result.EstimatedCostUsd, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "LlmStuckClassifier: failed to record spend (non-fatal)");
            }
        }

        return new StuckDiagnosis(
            Primary: result.Primary,
            PrimaryConfidence: result.PrimaryConfidence,
            Secondary: result.Secondary,
            SecondaryConfidence: result.SecondaryConfidence,
            SuggestedStrategy: MapStrategy(result.Primary),
            FocusChapterId: ctx.Advancement.CurrentChapterId,
            ShouldInvolveTeacher: ShouldEscalateToTeacher(result.Primary),
            Source: StuckDiagnosisSource.Llm,
            ClassifierVersion: _options.ClassifierVersion,
            DiagnosedAt: ctx.AsOf,
            LatencyMs: result.LatencyMs,
            SourceReasonCode: result.RawReason is null
                ? "llm.ok"
                : $"llm.{Sanitize(result.RawReason)}");
    }

    internal static StuckScaffoldStrategy MapStrategy(StuckType t) => t switch
    {
        StuckType.Encoding => StuckScaffoldStrategy.Rephrase,
        StuckType.Recall => StuckScaffoldStrategy.ShowDefinition,
        StuckType.Procedural => StuckScaffoldStrategy.ShowNextStep,
        StuckType.Strategic => StuckScaffoldStrategy.DecompositionPrompt,
        StuckType.Misconception => StuckScaffoldStrategy.ContradictionPrompt,
        StuckType.Motivational => StuckScaffoldStrategy.Encouragement,
        StuckType.MetaStuck => StuckScaffoldStrategy.Regroup,
        _ => StuckScaffoldStrategy.Unspecified,
    };

    internal static bool ShouldEscalateToTeacher(StuckType t) => t switch
    {
        StuckType.MetaStuck => true,
        StuckType.Motivational => true,
        _ => false,
    };

    private static StuckDiagnosisSource DetermineSource(string? errorCode) =>
        errorCode switch
        {
            null => StuckDiagnosisSource.LlmError,
            "llm_disabled" => StuckDiagnosisSource.None,
            "rate_limited" => StuckDiagnosisSource.CircuitBreaker,
            _ => StuckDiagnosisSource.LlmError,
        };

    /// <summary>
    /// Strip any non-tag characters from the LLM's reason string so it
    /// cannot be a vector for leakage into logs / ReasoningBank.
    /// </summary>
    private static string Sanitize(string raw)
    {
        Span<char> buf = stackalloc char[Math.Min(raw.Length, 48)];
        int j = 0;
        foreach (var c in raw)
        {
            if (j >= buf.Length) break;
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-') buf[j++] = char.ToLowerInvariant(c);
        }
        return j == 0 ? "untagged" : new string(buf[..j]);
    }
}
