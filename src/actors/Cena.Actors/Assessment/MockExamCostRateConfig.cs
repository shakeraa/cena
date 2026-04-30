// =============================================================================
// Cena Platform — Mock-exam cost rate configuration (PRR-322)
//
// Cost-per-unit rates that MockExamRunService.SubmitAsync multiplies into
// the per-run counts to compute USD totals. DI-injected as IOptions so
// per-environment rates can vary via appsettings without redeploy:
//
//   {
//     "Cena": {
//       "MockExamCostRates": {
//         "CasUsdPerCall":              0.0001,
//         "LlmInputUsdPer1kTokens":     0.0008,
//         "LlmOutputUsdPer1kTokens":    0.0040,
//         "OcrUsdPerCall":              0.0050
//       }
//     }
//   }
//
// Defaults below are 2026-Q2 estimates (Haiku Anthropic input/output rates,
// SymPy CPU amortized at near-zero, OCR sidecar wholesale per call).
// Realised costs reconcile against vendor invoices monthly; if drift
// exceeds 10% the operator updates appsettings + ops re-runs the historical
// re-projection script (docs/ops/migrations/2026-04-30-mock-exam-run-cost-
// telemetry.md §Replay rates).
// =============================================================================

namespace Cena.Actors.Assessment;

public sealed class MockExamCostRateConfig
{
    /// <summary>USD per single SymPy verification attempt (PRR-297 retries
    /// each count separately — retries are real CPU cost). Default 0.0001
    /// reflects in-process SymPy sidecar amortized container CPU.</summary>
    public decimal CasUsdPerCall { get; set; } = 0.0001m;

    /// <summary>USD per 1k input tokens. Default = Haiku 3.5 input
    /// (~$0.80/M-tok ÷ 1000 = $0.0008).</summary>
    public decimal LlmInputUsdPer1kTokens { get; set; } = 0.0008m;

    /// <summary>USD per 1k output tokens. Default = Haiku 3.5 output
    /// (~$4.00/M-tok ÷ 1000 = $0.0040).</summary>
    public decimal LlmOutputUsdPer1kTokens { get; set; } = 0.0040m;

    /// <summary>USD per OCR sidecar invocation (Gemini Vision + Doctr fan-out
    /// amortized).</summary>
    public decimal OcrUsdPerCall { get; set; } = 0.0050m;
}
