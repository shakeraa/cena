// =============================================================================
// Cena Platform — Exam Simulation Mode (SEC-ASSESS-002)
// Configuration and state management for Bagrut exam simulation sessions.
//
// Key constraints:
// - Questions drawn from a reserved pool (5x exam size, never shown in practice)
// - Timed to match real Bagrut format
// - No hints, no scaffolding, no step-by-step feedback
// - Feedback only shown after submission of entire exam
// - Visibility API detection for tab-switch/minimize events
//
// prr-008 (2026-04-20): Every item surfaced during an exam simulation MUST
// be routed through `IItemDeliveryGate.AssertDeliverable` at the last moment
// before serialisation. `ExamSimulationDelivery.AssertDeliverable` below is
// the ergonomic wrapper used by the HTTP delivery seam; the underlying gate
// enforces the Bagrut-reference-only invariant (CLAUDE.md non-negotiable
// "Bagrut reference-only", ADR-0032, ADR-0043 bagrut-reference-only-enforcement).
// =============================================================================

using Cena.Actors.Content;

namespace Cena.Actors.Assessment;

/// <summary>
/// Exam format configuration matching official Bagrut exam structure.
/// </summary>
public sealed record ExamFormat
{
    /// <summary>Exam code (e.g., "806", "807", "036").</summary>
    public string ExamCode { get; init; } = "";

    /// <summary>Total time allowed in minutes.</summary>
    public int TimeLimitMinutes { get; init; }

    /// <summary>Number of questions in Part A (short problems).</summary>
    public int PartAQuestionCount { get; init; }

    /// <summary>Number of questions in Part B (long problems, student chooses subset).</summary>
    public int PartBQuestionCount { get; init; }

    /// <summary>How many Part B questions the student must answer.</summary>
    public int PartBRequiredCount { get; init; }

    /// <summary>Official Bagrut Math 806 format.</summary>
    public static readonly ExamFormat Bagrut806 = new()
    {
        ExamCode = "806",
        TimeLimitMinutes = 180,
        PartAQuestionCount = 5,
        PartBQuestionCount = 4,
        PartBRequiredCount = 2
    };

    /// <summary>Official Bagrut Math 807 format.</summary>
    public static readonly ExamFormat Bagrut807 = new()
    {
        ExamCode = "807",
        TimeLimitMinutes = 180,
        PartAQuestionCount = 5,
        PartBQuestionCount = 4,
        PartBRequiredCount = 2
    };

    /// <summary>Official Bagrut Physics 036 format.</summary>
    public static readonly ExamFormat BagrutPhysics036 = new()
    {
        ExamCode = "036",
        TimeLimitMinutes = 180,
        PartAQuestionCount = 4,
        PartBQuestionCount = 5,
        PartBRequiredCount = 3
    };

    /// <summary>
    /// PRR-295 — Bagrut English 016 (D-tier, 5pt). Structurally different
    /// from math/physics: reading-comprehension + writing + listening.
    /// First-pass approximation: 4 short-form (Part A — reading
    /// passages + comprehension Q's) + 4 long-form (Part B — writing
    /// + listening), choose 2 of 4 for Part B. Time budget 180 min
    /// matches the Ministry's published spec.
    /// </summary>
    public static readonly ExamFormat BagrutEnglish016 = new()
    {
        ExamCode = "016",
        TimeLimitMinutes = 180,
        PartAQuestionCount = 4,
        PartBQuestionCount = 4,
        PartBRequiredCount = 2,
    };

    public static ExamFormat? FromCode(string examCode) => examCode switch
    {
        "806" => Bagrut806,
        "807" => Bagrut807,
        "036" => BagrutPhysics036,
        "016" => BagrutEnglish016,
        _ => null
    };
}

/// <summary>
/// Session-level state for an exam simulation in progress.
/// </summary>
public sealed class ExamSimulationState
{
    public string SimulationId { get; set; } = "";
    public string StudentId { get; set; } = "";
    public string ExamCode { get; set; } = "";
    public ExamFormat Format { get; set; } = new();

