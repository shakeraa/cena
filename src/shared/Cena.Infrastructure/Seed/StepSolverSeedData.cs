// =============================================================================
// Cena Platform — Step Solver Seed Questions (STEP-005)
//
// 10 step-solver questions across algebra, calculus, and trigonometry.
// Each question has a canonical step trace for CAS verification.
// =============================================================================

using Cena.Infrastructure.Documents;

namespace Cena.Infrastructure.Seed;

public static class StepSolverSeedData
{
    public static readonly StepSolverQuestionDocument[] Questions =
    [
        // ── Algebra (4) ──
        new()
        {
            Id = "step-alg-001",
            Prompt = "Solve for x: 2x + 5 = 13",
            Subject = "math",
            Topic = "algebra-linear-equations",
            Difficulty = 1,
            CanonicalSteps = ["2x + 5 = 13", "2x = 8", "x = 4"],
            FinalAnswer = "x = 4",
            TrackCode = "MATH-BAGRUT-806",
        },
        new()
        {
            Id = "step-alg-002",
            Prompt = "Solve for x: x² - 5x + 6 = 0",
            Subject = "math",
            Topic = "algebra-quadratic-equations",
            Difficulty = 2,
            CanonicalSteps = ["x² - 5x + 6 = 0", "(x - 2)(x - 3) = 0", "x = 2 or x = 3"],
            FinalAnswer = "x = 2, x = 3",
            TrackCode = "MATH-BAGRUT-806",
        },
        new()
        {
            Id = "step-alg-003",
            Prompt = "Simplify: (x² - 9) / (x + 3)",
            Subject = "math",
            Topic = "algebra-polynomial-operations",
            Difficulty = 2,
            CanonicalSteps = ["(x² - 9) / (x + 3)", "((x+3)(x-3)) / (x+3)", "x - 3"],
            FinalAnswer = "x - 3",
            TrackCode = "MATH-BAGRUT-806",
        },
        new()
        {
            Id = "step-alg-004",
            Prompt = "Solve the inequality: 3x - 7 > 2x + 1",
            Subject = "math",
            Topic = "algebra-inequalities",
            Difficulty = 1,
            CanonicalSteps = ["3x - 7 > 2x + 1", "3x - 2x > 1 + 7", "x > 8"],
            FinalAnswer = "x > 8",
            TrackCode = "MATH-BAGRUT-806",
        },

        // ── Calculus (3) ──
        new()
        {
            Id = "step-calc-001",
            Prompt = "Find dy/dx: y = 3x⁴ - 2x² + 7x - 5",
            Subject = "math",
            Topic = "calculus-derivatives-rules",
            Difficulty = 2,
            CanonicalSteps = ["y = 3x⁴ - 2x² + 7x - 5", "dy/dx = 12x³ - 4x + 7"],
            FinalAnswer = "12x³ - 4x + 7",
            TrackCode = "MATH-BAGRUT-806",
        },
        new()
        {
            Id = "step-calc-002",
            Prompt = "Evaluate: ∫(2x + 3) dx",
            Subject = "math",
            Topic = "calculus-integrals-basics",
            Difficulty = 1,
            CanonicalSteps = ["∫(2x + 3) dx", "x² + 3x + C"],
            FinalAnswer = "x² + 3x + C",
            TrackCode = "MATH-BAGRUT-806",
        },
        new()
        {
            Id = "step-calc-003",
            Prompt = "Find the limit: lim(x→2) (x² - 4)/(x - 2)",
            Subject = "math",
            Topic = "calculus-limits",
            Difficulty = 2,
            CanonicalSteps = ["lim(x→2) (x²-4)/(x-2)", "lim(x→2) (x+2)(x-2)/(x-2)", "lim(x→2) (x+2)", "4"],
            FinalAnswer = "4",
            TrackCode = "MATH-BAGRUT-806",
        },

        // ── Trigonometry (3) ──
        new()
        {
            Id = "step-trig-001",
            Prompt = "Solve: sin(x) = 1/2 for 0 ≤ x ≤ 2π",
            Subject = "math",
            Topic = "trigonometry-equations",
            Difficulty = 2,
            CanonicalSteps = ["sin(x) = 1/2", "x = π/6 or x = 5π/6"],
            FinalAnswer = "x = π/6, 5π/6",
            TrackCode = "MATH-BAGRUT-806",
        },
        new()
        {
            Id = "step-trig-002",
            Prompt = "Prove: sin²(x) + cos²(x) = 1",
            Subject = "math",
            Topic = "trigonometry-identities",
            Difficulty = 3,
            CanonicalSteps = [
                "On the unit circle: x² + y² = 1",
                "cos(θ) = x, sin(θ) = y",
                "cos²(θ) + sin²(θ) = x² + y² = 1"
            ],
            FinalAnswer = "QED",
            TrackCode = "MATH-BAGRUT-806",
        },
        new()
        {
            Id = "step-trig-003",
            Prompt = "Simplify: (1 - cos²(x)) / sin(x)",
            Subject = "math",
            Topic = "trigonometry-identities",
            Difficulty = 2,
            CanonicalSteps = [
                "(1 - cos²(x)) / sin(x)",
                "sin²(x) / sin(x)",
                "sin(x)"
            ],
            FinalAnswer = "sin(x)",
            TrackCode = "MATH-BAGRUT-806",
        },
    ];
}
