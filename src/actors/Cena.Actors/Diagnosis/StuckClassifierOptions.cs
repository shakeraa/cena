// =============================================================================
// Cena Platform — StuckClassifierOptions (RDY-063 Phase 1)
//
// Configuration bound from Cena:StuckClassifier:* keys. Defaults are
// conservative (feature off, tight confidence thresholds) so that
// enabling the flag doesn't silently change scaffolding behaviour in
// environments that haven't opted in.
// =============================================================================

namespace Cena.Actors.Diagnosis;

public sealed class StuckClassifierOptions
{
    public const string SectionName = "Cena:StuckClassifier";

    /// <summary>
    /// Master on/off. When false, the classifier returns Unknown instantly
    /// with Source=None and no LLM call is ever made. Default OFF.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// When the heuristic pre-pass produces a confident label (≥ this
    /// threshold), the LLM call is skipped. 0.7 by default — heuristic
    /// rules are designed to emit 0.8–0.95 on strong signals, 0.4–0.6
    /// on weak.
    /// </summary>
    public float HeuristicSkipLlmThreshold { get; set; } = 0.7f;

    /// <summary>
    /// Below this confidence the diagnosis is treated as low-signal and
    /// the caller falls back to the existing hint ladder. 0.6 by default.
    /// </summary>
    public float MinActionableConfidence { get; set; } = 0.6f;

    /// <summary>
    /// When heuristic and LLM disagree on the primary label, we emit a
    /// HybridDisagreement result and multiply the LLM's confidence by
    /// this dampening factor. 0.6 by default (lowers actionability).
    /// </summary>
    public float DisagreementDampening { get; set; } = 0.6f;

    /// <summary>
    /// Haiku model id. Default tracks the pinned id from
    /// environment memory (`claude-haiku-4-5-20251001`). Can be overridden
    /// per environment for canary or fallback.
    /// </summary>
    public string LlmModel { get; set; } = "claude-haiku-4-5-20251001";

    /// <summary>
    /// Max output tokens for the classifier call. The response is a
    /// structured JSON object ~80 tokens; budget 128 for headroom.
    /// </summary>
    public int MaxOutputTokens { get; set; } = 128;

    /// <summary>
    /// Classifier version string — persisted alongside every diagnosis so
    /// we can replay / retrain against specific labelling eras. Bump when
    /// the prompt OR the heuristic rules change materially.
    /// </summary>
    public string ClassifierVersion { get; set; } = "v1.0.0";

    /// <summary>
    /// Secret used to HMAC (studentId, sessionId) into the anon id. MUST
    /// be set in non-dev environments. A dev-default is applied if left
    /// blank; non-dev boot fails loudly in StuckClassifierRegistration.
    /// </summary>
    public string AnonSalt { get; set; } = "";

    /// <summary>
    /// Retention cap on persisted diagnoses (in days). ADR-0003 requires
    /// ≤30-day retention for session-scoped misconception data and
    /// derivative labels like this one.
    /// </summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>
    /// Per-call cost ceiling (USD) charged to the circuit breaker on LLM
    /// path. Haiku @ $0.0005/call × 2× safety = $0.001 cap. 0 disables
    /// the charge (useful in tests).
    /// </summary>
    public double PerCallCostUsd { get; set; } = 0.001;

    // ── Phase 2b: hint-level adjustment (default OFF) ─────────────────

    /// <summary>
    /// Phase 2b flag. When false, the classifier runs in shadow mode
    /// only — observed + persisted, never alters the student-facing
    /// hint response. When true, the classifier's suggested strategy
    /// maps to a hint-level adjustment (clamp up / down) before the
    /// pre-authored hint text is generated. Default OFF.
    /// <br/>
    /// Safety layers preserved even when ON:
    /// <list type="bullet">
    /// <item>Hint text still comes from the pre-authored ladder
    /// (no LLM-generated copy reaches the student).</item>
    /// <item>Adjustment happens AFTER the MaxHints budget check.</item>
    /// <item>Low-confidence diagnoses (below
    /// <see cref="HintAdjustmentMinConfidence"/>) produce no change.</item>
    /// <item>If the classifier exceeds
    /// <see cref="HintAdjustmentTimeoutMs"/>, the original level is
    /// used (hint never blocks on classifier).</item>
    /// </list>
    /// </summary>
    public bool HintAdjustmentEnabled { get; set; } = false;

    /// <summary>
    /// Max time (ms) the endpoint will wait on the classifier before
    /// abandoning the call and using the requested hint level unchanged.
    /// 500ms default: heuristic-only path p95 is &lt;50ms; LLM path p95
    /// with warm prompt cache is ~800ms — so a 500ms budget will
    /// occasionally fall back to heuristic only, which is acceptable.
    /// </summary>
    public int HintAdjustmentTimeoutMs { get; set; } = 500;

    /// <summary>
    /// Minimum primary-confidence required before the adjuster is
    /// allowed to change the hint level. Diagnoses below this threshold
    /// are treated as low-signal; original level is preserved.
    /// </summary>
    public float HintAdjustmentMinConfidence { get; set; } = 0.65f;
}
