// =============================================================================
// Cena Platform — HybridStuckClassifier (RDY-063 Phase 1)
//
// Composes the heuristic + LLM classifiers. Flow:
//
//   1. If the feature flag is OFF → return Unknown/None without calling
//      anything. This keeps the classifier a zero-cost no-op in
//      environments that haven't opted in.
//   2. Run the heuristic first (≤ 50 ms, no I/O).
//   3. If the heuristic fires with primaryConfidence ≥ HeuristicSkipLlmThreshold,
//      return it as-is with Source=Heuristic.
//   4. Otherwise, call the LLM. If it returns Unknown, fall back to the
//      heuristic result (even if low-confidence).
//   5. Compare the two. Agreement → escalate confidence, Source=HybridAgreement.
//      Disagreement → dampen confidence, Source=HybridDisagreement.
//
// Architecture tests enforce:
//   - Never throws; bad input returns Unknown.
//   - Never emits math / LaTeX (guaranteed by StuckDiagnosis shape).
//   - PII scrubber presence is a precondition — this class does NOT
//     re-scrub. See StuckContextBuilder.
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Actors.Diagnosis;

public sealed class HybridStuckClassifier : IStuckTypeClassifier, IDisposable
{
    private readonly HeuristicStuckClassifier _heuristic;
    private readonly IStuckTypeClassifier _llm;
    private readonly IOptionsMonitor<StuckClassifierOptions> _optionsMonitor;
    private readonly ILogger<HybridStuckClassifier> _logger;
    private readonly StuckClassifierMetrics _metrics;
    private readonly ActivitySource _activity;

    public HybridStuckClassifier(
        HeuristicStuckClassifier heuristic,
        IStuckTypeClassifier llm,
        IOptionsMonitor<StuckClassifierOptions> optionsMonitor,
        ILogger<HybridStuckClassifier> logger,
        StuckClassifierMetrics metrics)
    {
        _heuristic = heuristic ?? throw new ArgumentNullException(nameof(heuristic));
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _activity = new ActivitySource("Cena.Actors.Diagnosis.StuckClassifier", "1.0.0");
    }

    public async Task<StuckDiagnosis> DiagnoseAsync(StuckContext ctx, CancellationToken ct = default)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        using var activity = _activity.StartActivity("DiagnoseStuckType");
        activity?.SetTag("cena.session_id", ctx.SessionId);
        activity?.SetTag("cena.attempts", ctx.Attempts.Count);

        var opts = _optionsMonitor.CurrentValue;

        if (!opts.Enabled)
        {
            _metrics.RecordDiagnosis(StuckDiagnosisSource.None, StuckType.Unknown, actionable: false);
            return StuckDiagnosis.Unknown(
                opts.ClassifierVersion,
                StuckDiagnosisSource.None,
                latencyMs: 0,
                reasonCode: "hybrid.disabled",
                at: ctx.AsOf);
        }

        var totalSw = Stopwatch.StartNew();

