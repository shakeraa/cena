// =============================================================================
// Cena Platform — Error Pattern Matcher contracts (RDY-033, ADR-0031)
//
// Per ADR-0031: each buggy rule in MisconceptionCatalog is a first-class
// IErrorPatternMatcher. The matcher receives (questionStem, correctAnswer,
// studentAnswer) and reports whether the student's answer is symbolically
// equivalent to what the buggy rule would produce. CAS verification runs
// through ICasRouterService (ADR-0002 — the sole correctness oracle).
//
// Matchers are pure query services: no store writes, no event emission.
// =============================================================================

namespace Cena.Actors.Services.ErrorPatternMatching;

/// <summary>
/// Input to a pattern matcher — everything required to decide whether a
/// student's wrong answer is consistent with a specific buggy rule.
/// </summary>
public sealed record ErrorPatternMatchContext(
    string QuestionStem,
    string CorrectAnswer,
    string StudentAnswer,
    string Subject,
    string? ConceptId);

/// <summary>
/// Result of a single matcher's attempt to identify a buggy-rule application.
/// </summary>
/// <param name="Matched">True when Confidence ≥ 0.5.</param>
/// <param name="BuggyRuleId">Catalog ID if Matched; null otherwise.</param>
/// <param name="Confidence">
/// 1.0 — CAS reports exact symbolic equivalence with the buggy transform output.
/// 0.7 — numerical-tolerance match (for arithmetic answers).
/// 0.0 — no match, stem did not fit the rule's shape, or CAS error.
/// </param>
/// <param name="StudentTransformation">
/// Human-readable description of what the student appears to have done
/// (e.g. "student wrote x^2 + 9 instead of x^2 + 6x + 9"). Empty if not Matched.
/// </param>
/// <param name="Notes">Optional diagnostic for unmatched / error cases.</param>
/// <param name="ElapsedMs">Wall-clock time spent in this matcher.</param>
/// <param name="Engine">
/// Which oracle produced the decision: "MathNet", "SymPy", "heuristic", or "none".
/// </param>
public sealed record ErrorPatternMatchResult(
    bool Matched,
    string? BuggyRuleId,
    double Confidence,
    string StudentTransformation,
    string? Notes,
    double ElapsedMs,
    string Engine)
{
    /// <summary>Zero-confidence "did not match" result.</summary>
    public static ErrorPatternMatchResult NoMatch(string ruleId, string reason, double elapsedMs, string engine = "none") =>
        new(false, null, 0.0, string.Empty, $"{ruleId}: {reason}", elapsedMs, engine);
}

/// <summary>
/// One buggy-rule matcher. Each implementation targets exactly one rule in
/// MisconceptionCatalog and must declare the catalog ID and subject scope
/// so the engine can filter matchers by subject before spending CAS budget.
/// </summary>
public interface IErrorPatternMatcher
{
    /// <summary>Catalog ID from MisconceptionCatalog (e.g. "DIST-EXP-SUM").</summary>
    string BuggyRuleId { get; }

    /// <summary>Subject scope ("math", "physics", etc.) — used for short-circuit.</summary>
    string Subject { get; }

    /// <summary>
    /// Attempt to classify the student's answer as an application of this buggy rule.
    /// Implementations must respect <paramref name="ct"/> for the engine's 100ms budget.
    /// </summary>
    Task<ErrorPatternMatchResult> TryMatchAsync(ErrorPatternMatchContext context, CancellationToken ct);
}
