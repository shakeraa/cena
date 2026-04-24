// =============================================================================
// Cena Platform — Flow State API Contracts (RDY-034)
//
// Shared DTOs for the flow state assessment endpoint. The student PWA's
// useFlowState composable (src/student/full-version/src/composables/useFlowState.ts)
// feeds four session signals into the backend and receives the authoritative
// state + recommended action. The state machine used to live in the frontend
// only; RDY-034 moves it server-side so every client (web, mobile, CLI tools)
// agrees on the same boundaries and transitions.
//
// Csikszentmihalyi-style flow model (backed by docs/research/…):
//   warming     session warm-up (< 3 questions answered, no signals yet)
//   approaching building momentum (positive accuracy trend)
//   inFlow      peak engagement (streak + rising trend + low fatigue)
//   disrupted   accuracy falling fast (difficulty step-down advised)
//   fatigued    long session or high fatigue (break advised)
//
// Ship-gate reminder: this is the engagement lever that REPLACES the banned
// dark-pattern mechanics (streaks-as-scoreboard, variable-ratio rewards).
// Do not bolt any counter-gamification layer on top of FlowState.
// =============================================================================

namespace Cena.Api.Contracts.Sessions;

/// <summary>
/// Flow state categories surfaced to clients. Stringified on the wire so the
/// Vue composable can keep using literal types ("warming", "inFlow", etc.).
/// </summary>
public enum FlowStateKind
{
    Warming = 0,
    Approaching = 1,
    InFlow = 2,
    Disrupted = 3,
    Fatigued = 4,
}

/// <summary>
/// Request payload for <c>POST /api/sessions/flow-state/assess</c>.
/// All four signals come from the student session actor — the caller is
/// responsible for supplying real values (no stub defaults on the server).
/// </summary>
/// <param name="FatigueLevel">
///     Cognitive fatigue score in [0,1], computed by ICognitiveLoadService.
/// </param>
/// <param name="AccuracyTrend">
///     Accuracy trend over the last 5 questions, in [-1,1].
///     Negative = declining, positive = improving.
/// </param>
/// <param name="ConsecutiveCorrect">
///     Number of consecutive correct answers immediately preceding now.
///     Zero after any incorrect answer.
/// </param>
/// <param name="SessionDurationMinutes">
///     Minutes elapsed since session start. Used for the 45-minute fatigue
///     cap that triggers a break suggestion regardless of fatigue score.
/// </param>
/// <param name="CurrentDifficulty">
///     Current difficulty level 1–10; optional. Provided → response carries
///     a difficulty adjustment suggestion. Omitted → suggestion is null.
/// </param>
public sealed record FlowStateAssessmentRequest(
    double FatigueLevel,
    double AccuracyTrend,
    int ConsecutiveCorrect,
    double SessionDurationMinutes,
    int? CurrentDifficulty = null);

/// <summary>
/// Response payload. <c>State</c> is camelCase-stringified to match the Vue
/// composable's <c>FlowState</c> union type exactly ("warming", "approaching",
/// "inFlow", "disrupted", "fatigued"). Frontend drops <see cref="Cena.Api.Contracts.Sessions.FlowStateAssessmentResponse.State"/>
/// straight into its ref.
/// </summary>
/// <param name="State">
///     Camel-cased state name matching the frontend union type.
/// </param>
/// <param name="FatigueLevel">Echoed from the request for client confirmation.</param>
/// <param name="AccuracyTrend">Echoed from the request.</param>
/// <param name="ConsecutiveCorrect">Echoed from the request.</param>
/// <param name="SessionDurationMinutes">Echoed from the request.</param>
/// <param name="RecommendedAction">
///     Client-actionable hint: "continue" | "slow_down" | "reduce_difficulty"
///     | "suggest_break". Never null.
/// </param>
/// <param name="CooldownMinutes">
///     If <c>State == fatigued</c>, the ICognitiveLoadService-computed cooldown.
///     Null for all other states.
/// </param>
/// <param name="DifficultyAdjustment">
///     "ease" | "maintain" | "increase" when the request supplied
///     CurrentDifficulty, otherwise null.
/// </param>
public sealed record FlowStateAssessmentResponse(
    string State,
    double FatigueLevel,
    double AccuracyTrend,
    int ConsecutiveCorrect,
    double SessionDurationMinutes,
    string RecommendedAction,
    int? CooldownMinutes,
    string? DifficultyAdjustment);
