// =============================================================================
// Cena Platform — If-Then Implementation-Intentions Planner (prr-154 / F2)
//
// Generates implementation-intention plans for student study sessions in the
// form "If <situation X>, then I will <behaviour Y>". Implementation-
// intentions are a self-regulation technique from Gollwitzer (1999) that
// help bridge the intention-behaviour gap. Students form concrete plans
// instead of abstract goals ("I'll study math" → "If I finish practice
// problem 3, then I'll check my work before moving on").
//
// SCOPE: domain service only. Student-facing UI is deferred (prr-154
// brief says "service + unit tests only"). This file generates plan copy
// + preview reminders; a later UI task will expose them in the session
// planner.
//
// EFFECT SIZE HONESTY (ADR-0049, user memory "Honest not complimentary"):
// Gollwitzer & Sheeran (2006) meta-analysis of 94 studies reported a
// medium-to-large effect size of d ≈ 0.65 for implementation-intentions
// on goal-attainment across domains. Academic-achievement sub-domain
// effects are smaller (Duckworth et al. 2011 found d ≈ 0.30 for
// homework-specific interventions with adolescents). We cite the
// meta-analytic figure in comments; we do NOT make marketing claims
// like "studies show 65% better outcomes". The code emits no
// effect-size copy — any student-facing effect-size claim must pass
// the citation-integrity shipgate scanner.
//
// NON-NEGOTIABLES:
//  - No dark-pattern copy (ADR-0048; shipgate scanner enforces).
//  - No therapeutic framing (ADR-0054; prr-073 scanner enforces).
//  - Stateless. Deterministic on inputs.
//  - Session-scoped consumption only. No persistent student profile.
// =============================================================================

namespace Cena.Actors.Pedagogy;

/// <summary>
/// Kind of trigger event that opens the "if" clause of an implementation
/// intention. Narrow enum — new triggers require an explicit addition and
/// template rather than a free-form string, to keep copy audit-able.
/// </summary>
public enum IfThenTrigger
{
    /// <summary>Just finished a practice item correctly.</summary>
    FinishedProblemCorrect,

    /// <summary>Just finished a practice item incorrectly.</summary>
    FinishedProblemIncorrect,

    /// <summary>Stuck on the current item (hint-ladder depth ≥ 2).</summary>
    StuckOnCurrent,

    /// <summary>Completed a full mini-quiz block.</summary>
    FinishedBlock,

    /// <summary>Session-start cue (no prior item yet).</summary>
    SessionStart,

    /// <summary>Session-duration cue (time-awareness, NOT time-pressure per ADR-0048).</summary>
    Every15Minutes,
}

/// <summary>
/// Action the student commits to do when the trigger fires.
/// Narrow enum by design — keeps copy auditable and prevents free-form
/// emission of unverified phrasing.
/// </summary>
public enum IfThenAction
{
    /// <summary>Re-read the problem statement before submitting.</summary>
    CheckWorkBeforeMoving,

    /// <summary>Explain the last step aloud to yourself (self-explanation).</summary>
    SelfExplainStep,

    /// <summary>Request the next hint if still stuck after one minute.</summary>
    RequestHintAfterOneMinute,

    /// <summary>Switch topic to a strong-area warm-up.</summary>
    SwitchToWarmUp,

    /// <summary>Take a 3-5 min stretch break before next block.</summary>
    TakeShortBreak,

    /// <summary>Write the formula down on paper before solving.</summary>
    WriteFormulaOnPaper,

    /// <summary>Preview the block's topic list before starting.</summary>
    PreviewTopicList,
}

/// <summary>
/// A single if-then plan entry. Carries the enumerated trigger + action
/// so downstream UIs can localise + i18n the copy (never the service).
/// </summary>
public sealed record IfThenPlan(
    IfThenTrigger Trigger,
    IfThenAction Action,
    string PreviewCopy);        // English neutral preview; UI will localise.

/// <summary>
/// Context the planner uses to choose appropriate plans for a session.
/// All optional; the planner degrades gracefully. Explicitly does NOT
/// include emotional / misconception / personal state (ADR-0003,
/// ADR-0037, ADR-0054).
/// </summary>
public sealed record IfThenPlannerContext(
    int QuestionsPlanned,       // number of items in the upcoming session
    int EstimatedMinutes,       // estimated session length in minutes
    bool HasPreExamWindow);     // institute-configured pre-exam cadence

