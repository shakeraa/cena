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
// =============================================================================

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

    public static ExamFormat? FromCode(string examCode) => examCode switch
    {
        "806" => Bagrut806,
        "807" => Bagrut807,
        "036" => BagrutPhysics036,
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

    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset Deadline => StartedAt.AddMinutes(Format.TimeLimitMinutes);
    public bool IsExpired(DateTimeOffset now) => now >= Deadline;
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