        StuckDiagnosis heuristicResult;
        try
        {
            heuristicResult = await _heuristic.DiagnoseAsync(ctx, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Heuristic classifier threw for session {Session}", ctx.SessionId);
            _metrics.RecordDiagnosis(StuckDiagnosisSource.LlmError, StuckType.Unknown, actionable: false);
            return StuckDiagnosis.Unknown(
                opts.ClassifierVersion,
                StuckDiagnosisSource.LlmError,
                (int)totalSw.ElapsedMilliseconds,
                "hybrid.heuristic_threw",
                ctx.AsOf);
        }

        // Shortcut: strong heuristic → skip LLM altogether.
        if (heuristicResult.Primary != StuckType.Unknown &&
            heuristicResult.PrimaryConfidence >= opts.HeuristicSkipLlmThreshold)
        {
            var strong = heuristicResult with { LatencyMs = (int)totalSw.ElapsedMilliseconds };
            _metrics.RecordDiagnosis(
                StuckDiagnosisSource.Heuristic,
                strong.Primary,
                strong.IsActionable(opts.MinActionableConfidence));
            activity?.SetTag("cena.classifier.source", "heuristic_strong");
            return strong;
        }

        StuckDiagnosis llmResult;
        try
        {
            llmResult = await _llm.DiagnoseAsync(ctx, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LLM classifier threw for session {Session}", ctx.SessionId);
            // Fall back to the (possibly weak) heuristic; better than refusing.
            _metrics.RecordDiagnosis(
                StuckDiagnosisSource.LlmError,
                heuristicResult.Primary,
                heuristicResult.IsActionable(opts.MinActionableConfidence));
            return heuristicResult with
            {
                LatencyMs = (int)totalSw.ElapsedMilliseconds,
                Source = StuckDiagnosisSource.LlmError,
                SourceReasonCode = "hybrid.llm_threw_fell_back_heuristic",
            };
        }

        var combined = Compose(heuristicResult, llmResult, opts);
        var final = combined with { LatencyMs = (int)totalSw.ElapsedMilliseconds };
        _metrics.RecordDiagnosis(
            final.Source,
            final.Primary,
            final.IsActionable(opts.MinActionableConfidence));
        activity?.SetTag("cena.classifier.source", final.Source.ToString());
        activity?.SetTag("cena.classifier.primary", final.Primary.ToString());
        return final;
    }

    // Exposed for unit tests so we can exercise the matrix without a live LLM.
    internal static StuckDiagnosis Compose(
        StuckDiagnosis heuristic, StuckDiagnosis llm, StuckClassifierOptions opts)
    {
        // LLM unknown + heuristic unknown → Unknown/None.
        if (heuristic.Primary == StuckType.Unknown && llm.Primary == StuckType.Unknown)
        {
            return StuckDiagnosis.Unknown(
                opts.ClassifierVersion,
                StuckDiagnosisSource.None,
                Math.Max(heuristic.LatencyMs, llm.LatencyMs),
                "hybrid.both_unknown",
                heuristic.DiagnosedAt);
        }

        // LLM failed but heuristic fired → use heuristic as-is.
        if (llm.Primary == StuckType.Unknown)
        {
            return heuristic with { Source = StuckDiagnosisSource.Heuristic };
        }

        // Heuristic couldn't decide; use LLM.
        if (heuristic.Primary == StuckType.Unknown)
        {
            return llm;
        }

        // Both decided. Agreement → escalate. Disagreement → dampen.
        if (heuristic.Primary == llm.Primary)
        {
            // Take the MAX confidence as signal-boost, but cap at 0.95
            // so downstream code never treats classifier output as
            // "absolutely certain" — CAS verification and delivery
            // gating remain authoritative.
            var boosted = Math.Min(0.95f, Math.Max(heuristic.PrimaryConfidence, llm.PrimaryConfidence) + 0.05f);
            return llm with
            {
                Primary = heuristic.Primary,
                PrimaryConfidence = boosted,
                Source = StuckDiagnosisSource.HybridAgreement,
                SourceReasonCode = $"hybrid.agree.{heuristic.SourceReasonCode ?? "na"}",
            };
        }

        // Disagreement: trust the LLM more than the heuristic in
        // ambiguous cases (LLM has more context about wording, language,
        // cultural framing) BUT dampen the confidence so the caller is
        // likely to fall back to the conservative hint-ladder path.
        var dampened = Math.Max(0f, llm.PrimaryConfidence * opts.DisagreementDampening);
        return llm with
        {
            PrimaryConfidence = dampened,
            Secondary = heuristic.Primary,
            SecondaryConfidence = heuristic.PrimaryConfidence,
            Source = StuckDiagnosisSource.HybridDisagreement,
            SourceReasonCode = $"hybrid.disagree.heuristic_said_{heuristic.Primary.ToString().ToLowerInvariant()}",
        };
    }

    public void Dispose() => _activity.Dispose();
}
