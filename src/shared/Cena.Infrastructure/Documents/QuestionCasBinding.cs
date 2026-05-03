// =============================================================================
// Cena Platform — Question CAS Binding (CAS-BIND-001, RDY-034, RDY-036)
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
//
// RDY-034 / ADR-0002: Extended with ingestion-gate fields so we can track
// ingestion-time verification outcomes, idempotency hashes, and operator
// overrides without forking the binding doc into a second table.
// =============================================================================

using System.Security.Cryptography;
using System.Text;

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
/// RDY-034 / ADR-0002: State of the CAS verification outcome recorded
/// on the binding document. Used by the ingestion gate to decide whether
/// a question may be approved/published.
/// </summary>
public enum CasBindingStatus
{
    /// <summary>CAS engine confirmed the authored answer is correct.</summary>
    Verified,

    /// <summary>
    /// Subject is non-math or no math content detected. Binding exists for
    /// audit but no CAS verdict was produced. Allowed to proceed.
    /// </summary>
    Unverifiable,

    /// <summary>CAS engine ran and disagreed with the authored answer. Ship-blocker.</summary>
    Failed,

    /// <summary>
    /// A super-admin explicitly overrode a failure/unverifiable binding with a
    /// documented reason + ticket. Heavy audit trail required (RDY-036 §14).
    /// </summary>
    OverriddenByOperator
}

/// <summary>
/// CAS-BIND-001 / RDY-034: Binds a question to the CAS engine and canonical forms
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

    // ── RDY-034 / RDY-036: Ingestion-gate fields ────────────────────────────

    /// <summary>RDY-034: Outcome state used by ingestion gate and approval flow.</summary>
    public CasBindingStatus Status { get; set; } = CasBindingStatus.Verified;

    /// <summary>Raw correct-answer text as authored (pre-NFC, pre-canonicalization).</summary>
    public string CorrectAnswerRaw { get; set; } = "";

    /// <summary>
    /// SHA256 over NFC-normalized <see cref="CorrectAnswerRaw"/>, lowercase hex.
    /// Used as idempotency key so repeated ingestion of the same (question,
    /// answer) pair short-circuits to the cached verification result.
    /// </summary>
    public string CorrectAnswerHash { get; set; } = "";

    /// <summary>CAS verification latency in milliseconds (wall clock).</summary>
    public double LatencyMs { get; set; }

    /// <summary>Failure reason when <see cref="Status"/> is Failed; null otherwise.</summary>
    public string? FailureReason { get; set; }

    /// <summary>
    /// RDY-036 §14: Operator (user id) that submitted an override for a
    /// failed/unverifiable binding. Null unless Status=OverriddenByOperator.
    /// </summary>
    public string? OverrideOperator { get; set; }

    /// <summary>RDY-036 §14: Operator-supplied justification (minimum 20 chars).</summary>
    public string? OverrideReason { get; set; }

    /// <summary>RDY-036 §14: External change-ticket tying the override to a process.</summary>
    public string? OverrideTicket { get; set; }

    /// <summary>
    /// RDY-034: Compute the idempotency hash for a raw answer string.
    /// NFC-normalizes the input, hashes with SHA256, returns lowercase hex.
    /// </summary>
    public static string ComputeAnswerHash(string raw)
    {
        var normalized = (raw ?? string.Empty).Normalize(NormalizationForm.FormC);
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
