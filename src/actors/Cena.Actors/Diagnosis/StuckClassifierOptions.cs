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
}