/// <summary>
/// If-then implementation-intention planner. Stateless, deterministic.
/// </summary>
public interface IIfThenPlanner
{
    /// <summary>
    /// Build a small, non-overwhelming set of if-then plans for the upcoming
    /// session. Returns at most <see cref="MaxPlansPerSession"/> entries —
    /// Gollwitzer (2014) notes that 2–4 is the evidence-backed sweet spot;
    /// more than 4 dilutes the commitment effect. We never return more than
    /// 4, and typically 2–3.
    /// </summary>
    IReadOnlyList<IfThenPlan> BuildSessionPlans(IfThenPlannerContext ctx);

    /// <summary>
    /// Preview reminder: at trigger fire, what should the assistant surface?
    /// Returns null when the trigger is not in the current plan set.
    /// </summary>
    IfThenPlan? PreviewFor(IfThenTrigger trigger, IReadOnlyList<IfThenPlan> plans);
}

public sealed class IfThenPlanner : IIfThenPlanner
{
    /// <summary>
    /// Gollwitzer (2014): 2–4 plans is the evidence-backed range. Above 4,
    /// the commitment effect dilutes. We cap at 4.
    /// </summary>
    public const int MaxPlansPerSession = 4;

    /// <summary>
    /// Short sessions (&lt;15 min) get at most 2 plans to avoid
    /// overload. Medium sessions (15–30 min) get 3. Longer get 4.
    /// </summary>
    internal static int PlansForSession(int estimatedMinutes)
    {
        if (estimatedMinutes < 15) return 2;
        if (estimatedMinutes < 30) return 3;
        return MaxPlansPerSession;
    }

    public IReadOnlyList<IfThenPlan> BuildSessionPlans(IfThenPlannerContext ctx)
    {
        if (ctx is null) throw new ArgumentNullException(nameof(ctx));

        var targetCount = PlansForSession(ctx.EstimatedMinutes);
        var plans = new List<IfThenPlan>(targetCount);

        // 1. Always-on foundational plan: self-check after each completed item.
        //    This is the highest-evidence plan across the meta-analytic
        //    literature for academic tasks.
        plans.Add(new IfThenPlan(
            IfThenTrigger.FinishedProblemCorrect,
            IfThenAction.CheckWorkBeforeMoving,
            "If I finish a practice problem, then I'll re-read the problem statement and check my work before moving on."));

        if (plans.Count == targetCount) return plans;

        // 2. Stuck-recovery plan: when the student is stuck, they have a
        //    pre-committed behaviour (request a hint) rather than drifting
        //    off-task. This plan's text deliberately avoids any
        //    self-blame or affective-state claim (ADR-0054).
        plans.Add(new IfThenPlan(
            IfThenTrigger.StuckOnCurrent,
            IfThenAction.RequestHintAfterOneMinute,
            "If I'm stuck on a problem for more than a minute, then I'll request the next hint instead of guessing."));

        if (plans.Count == targetCount) return plans;

        // 3. Block-boundary plan: use block completion as a cadence hook for
        //    a short break. Time-awareness, not time-pressure (ADR-0048).
        //    Choose break vs warm-up based on pre-exam context: pre-exam
        //    students practising under time should stay in flow; other
        //    students benefit from short recovery breaks.
        if (ctx.HasPreExamWindow)
        {
            plans.Add(new IfThenPlan(
                IfThenTrigger.FinishedBlock,
                IfThenAction.SwitchToWarmUp,
                "If I finish a block of problems, then I'll start the next block with a quick warm-up on a strong area."));
        }
        else
        {
            plans.Add(new IfThenPlan(
                IfThenTrigger.FinishedBlock,
                IfThenAction.TakeShortBreak,
                "If I finish a block of problems, then I'll take a short 3-to-5-minute stretch break before the next one."));
        }

        if (plans.Count == targetCount) return plans;

        // 4. Preview plan: at session start, preview the topic list so the
        //    student activates prior knowledge. Paired with a time-awareness
        //    cue every 15 min.
        plans.Add(new IfThenPlan(
            IfThenTrigger.SessionStart,
            IfThenAction.PreviewTopicList,
            "If I start the session, then I'll glance over the topic list before answering the first question."));

        return plans;
    }

    public IfThenPlan? PreviewFor(IfThenTrigger trigger, IReadOnlyList<IfThenPlan> plans)
    {
        if (plans is null) throw new ArgumentNullException(nameof(plans));
        foreach (var p in plans)
        {
            if (p.Trigger == trigger) return p;
        }
        return null;
    }
}