    /// <summary>Question IDs drawn from the reserved pool.</summary>
    public List<string> PartAQuestionIds { get; set; } = new();
    public List<string> PartBQuestionIds { get; set; } = new();

    /// <summary>Which Part B questions the student chose to answer.</summary>
    public List<string> PartBSelectedIds { get; set; } = new();

    /// <summary>Student answers keyed by question ID.</summary>
    public Dictionary<string, string> Answers { get; set; } = new();

    /// <summary>
    /// PRR-299 — first-time-engaged timestamp keyed by the same answerKey
    /// as <see cref="Answers"/> (qid for single-cell, "qid:subpart" for
    /// multi-part). Set on the first SubmitAnswer for that key and
    /// preserved on subsequent edits — the value is "when did the student
    /// FIRST commit an answer for this slot", which is the cleanest proxy
    /// for "when did they engage with this question". Used by the grader
    /// to compute per-question pacing on the result page.
    ///
    /// NOT a streak/loss-aversion mechanic (GD-004 ban). Pacing is a
    /// pedagogical diagnostic — the result page surfaces per-question
    /// time so a student can see their own allocation ("I spent 35 min
    /// on Q3 and 5 min on Q4") for honest self-reflection, never as a
    /// countdown or pressure indicator.
    /// </summary>
    public Dictionary<string, DateTimeOffset> AnswerTimestamps { get; set; } = new();

    /// <summary>
    /// PRR-299 — most-recent-engaged timestamp keyed by answerKey. Updated
    /// on every SubmitAnswer (first call sets it equal to
    /// <see cref="AnswerTimestamps"/>; subsequent edits overwrite). The
    /// grader uses this with <see cref="AnswerTimestamps"/> to derive
    /// "time spent on Q[i] = first(Q[i]) − last(prior question)" per
    /// the PRR-299 spec.
    /// </summary>
    public Dictionary<string, DateTimeOffset> AnswerLastTimestamps { get; set; } = new();

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }

    /// <summary>Phase 2B — accommodation extra-time, in minutes. Default
    /// 0 keeps legacy simulations on standard time. Always added on top
    /// of <see cref="ExamFormat.TimeLimitMinutes"/> so the canonical
    /// time stays auditable.</summary>
    public int ExtraTimeMinutes { get; set; }

    /// <summary>PRR-293 / PRR-292 — calculator policy + formula-sheet
    /// mode resolved at start-time from the BagrutPaperStructure. Stored
    /// on state so the runner doesn't need to re-fetch the structure
    /// on every state poll. Defaults preserve back-compat for state
    /// rows that predate these fields.</summary>
    public string CalculatorPolicy { get; set; } = "Allowed";
    public string FormulaSheetMode { get; set; } = "None";

    /// <summary>
    /// PRR-287 — save-and-resume. When non-null, the run is currently
    /// paused; the deadline computation pauses while this is set and
    /// resumes when <see cref="ResumeAsync"/> is called. Real-Ministry-
    /// strict mode is "no pause"; this is a practice-mode affordance.
    /// </summary>
    public DateTimeOffset? PausedAt { get; set; }

    /// <summary>
    /// PRR-287 — accumulated paused duration in milliseconds. Added
    /// to the effective deadline computation so a 60-min pause shifts
    /// the end time by 60 min; paused intervals don't count against
    /// the time-limit budget. Per-resume increment.
    /// </summary>
    public long TotalPausedMs { get; set; }

    /// <summary>True iff this run is currently paused (PRR-287).</summary>
    public bool IsPaused => PausedAt.HasValue;

    public DateTimeOffset Deadline =>
        StartedAt
            .AddMinutes(Format.TimeLimitMinutes + ExtraTimeMinutes)
            // PRR-287 — accumulated pauses extend the deadline by exactly
            // the paused duration. While paused, the deadline keeps
            // sliding forward (callers should consult IsPaused too if
            // they need the "is the timer ticking right now" question).
            .AddMilliseconds(TotalPausedMs)
            // While currently paused, also extend by the elapsed-since-
            // pause so the displayed deadline reflects "won't expire
            // while you're paused" reality. The actual TotalPausedMs
            // isn't bumped until Resume — this read-only adjustment is
            // for the UI's countdown only.
            .AddMilliseconds(IsPaused ? Math.Max(0, (DateTimeOffset.UtcNow - PausedAt!.Value).TotalMilliseconds) : 0);

    public bool IsExpired(DateTimeOffset now) => !IsPaused && now >= Deadline;
    public bool IsSubmitted => SubmittedAt.HasValue;

    /// <summary>Tab-switch/minimize events detected via Visibility API.</summary>
    public List<VisibilityEvent> VisibilityEvents { get; set; } = new();

    /// <summary>Variant seed for this simulation (SEC-ASSESS-001).</summary>
    public int VariantSeed { get; set; }
}

