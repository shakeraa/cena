// =============================================================================
// Cena Platform — SessionAttemptMode (EPIC-PRR-F PRR-260)
//
// Student-controlled hide-then-reveal toggle. Backs the
// `attemptMode: 'visible' | 'hidden_reveal'` session-scoped flag that the
// student flips at session start or mid-session. Default is
// <see cref="Visible"/> (traditional behavior; 8/10 persona consensus).
//
// Pedagogy rationale. Bjork's generation-effect research suggests that
// attempting an answer from memory BEFORE seeing the options produces
// better retrieval-strength than passive option-reading. That benefit is
// documented but not universal — a11y + SRE personas flag that forcing
// hidden-first would misrepresent effect-size and harm students who
// benefit from the visible scaffold. So Cena ships visible-first by
// default and lets students opt in to hidden-reveal per session, never
// globally. Opt-in autonomy preserves the persona-ethics guardrail.
//
// Scope boundaries locked in the task file (PRR-260):
//   - Per-session only. Does NOT persist across sessions (student re-
//     opts-in each session so autonomy is expressed anew every time).
//   - Applies only to questions with MC options (Choices.Length >= 1).
//     Step-solver math / chem / essay items skip the toggle entirely —
//     attempt-mode is a no-op for them.
//   - Diagnostic-block assessments (PRR-228 per-target diagnostics)
//     IGNORE the stored mode and always render visible. That's a
//     read-time override implemented by callers via the helper on
//     LearningSessionQueueProjection — see PRR-263 for the downstream
//     diagnostic-ignores-hide-reveal task.
//   - Author-level `forceOptionsVisible: true` on the question document
//     wins — some items ARE the options (e.g. "which graph is correct")
//     and hiding them would break the question's pedagogical contract.
//   - Server-side enforcement: student-mode uses CLIENT-side hiding.
//     Persona-redteam flagged bypass via DevTools — acceptable for
//     self-discipline mode. Classroom-enforced redaction is PRR-261
//     (different task entirely, with server-side projection trimming).
//
// Ship-gate memory discipline. No streak, no timer-based auto-hide
// (ADR-0048 countdown ban is absolute), no Option-C pedagogy-driven
// hide (dark-pattern, persona consensus).
// =============================================================================

namespace Cena.Actors.Sessions;

/// <summary>
/// Session-scoped attempt-mode preference. Drives whether MC options
/// render immediately (<see cref="Visible"/>) or behind a click-to-reveal
/// placeholder (<see cref="HiddenReveal"/>).
/// </summary>
public enum SessionAttemptMode
{
    /// <summary>
    /// Traditional render — MC options visible from question load.
    /// Default for every new session. 8/10 persona consensus baseline.
    /// </summary>
    Visible = 0,

    /// <summary>
    /// Placeholder-first render — options hidden behind a
    /// <c>"Click to reveal options"</c> button until the student reveals.
    /// Realises Bjork's generation effect for students who opt in.
    /// </summary>
    HiddenReveal = 1,
}

/// <summary>
/// Stable wire values + parse helpers for <see cref="SessionAttemptMode"/>.
/// Keep the wire form in this file so the enum and the strings never
/// drift — if a new mode is added the compiler forces an update here too.
/// </summary>
public static class SessionAttemptModeWire
{
    /// <summary>Wire value for <see cref="SessionAttemptMode.Visible"/>.</summary>
    public const string Visible = "visible";

    /// <summary>Wire value for <see cref="SessionAttemptMode.HiddenReveal"/>.</summary>
    public const string HiddenReveal = "hidden_reveal";

    /// <summary>Render the enum as its canonical wire string.</summary>
    public static string ToWire(SessionAttemptMode mode) => mode switch
    {
        SessionAttemptMode.Visible => Visible,
        SessionAttemptMode.HiddenReveal => HiddenReveal,
        _ => Visible, // safe default — never emit an unknown on the wire
    };

    /// <summary>
    /// Parse an inbound wire string. Case-insensitive; whitespace-trimmed.
    /// Returns false for unknown / null / whitespace input so callers can
    /// reject at the endpoint boundary with a 400.
    /// </summary>
    public static bool TryParse(string? input, out SessionAttemptMode mode)
    {
        mode = SessionAttemptMode.Visible;
        if (string.IsNullOrWhiteSpace(input)) return false;
        switch (input.Trim().ToLowerInvariant())
        {
            case Visible:
                mode = SessionAttemptMode.Visible;
                return true;
            case HiddenReveal:
                mode = SessionAttemptMode.HiddenReveal;
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// Inputs the read-side override policy needs to compute the
/// <em>effective</em> attempt mode for a single question render.
/// Callers assemble this and pass it to
/// <see cref="SessionAttemptModePolicy.ResolveEffective"/> or
/// <see cref="Cena.Actors.Projections.LearningSessionQueueProjectionExtensions.EffectiveAttemptMode"/>.
/// </summary>
/// <param name="StoredMode">Mode captured on the session projection.</param>
/// <param name="IsQuestionMultipleChoice">
/// False for step-solver / chem / essay; in that case attempt-mode is
/// irrelevant and the policy returns Visible (no hide affordance).
/// </param>
/// <param name="AuthorForceVisible">
/// Question-author flag. True when the options ARE the question
/// ("which graph is correct") and hiding would break the contract.
/// </param>
/// <param name="IsDiagnosticBlock">
/// True when the session is currently inside a PRR-228 per-target
/// diagnostic block. Diagnostic renders are always visible to keep
/// calibration comparable across students (PRR-263).
/// </param>
public sealed record SessionAttemptModeContext(
    SessionAttemptMode StoredMode,
    bool IsQuestionMultipleChoice,
    bool AuthorForceVisible,
    bool IsDiagnosticBlock);

/// <summary>
/// Read-side resolver. Folds the five scope invariants (MC-only,
/// diagnostic override, author force-visible, default) into the
/// render decision so no caller has to remember the precedence rules
/// on their own.
/// </summary>
public static class SessionAttemptModePolicy
{
    /// <summary>
    /// Resolve the effective attempt mode for a single question render.
    /// Precedence (highest wins):
    /// <list type="number">
    ///   <item>Non-MC questions → Visible (no hide affordance).</item>
    ///   <item>Author force-visible → Visible (authoring contract).</item>
    ///   <item>Diagnostic block → Visible (PRR-263 calibration guarantee).</item>
    ///   <item>Otherwise → <see cref="SessionAttemptModeContext.StoredMode"/>.</item>
    /// </list>
    /// </summary>
    public static SessionAttemptMode ResolveEffective(SessionAttemptModeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.IsQuestionMultipleChoice) return SessionAttemptMode.Visible;
        if (context.AuthorForceVisible) return SessionAttemptMode.Visible;
        if (context.IsDiagnosticBlock) return SessionAttemptMode.Visible;
        return context.StoredMode;
    }
}
