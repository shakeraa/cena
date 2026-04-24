// =============================================================================
// Cena Platform — Remediation Micro-Task Templates (REMEDIATION-001)
//
// Pre-authored remediation patterns triggered by misconception detection.
// Each template generates a targeted micro-exercise addressing a specific
// buggy rule (Koedinger tradition, d≈0.2–0.4 per targeted misconception).
// =============================================================================

namespace Cena.Actors.Services;

/// <summary>
/// A remediation micro-task triggered by a misconception detection.
/// </summary>
public record RemediationTask(
    string BuggyRuleId,
    string Title,
    string CounterExamplePrompt,
    string TargetedExercisePrompt,
    string? HintText,
    RemediationDifficulty Difficulty,
    int EstimatedMinutes
);

public enum RemediationDifficulty
{
    Warmup,
    Targeted,
    Transfer
}

/// <summary>
/// Template registry for remediation micro-tasks.
/// Maps buggy-rule IDs to pre-authored remediation exercises.
/// </summary>
public static class RemediationTemplates
{
    private static readonly Dictionary<string, RemediationTask> Templates = new()
    {
        // Algebra misconceptions
        ["DIST-EXP-SUM"] = new(
            "DIST-EXP-SUM",
            "Exponent distribution over addition",
            "Is (a+b)² the same as a²+b²? Try a=2, b=3.",
            "Expand (x+3)² without distributing the exponent.",
            "Remember: (a+b)² = a² + 2ab + b²",
            RemediationDifficulty.Targeted, 3),

        ["SIGN-FLIP-INEQ"] = new(
            "SIGN-FLIP-INEQ",
            "Inequality sign flip on multiplication by negative",
            "If -2x > 6, what happens when you divide both sides by -2?",
            "Solve: -3x < 9. What is the correct inequality direction?",
            "When you multiply or divide by a negative, the inequality flips.",
            RemediationDifficulty.Targeted, 2),

        ["CANCEL-FRACTION-ADD"] = new(
            "CANCEL-FRACTION-ADD",
            "Cancelling terms in fraction addition",
            "Is (a+b)/a the same as 1+b? Try a=2, b=3.",
            "Simplify: (x+5)/x. Can you cancel the x?",
            "You can only cancel factors, not terms added in the numerator.",
            RemediationDifficulty.Targeted, 3),

        ["SQRT-SUM"] = new(
            "SQRT-SUM",
            "Square root of a sum",
            "Is √(a²+b²) the same as a+b? Try a=3, b=4.",
            "Calculate √(9+16). Is it √9 + √16?",
            "√(a²+b²) ≠ a+b. Think Pythagorean theorem.",
            RemediationDifficulty.Targeted, 2),

        ["NEGATIVE-EXPONENT"] = new(
            "NEGATIVE-EXPONENT",
            "Negative exponent as negative number",
            "Is 2⁻³ a negative number? Calculate it.",
            "Evaluate: 5⁻² and (-5)². Are they the same?",
            "x⁻ⁿ = 1/xⁿ, not -xⁿ.",
            RemediationDifficulty.Targeted, 2),

        // Physics misconceptions
        ["VELOCITY-ACCEL-SIGN"] = new(
            "VELOCITY-ACCEL-SIGN",
            "Confusing velocity and acceleration signs in projectile motion",
            "A ball thrown upward: is acceleration negative when velocity is still positive?",
            "A ball is at its highest point. What are v and a?",
            "Acceleration due to gravity is always downward (-g), regardless of velocity direction.",
            RemediationDifficulty.Targeted, 3),

        ["FORCE-VELOCITY-CONFUSION"] = new(
            "FORCE-VELOCITY-CONFUSION",
            "Believing force is required to maintain velocity",
            "A hockey puck sliding on frictionless ice: what forces act on it?",
            "Draw the free-body diagram for a satellite in orbit. Any horizontal forces?",
            "Newton's 1st law: no net force = constant velocity, not zero velocity.",
            RemediationDifficulty.Targeted, 3),

        ["NORMAL-FORCE-EQUALS-WEIGHT"] = new(
            "NORMAL-FORCE-EQUALS-WEIGHT",
            "Normal force always equals weight",
            "On an inclined plane, is the normal force equal to mg?",
            "Find the normal force on a 5kg block on a 30° incline.",
            "Normal force equals the perpendicular component of weight: N = mg·cos(θ).",
            RemediationDifficulty.Targeted, 4),

        // Calculus misconceptions
        ["DERIVATIVE-EXPONENT-RULE"] = new(
            "DERIVATIVE-EXPONENT-RULE",
            "Applying power rule to exponential functions",
            "Is the derivative of 2ˣ equal to x·2ˣ⁻¹?",
            "Find d/dx[eˣ] and d/dx[x³]. Which rule applies to each?",
            "Power rule: d/dx[xⁿ] = nxⁿ⁻¹. Exponential: d/dx[aˣ] = aˣ·ln(a).",
            RemediationDifficulty.Targeted, 3),

        ["CHAIN-RULE-MISSING"] = new(
            "CHAIN-RULE-MISSING",
            "Forgetting the chain rule on composite functions",
            "Is d/dx[sin(2x)] = cos(2x)?",
            "Differentiate f(x) = (3x+1)⁵. Don't forget the inner derivative.",
            "Chain rule: d/dx[f(g(x))] = f'(g(x))·g'(x). The inner derivative matters.",
            RemediationDifficulty.Targeted, 3),

        // RDY-033 additions
        ["CANCEL-COMMON"] = new(
            "CANCEL-COMMON",
            "Cancelling summands like factors",
            "Is (a+b)/a the same as b? Try a=2, b=5.",
            "Simplify (x+5)/x by splitting into two fractions first. Which part simplifies?",
            "Only factors cancel. Split (a+b)/a = a/a + b/a = 1 + b/a.",
            RemediationDifficulty.Targeted, 3),

        ["SIGN-NEGATIVE"] = new(
            "SIGN-NEGATIVE",
            "Distributing a leading negative sign",
            "Is -(a+b) the same as -a+b? Try a=2, b=5.",
            "Expand: -(x+5). The leading minus multiplies every term inside.",
            "-(a+b) = -a - b. Every term inside the parentheses flips sign.",
            RemediationDifficulty.Targeted, 2),

        ["ORDER-OPS"] = new(
            "ORDER-OPS",
            "Order of operations (PEMDAS)",
            "Evaluate 2 + 3 × 4. Which operation comes first?",
            "Compute: 5 + 6 × 2 - 1. Show each step in PEMDAS order.",
            "Multiplication and division bind tighter than addition and subtraction.",
            RemediationDifficulty.Warmup, 2),
    };

    /// <summary>
    /// Get a remediation task for a specific buggy rule.
    /// Returns null if no template exists for this rule.
    /// </summary>
    public static RemediationTask? GetTemplate(string buggyRuleId) =>
        Templates.TryGetValue(buggyRuleId, out var task) ? task : null;

    /// <summary>
    /// Get all available template IDs.
    /// </summary>
    public static IReadOnlyList<string> AllRuleIds => Templates.Keys.ToList();

    /// <summary>
    /// Total number of remediation templates in the registry.
    /// </summary>
    public static int Count => Templates.Count;
}