/// <summary>
/// Record of a browser visibility change during an exam simulation.
/// </summary>
public sealed record VisibilityEvent(
    DateTimeOffset Timestamp,
    string State,
    TimeSpan DurationAway);

/// <summary>
/// Exam simulation result with readiness assessment.
/// </summary>
public sealed record ExamSimulationResult
{
    public string SimulationId { get; init; } = "";
    public string StudentId { get; init; } = "";
    public string ExamCode { get; init; } = "";
    public int TotalQuestions { get; init; }
    public int QuestionsAttempted { get; init; }
    public int QuestionsCorrect { get; init; }
    public double ScorePercent { get; init; }
    public TimeSpan TimeTaken { get; init; }
    public TimeSpan TimeLimit { get; init; }

    /// <summary>Bagrut readiness confidence interval (e.g., 65-78%).</summary>
    public double ReadinessLowerBound { get; init; }
    public double ReadinessUpperBound { get; init; }
    public string ReadinessLevel { get; init; } = "";

    /// <summary>Number of tab-switch events detected.</summary>
    public int VisibilityWarnings { get; init; }
}

/// <summary>
/// prr-008 delivery seam for exam-simulation items. The HTTP endpoint that
/// serves a next-question payload to the student MUST call
/// <see cref="AssertDeliverable"/> immediately before serialising the item
/// onto the wire. Callers that type their payload as
/// <see cref="Deliverable{T}"/> get compile-time enforcement too.
/// </summary>
/// <remarks>
/// Threading <see cref="Provenance"/> through every item-bank read path is
/// Sprint-2 scope (prr-008 "scope cuts"). Today, exam-simulation callers
/// construct the <see cref="Provenance"/> at the delivery seam from the
/// item's on-disk metadata (BagrutRecreationAggregate.RecreationId →
/// AiRecreated, teacher upload id → TeacherAuthoredOriginal, Ministry
/// code → MinistryBagrut). The gate then decides whether to let it
/// through. Any path that loses provenance visibility is expected to
/// default-fail closed by constructing a MinistryBagrut provenance so
/// the gate throws loudly rather than silently leaking.
/// </remarks>
public static class ExamSimulationDelivery
{
    /// <summary>
    /// Chokepoint call that enforces the Bagrut-reference-only invariant
    /// at exam-simulation delivery. Thin wrapper around
    /// <see cref="IItemDeliveryGate.AssertDeliverable"/> with context
    /// derived from <see cref="ExamSimulationState"/>.
    /// </summary>
    public static void AssertDeliverable(
        IItemDeliveryGate gate,
        ExamSimulationState state,
        string itemId,
        Provenance provenance,
        string tenantId,
        string actorId)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(state);
        gate.AssertDeliverable(
            provenance: provenance,
            itemId: itemId,
            sessionId: state.SimulationId,
            tenantId: tenantId,
            actorId: actorId);
    }
}
