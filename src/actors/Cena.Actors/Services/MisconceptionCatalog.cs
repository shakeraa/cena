// =============================================================================
// Cena Platform — Misconception Catalog (MISC-001)
//
// 15 empirically-backed buggy rules (Koedinger, ASSISTments tradition).
// Each entry: ID, subject, description, counter-example, difficulty level.
// Session-scoped tally per ADR-0003 (never persisted to student profile).
// =============================================================================

namespace Cena.Actors.Services;

/// <summary>
/// A cataloged misconception (buggy rule) with empirical backing.
/// </summary>
public record MisconceptionEntry(
    string Id,
    string Subject,
    string TopicArea,
    string Description,
    string StudentManifestation,
    string CounterExample,
    string CorrectReasoning,
    string AcademicSource
);

/// <summary>
/// Session-scoped misconception tally (per ADR-0003: never persisted beyond session).
/// </summary>
public record SessionMisconceptionTally(
    string SessionId,
    Dictionary<string, int> DetectionCounts,
    Dictionary<string, bool> Remediated
);

/// <summary>
/// Catalog of 18 empirically-documented misconceptions across math and physics.
/// (15 original + 3 added by RDY-033: CANCEL-COMMON, SIGN-NEGATIVE, ORDER-OPS)
/// </summary>
public static class MisconceptionCatalog
{
    public static readonly IReadOnlyList<MisconceptionEntry> Entries = new List<MisconceptionEntry>
    {
        // ── Algebra (5) ──
        new("DIST-EXP-SUM", "math", "algebra",
            "Distributing exponent over addition: (a+b)² → a²+b²",
            "Student writes (x+3)² = x²+9",
            "(2+3)² = 25, but 2²+3² = 13",
            "(a+b)² = a² + 2ab + b² (binomial expansion)",
            "Koedinger et al. 1997; ASSISTments data"),

        new("SIGN-FLIP-INEQ", "math", "algebra",
            "Forgetting to flip inequality when multiplying by negative",
            "Student writes -2x > 6 → x > -3",
            "-2(−4) = 8 > 6 but −4 is not > −3",
            "Multiplying both sides by negative reverses the inequality",
            "Booth & Koedinger 2008"),

        new("CANCEL-FRACTION-ADD", "math", "algebra",
            "Cancelling additive terms in fractions",
            "Student writes (x+5)/x = 5",
            "(2+5)/2 = 3.5, not 5",
            "Only multiplicative factors cancel, not additive terms",
            "Heffernan & Heffernan 2014"),

        new("SQRT-SUM", "math", "algebra",
            "Square root of sum equals sum of square roots",
            "Student writes √(9+16) = √9+√16 = 7",
            "√25 = 5, not 3+4 = 7",
            "√(a+b) ≠ √a+√b; only √(a·b) = √a·√b",
            "Matz 1982 (mal-rules)"),

        new("NEGATIVE-EXPONENT", "math", "algebra",
            "Interpreting negative exponent as negative number",
            "Student writes 2⁻³ = -8",
            "2⁻³ = 1/2³ = 1/8 = 0.125",
            "x⁻ⁿ = 1/xⁿ (reciprocal, not negation)",
            "Pitta-Pantazi & Christou 2011"),

        // ── Algebra (3 added by RDY-033) ──
        new("CANCEL-COMMON", "math", "algebra",
            "Cancelling a summand as if it were a factor: (a+b)/a → b",
            "Student writes (x+5)/x = 5",
            "(2+5)/2 = 3.5, not 5",
            "Only factors (multiplicative) cancel; summands (additive) cannot be cancelled.",
            "Matz 1982 (mal-rules); ASSISTments error logs"),

        new("SIGN-NEGATIVE", "math", "algebra",
            "Failing to distribute a leading negative across all summands: -(a+b) → -a+b",
            "Student writes -(x+5) = -x+5",
            "-(2+5) = -7, not -2+5 = 3",
            "A leading minus multiplies every term inside the parentheses: -(a+b) = -a - b.",
            "Booth & Koedinger 2008"),

        new("ORDER-OPS", "math", "arithmetic",
            "Evaluating arithmetic left-to-right instead of PEMDAS: 2+3·4 → 20",
            "Student writes 2 + 3 × 4 = 20 (treated as (2+3)·4)",
            "PEMDAS says multiplication binds tighter: 2 + (3·4) = 14",
            "Parentheses, Exponents, Multiplication/Division, Addition/Subtraction.",
            "Kieran 2007 (order-of-operations misconceptions)"),

        // ── Trigonometry (2) ──
        new("TRIG-INVERSE-RECIPROCAL", "math", "trigonometry",
            "Confusing inverse trig with reciprocal trig",
            "Student writes sin⁻¹(x) = 1/sin(x)",
            "sin⁻¹(0.5) = 30° but 1/sin(0.5) = 2.09",
            "sin⁻¹ is arcsin (angle), not cosecant (reciprocal)",
            "Weber 2005"),

        new("TRIG-ANGLE-RADIAN", "math", "trigonometry",
            "Mixing degrees and radians in calculations",
            "Student enters sin(90) expecting 1, gets 0.894",
            "sin(90°) = 1 but sin(90 radians) ≈ 0.894",
            "Always check: calculator in degree or radian mode",
            "Bressoud et al. 2004"),

        // ── Calculus (3) ──
        new("DERIVATIVE-EXP-POWER", "math", "calculus",
            "Applying power rule to exponential functions",
            "Student writes d/dx[2ˣ] = x·2ˣ⁻¹",
            "d/dx[2ˣ] = 2ˣ·ln(2) ≈ 0.693·2ˣ",
            "Power rule: xⁿ. Exponential rule: aˣ → aˣ·ln(a)",
            "Selden et al. 1994"),

        new("CHAIN-RULE-MISSING", "math", "calculus",
            "Omitting the chain rule on composite functions",
            "Student writes d/dx[sin(2x)] = cos(2x)",
            "d/dx[sin(2x)] = cos(2x)·2 = 2cos(2x)",
            "d/dx[f(g(x))] = f'(g(x))·g'(x)",
            "Thompson & Silverman 2008"),

        new("INTEGRAL-CONSTANT", "math", "calculus",
            "Forgetting the constant of integration",
            "Student writes ∫2x dx = x²",
            "∫2x dx = x² + C (the family of antiderivatives)",
            "Every indefinite integral has +C",
            "Ferrini-Mundy & Graham 1991"),

        // ── Physics (5) ──
        new("FORCE-VELOCITY", "physics", "mechanics",
            "Believing force is needed to maintain velocity",
            "Student says a sliding hockey puck must have a forward force",
            "On frictionless ice, the puck slides at constant v with zero net force",
            "Newton's 1st law: no net force = constant velocity",
            "Halloun & Hestenes 1985 (FCI)"),

        new("NORMAL-EQUALS-WEIGHT", "physics", "mechanics",
            "Normal force always equals weight",
            "Student writes N = mg on an inclined plane",
            "N = mg·cos(θ); on a 30° plane, N = 0.866·mg",
            "Normal force is the perpendicular component of contact force",
            "Thornton & Sokoloff 1998"),

        new("VELOCITY-ACCEL-SIGN", "physics", "kinematics",
            "Confusing velocity and acceleration signs in projectile motion",
            "Student says acceleration is zero at the top of a throw",
            "At apex: v=0 but a=−g=−9.8 m/s² (always downward)",
            "Acceleration is change in velocity, not velocity itself",
            "Trowbridge & McDermott 1980"),

        new("HEAVIER-FALLS-FASTER", "physics", "mechanics",
            "Heavier objects fall faster in a vacuum",
            "Student predicts a bowling ball hits ground before a tennis ball",
            "Apollo 15 feather-hammer drop: both hit simultaneously",
            "In vacuum, all objects have the same gravitational acceleration",
            "Galileo; confirmed by Hammer & Feather experiment"),

        new("CURRENT-CONSUMED", "physics", "electricity",
            "Current is 'used up' by components in a circuit",
            "Student says current after a bulb is less than before",
            "Ammeter reads same current before and after the bulb",
            "Current is conserved (Kirchhoff's current law); voltage drops, not current",
            "McDermott & Shaffer 1992"),
    };

    /// <summary>Get a misconception entry by ID.</summary>
    public static MisconceptionEntry? GetById(string id) =>
        Entries.FirstOrDefault(e => e.Id == id);

    /// <summary>Get all entries for a subject.</summary>
    public static IReadOnlyList<MisconceptionEntry> GetBySubject(string subject) =>
        Entries.Where(e => e.Subject == subject).ToList();
}
