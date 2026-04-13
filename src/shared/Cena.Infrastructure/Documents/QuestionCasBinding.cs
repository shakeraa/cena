// =============================================================================
// Cena Platform — Question CAS Binding (CAS-BIND-001)
// Locks each question to the CAS engine used during authoring.
//
// WHY: Different CAS engines have different canonical forms for equivalent
// expressions. A question authored and verified in SymPy must always be
// graded by SymPy. Silent fallback to a different engine would produce
// false negatives (marking correct answers wrong) because the canonical
// forms don't match. Per Dr. Rami Khalil's recommendation (#43).
//
// DEGRADED MODE: If the bound engine is down (circuit breaker open), the
// student sees "answer recorded, verification pending" — never a wrong
// grade from a fallback engine.
// =============================================================================

namespace Cena.Infrastructure.Documents;

/// <summary>
/// CAS engine identifier.
/// </summary>
public enum CasEngine
{
    /// <summary>SymPy (Python sidecar). Default for new questions.</summary>
    SymPy,

    /// <summary>Giac (C++ CAS, alternative verifier).</summary>
    Giac,

    /// <summary>MathNet (in-process .NET, fast but limited symbolic).</summary>
    MathNet
}

/// <summary>
/// How two expressions should be compared for equivalence.
/// </summary>
public enum EquivalenceMode
{
    /// <summary>Full symbolic equivalence (simplify both, compare AST).</summary>
    Symbolic,

    /// <summary>Numeric evaluation at multiple points, within tolerance 1e-9.</summary>
    Numeric,

    /// <summary>Pattern matching (regex/template). For non-math answers.</summary>
    Pattern
}

/// <summary>
/// CAS-BIND-001: Binds a question to the CAS engine and canonical forms
/// used during authoring. Stored alongside the question document.
/// </summary>
public sealed class QuestionCasBinding
{
    /// <summary>Marten document ID. Same as QuestionDocument.Id.</summary>
    public string Id { get; set; } = "";

    /// <summary>Question this binding belongs to.</summary>
    public string QuestionId { get; set; } = "";

    /// <summary>CAS engine used to author and verify this question.</summary>
    public CasEngine Engine { get; set; } = CasEngine.SymPy;

    /// <summary>Canonical form of the final answer as produced by the bound engine.</summary>
    public string CanonicalAnswer { get; set; } = "";

    /// <summary>Canonical forms for each step (for step-solver questions). Empty for MCQ.</summary>
    public IReadOnlyList<string> StepCanonicals { get; set; } = Array.Empty<string>();

    /// <summary>How to compare student answers against the canonical form.</summary>
    public EquivalenceMode EquivalenceMode { get; set; } = EquivalenceMode.Symbolic;

    /// <summary>When this binding was created or last re-verified.</summary>
    public DateTimeOffset VerifiedAt { get; set; }

    /// <summary>Whether the nightly batch has flagged a cross-engine disagreement.</summary>
    public bool HasCrossEngineDisagreement { get; set; }

    /// <summary>Details of the disagreement, if any (for human review).</summary>
    public string? DisagreementDetails { get; set; }
}
