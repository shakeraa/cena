// =============================================================================
// Cena Platform — CAS Engine Contracts (CAS-001)
//
// Shared types for the 3-tier CAS verification pipeline:
//   Tier 1: MathNet.Symbolics (in-process, .NET, arithmetic/algebra)
//   Tier 2: SymPy sidecar (NATS, Python, calculus/trig/ODE)
//   Fallback: MathNet for simple checks when SymPy is unavailable
// =============================================================================

namespace Cena.Actors.Cas;

/// <summary>
/// Type of CAS verification operation.
/// </summary>
public enum CasOperation
{
    /// <summary>Two expressions are mathematically equivalent.</summary>
    Equivalence,

    /// <summary>A transformation from expression A to B preserves equality.</summary>
    StepValidity,

    /// <summary>Two numerical results are within tolerance (ε = 1e-9).</summary>
    NumericalTolerance,

    /// <summary>An expression is in canonical simplified form.</summary>
    NormalForm,

    /// <summary>Solve an equation and return the solution set.</summary>
    Solve
}

/// <summary>
/// CAS verification request.
/// </summary>
public record CasVerifyRequest(
    CasOperation Operation,
    string ExpressionA,
    string? ExpressionB,
    string? Variable,
    double Tolerance = 1e-9
);

/// <summary>
/// CAS verification result.
/// </summary>
public record CasVerifyResult(
    bool Verified,
    CasOperation Operation,
    string Engine,
    string? SimplifiedA,
    string? SimplifiedB,
    string? ErrorMessage,
    double LatencyMs
)
{
    public static CasVerifyResult Success(CasOperation op, string engine, double latencyMs,
        string? simplifiedA = null, string? simplifiedB = null) =>
        new(true, op, engine, simplifiedA, simplifiedB, null, latencyMs);

    public static CasVerifyResult Failure(CasOperation op, string engine, double latencyMs,
        string errorMessage) =>
        new(false, op, engine, null, null, errorMessage, latencyMs);

    public static CasVerifyResult Error(CasOperation op, string engine, double latencyMs,
        string errorMessage) =>
        new(false, op, engine, null, null, $"[ERROR] {errorMessage}", latencyMs);
}

/// <summary>
/// Routing tier for CAS operations.
/// </summary>
public enum CasTier
{
    /// <summary>MathNet.Symbolics in-process (.NET, fast, limited).</summary>
    MathNet,

    /// <summary>SymPy sidecar via NATS (Python, full CAS, ~50ms).</summary>
    SymPy
}

/// <summary>
/// CAS routing rule: which tier handles which operation + complexity.
/// </summary>
public record CasRoutingRule(
    string Pattern,
    CasTier Tier,
    CasOperation[] Operations
);
