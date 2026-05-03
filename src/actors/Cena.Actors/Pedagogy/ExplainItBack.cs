// =============================================================================
// Cena Platform — Socratic Explain-it-Back (RDY-074 Phase 1A)
//
// The "explain it back" prompt asks the student to state the rule /
// concept in their own words AFTER they've solved a problem. The LLM
// judge (phase 1B sidecar) classifies the explanation against a rubric;
// phase 1A ships the domain types + the prompt-fatigue gate + the
// null-judge graceful-disabled fallback so the student UI can call
// through the interface without waiting for the sidecar build.
//
// ADR-0003: explanation text NEVER persists beyond the judgment cycle.
// The JudgmentResult stores the outcome (Pass / PartialPass / Retry) +
// a rubric-category tag, never the student's free text.
//
// GD-004: no shame framing on wrong explanations. The result emits a
// neutral CopyBucket that maps to "try again, think about X" copy,
// never "you're wrong."
// =============================================================================

namespace Cena.Actors.Pedagogy;

/// <summary>
/// Outcome of judging one student explanation. Pass closes the
/// prompt; PartialPass gives a gentle nudge + allows a retry;
/// Retry asks for a rewrite. Unavailable means the judge sidecar
/// is down and the UI skips the prompt entirely.
/// </summary>
public enum ExplainJudgment
{
    /// <summary>Sidecar offline / circuit-broken / SDK missing. UI skips.</summary>
    Unavailable = 0,

    /// <summary>Explanation hits the rubric cleanly.</summary>
    Pass = 1,

    /// <summary>Partially correct; nudge toward the missing piece.</summary>
    PartialPass = 2,

    /// <summary>Misses the core rule; ask for a rewrite with a hint.</summary>
    Retry = 3
}

/// <summary>
/// Rubric category the judge emits alongside the judgment. Drives the
/// gentle-nudge copy without ever quoting the student's own text.
/// </summary>
public enum ExplainRubricCategory
{
    Unknown = 0,
    StatesRuleCorrectly = 1,
    StatesRuleButMisnames = 2,
    RestatesProblemWithoutRule = 3,
    GivesNumericAnswerOnly = 4,
    OffTopic = 5
}

/// <summary>
/// The student-facing copy bucket a judgment maps to. The UI picks the
/// localised copy string from the bucket; the student never sees a
/// rubric-internal label.
/// </summary>
public enum CopyBucket
{
    /// <summary>"Nice — you got the idea."</summary>
    Celebrate = 0,

    /// <summary>"Close — remember the part about X."</summary>
    NudgeMissing = 1,

    /// <summary>"Let's restate in your own words — think about X."</summary>
    Redirect = 2,

    /// <summary>"We'll skip this for now." (sidecar unavailable)</summary>
    SilentSkip = 3
}

/// <summary>
/// Request bundle the judge receives. The student's free-text
/// explanation is a *transient* string; callers MUST NOT persist it
/// beyond the Judge() call, and the judge implementation MUST NOT
/// log it or forward it anywhere outside the judgment cycle
/// (ADR-0003 + GD-004 + Ran's sign-off gate).
/// </summary>
public sealed record ExplainRequest(
    string StudentAnonId,
    string ConceptSlug,
    string ExpectedRulePlainLanguage,
    string StudentExplanationEphemeral,
    string Locale);

/// <summary>
/// Result returned to the session pipeline. Never carries the
/// student's original text — only the outcome + category + bucket.
/// </summary>
public sealed record JudgmentResult(
    ExplainJudgment Judgment,
    ExplainRubricCategory Category,
    CopyBucket Bucket,
    TimeSpan JudgeLatency,
    string JudgeBackend)
{
    /// <summary>
    /// Static convenience for callers who need a ready-made
    /// "sidecar unavailable" result — graceful-disabled pattern.
    /// </summary>
    public static JudgmentResult Unavailable(string reason) => new(
        Judgment: ExplainJudgment.Unavailable,
        Category: ExplainRubricCategory.Unknown,
        Bucket: CopyBucket.SilentSkip,
        JudgeLatency: TimeSpan.Zero,
        JudgeBackend: $"null:{reason}");
}

/// <summary>
/// Abstraction over the LLM judge sidecar. Phase 1A ships the
/// interface + <see cref="NullLlmJudge"/>; phase 1B wires the real
/// sidecar + circuit breaker.
/// </summary>
public interface ILlmJudge
{
    /// <summary>
    /// Judge one student explanation. MUST be no-throw on the hot
    /// path — a judge failure returns an Unavailable result rather
    /// than raising.
    /// </summary>
    Task<JudgmentResult> JudgeAsync(ExplainRequest request, CancellationToken ct = default);

    /// <summary>Stable identifier for metrics + health checks.</summary>
    string Backend { get; }
}

/// <summary>
/// Graceful-disabled default. Always returns Unavailable; the UI
/// falls through to "skip the prompt" when this is wired.
/// </summary>
public sealed class NullLlmJudge : ILlmJudge
{
    public string Backend => "null";

    public Task<JudgmentResult> JudgeAsync(ExplainRequest request, CancellationToken ct = default)
        => Task.FromResult(JudgmentResult.Unavailable("sidecar-not-configured"));
}

/// <summary>
/// Prompt-fatigue gate per RDY-074 acceptance: "same student never
/// gets > 1 prompt per session." Stateful on the session scope; the
/// pipeline resets it per session.
/// </summary>
public sealed class PromptFatigueGate
{
    private readonly HashSet<string> _studentsPromptedThisSession = new();

    /// <summary>
    /// Returns true when the student has NOT yet been prompted in the
    /// current session. Call once per prompt candidate; if true, mark
    /// them prompted via <see cref="MarkPrompted"/>.
    /// </summary>
    public bool ShouldPrompt(string studentAnonId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentAnonId);
        return !_studentsPromptedThisSession.Contains(studentAnonId);
    }

    public void MarkPrompted(string studentAnonId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(studentAnonId);
        _studentsPromptedThisSession.Add(studentAnonId);
    }

    /// <summary>Reset between sessions.</summary>
    public void Reset() => _studentsPromptedThisSession.Clear();
}
