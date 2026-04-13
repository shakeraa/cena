// =============================================================================
// Cena Platform — Step Solver Question Document (STEP-003)
// Schema for multi-step algebraic/calculus problems where students solve
// step-by-step with CAS verification at each stage.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// Scaffolding level for a step-solver question, controlling how much
/// guidance the student receives at each step.
/// </summary>
public enum StepScaffoldingLevel
{
    /// <summary>No guidance — student writes each step from scratch.</summary>
    None = 0,

    /// <summary>Faded example shown — some parts blanked out for student to fill.</summary>
    Faded = 1,

    /// <summary>Full worked example visible alongside the blank workspace.</summary>
    Full = 2,

    /// <summary>
    /// SCAFFOLD-001: Productive failure mode (Kapur 2008/2014, d=0.37 on transfer).
    /// Student sees only problem stem + figure. Free-form input, no step slots.
    /// CAS verifies only the final answer. After 2 wrong attempts, re-renders
    /// in Full mode with divergence highlight. Triggered when BKT PLEffective >= 0.8.
    /// </summary>
    Exploratory = 3
}

/// <summary>
/// A single step within a multi-step solution.
/// </summary>
public sealed record SolutionStep
{
    /// <summary>1-based step number within the solution.</summary>
    public int StepNumber { get; init; }

    /// <summary>Instruction text shown to the student (e.g., "Differentiate both sides").</summary>
    public string? Instruction { get; init; }

    /// <summary>
    /// Faded example for scaffolding: a partially completed expression
    /// where blanked sections are marked with ___. Null when scaffolding is None.
    /// </summary>
    public string? FadedExample { get; init; }

    /// <summary>
    /// The expected mathematical expression for this step, in a format
    /// that can be verified by the CAS engine (SymPy-parseable).
    /// </summary>
    public string ExpectedExpression { get; init; } = "";

    /// <summary>
    /// Progressive hints for this step, ordered from least to most revealing.
    /// Empty array if no hints authored.
    /// </summary>
    public string[] Hints { get; init; } = Array.Empty<string>();
}

/// <summary>
/// STEP-003: Multi-step solver question stored as a Marten document.
/// Students solve step-by-step; each step is CAS-verified before proceeding.
/// </summary>
public sealed class StepSolverQuestionDocument
{
    /// <summary>Marten document ID. Format: <c>stepsolver-{guid}</c>.</summary>
    public string Id { get; set; } = "";

    /// <summary>The question stem / problem statement shown to the student.</summary>
    public string Stem { get; set; } = "";

    /// <summary>Subject slug (e.g., "math", "physics").</summary>
    public string Subject { get; set; } = "";

    /// <summary>Concept being assessed (FK to concept graph).</summary>
    public string ConceptId { get; set; } = "";

    /// <summary>
    /// Optional figure specification (function-plot, JSXGraph, or SVG).
    /// Null when the problem has no accompanying diagram.
    /// </summary>
    public object? FigureSpec { get; set; }

    /// <summary>Ordered steps of the solution. Must have at least 2 steps.</summary>
    public IReadOnlyList<SolutionStep> Steps { get; set; } = Array.Empty<SolutionStep>();

    /// <summary>The final answer expression (CAS-verifiable).</summary>
    public string FinalAnswer { get; set; } = "";

    /// <summary>Default scaffolding level for this question.</summary>
    public StepScaffoldingLevel ScaffoldingLevel { get; set; } = StepScaffoldingLevel.None;

    // BagrutAlignment will be added after BAGRUT-ALIGN-001 merges

    /// <summary>Difficulty Elo rating for adaptive selection.</summary>
    public double DifficultyElo { get; set; } = 1500.0;

    /// <summary>Number of student attempts for K-factor decay.</summary>
    public int EloAttemptCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}
