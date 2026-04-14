// =============================================================================
// Cena Platform — MathNet.Symbolics Verifier (CAS-001, Tier 1)
//
// In-process CAS for arithmetic and basic algebra. Fast (<1ms), no external
// dependency. Handles: polynomial equivalence, basic simplification,
// numerical tolerance checks. Does NOT handle: calculus, trig identities,
// equation solving, ODE. Those route to SymPy (Tier 2).
// =============================================================================

using System.Diagnostics;
using MathNet.Symbolics;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Cas;

public interface IMathNetVerifier
{
    CasVerifyResult Verify(CasVerifyRequest request);
    bool CanHandle(CasVerifyRequest request);
}

/// <summary>
/// Tier 1 CAS verifier using MathNet.Symbolics for basic algebraic operations.
/// </summary>
public sealed class MathNetVerifier : IMathNetVerifier
{
    private readonly ILogger<MathNetVerifier> _logger;
    private const string EngineName = "MathNet";

    public MathNetVerifier(ILogger<MathNetVerifier> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(CasVerifyRequest request)
    {
        // MathNet handles: equivalence of polynomial/arithmetic expressions,
        // numerical tolerance, basic normal form.
        // Does NOT handle: calculus, trig identities, equation solving.
        if (request.Operation == CasOperation.Solve) return false;

        var expr = request.ExpressionA;
        if (ContainsCalculus(expr)) return false;
        if (ContainsTrigIdentity(expr)) return false;

        return true;
    }

    public CasVerifyResult Verify(CasVerifyRequest request)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return request.Operation switch
            {
                CasOperation.Equivalence => VerifyEquivalence(request, sw),
                CasOperation.NumericalTolerance => VerifyNumerical(request, sw),
                CasOperation.NormalForm => VerifyNormalForm(request, sw),
                CasOperation.StepValidity => VerifyStepValidity(request, sw),
                _ => CasVerifyResult.Error(request.Operation, EngineName, sw.Elapsed.TotalMilliseconds,
                    $"Operation {request.Operation} not supported by MathNet",
                    CasVerifyStatus.UnsupportedOperation)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MathNet verification failed for {Operation}", request.Operation);
            return CasVerifyResult.Error(request.Operation, EngineName, sw.Elapsed.TotalMilliseconds, ex.Message);
        }
    }

    private CasVerifyResult VerifyEquivalence(CasVerifyRequest request, Stopwatch sw)
    {
        if (string.IsNullOrEmpty(request.ExpressionB))
            return CasVerifyResult.Failure(CasOperation.Equivalence, EngineName,
                sw.Elapsed.TotalMilliseconds, "ExpressionB required for equivalence check");

        try
        {
            var exprA = MathNet.Symbolics.Infix.ParseOrThrow(request.ExpressionA);
            var exprB = MathNet.Symbolics.Infix.ParseOrThrow(request.ExpressionB);

            var simplifiedA = MathNet.Symbolics.Algebraic.Expand(exprA);
            var simplifiedB = MathNet.Symbolics.Algebraic.Expand(exprB);

            var diff = MathNet.Symbolics.Algebraic.Expand(simplifiedA - simplifiedB);
            var isZero = diff.Equals(MathNet.Symbolics.Expression.Zero);

            var strA = MathNet.Symbolics.Infix.Format(simplifiedA);
            var strB = MathNet.Symbolics.Infix.Format(simplifiedB);

            return isZero
                ? CasVerifyResult.Success(CasOperation.Equivalence, EngineName,
                    sw.Elapsed.TotalMilliseconds, strA, strB)
                : CasVerifyResult.Failure(CasOperation.Equivalence, EngineName,
                    sw.Elapsed.TotalMilliseconds, $"Expressions differ: {strA} ≠ {strB}");
        }
        catch (Exception ex)
        {
            return CasVerifyResult.Error(CasOperation.Equivalence, EngineName,
                sw.Elapsed.TotalMilliseconds, $"Parse error: {ex.Message}");
        }
    }

    private CasVerifyResult VerifyNumerical(CasVerifyRequest request, Stopwatch sw)
    {
        if (string.IsNullOrEmpty(request.ExpressionB))
            return CasVerifyResult.Failure(CasOperation.NumericalTolerance, EngineName,
                sw.Elapsed.TotalMilliseconds, "ExpressionB required");

        if (double.TryParse(request.ExpressionA, out var a) && double.TryParse(request.ExpressionB, out var b))
        {
            var diff = Math.Abs(a - b);
            return diff <= request.Tolerance
                ? CasVerifyResult.Success(CasOperation.NumericalTolerance, EngineName,
                    sw.Elapsed.TotalMilliseconds, a.ToString("G"), b.ToString("G"))
                : CasVerifyResult.Failure(CasOperation.NumericalTolerance, EngineName,
                    sw.Elapsed.TotalMilliseconds, $"|{a} - {b}| = {diff} > ε={request.Tolerance}");
        }

        return CasVerifyResult.Error(CasOperation.NumericalTolerance, EngineName,
            sw.Elapsed.TotalMilliseconds, "Could not parse expressions as numbers");
    }

    private CasVerifyResult VerifyNormalForm(CasVerifyRequest request, Stopwatch sw)
    {
        try
        {
            var expr = MathNet.Symbolics.Infix.ParseOrThrow(request.ExpressionA);
            var simplified = MathNet.Symbolics.Algebraic.Expand(expr);
            var result = MathNet.Symbolics.Infix.Format(simplified);

            return CasVerifyResult.Success(CasOperation.NormalForm, EngineName,
                sw.Elapsed.TotalMilliseconds, result);
        }
        catch (Exception ex)
        {
            return CasVerifyResult.Error(CasOperation.NormalForm, EngineName,
                sw.Elapsed.TotalMilliseconds, ex.Message);
        }
    }

    private CasVerifyResult VerifyStepValidity(CasVerifyRequest request, Stopwatch sw)
    {
        // Step validity = equivalence (the step preserves equation equality)
        return VerifyEquivalence(request, sw) with { Operation = CasOperation.StepValidity };
    }

    private static bool ContainsCalculus(string expr)
    {
        return expr.Contains("diff(", StringComparison.OrdinalIgnoreCase)
            || expr.Contains("integrate(", StringComparison.OrdinalIgnoreCase)
            || expr.Contains("limit(", StringComparison.OrdinalIgnoreCase)
            || expr.Contains("d/dx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsTrigIdentity(string expr)
    {
        // MathNet can parse sin/cos but can't simplify identities like sin²+cos²=1
        return (expr.Contains("sin", StringComparison.OrdinalIgnoreCase)
            || expr.Contains("cos", StringComparison.OrdinalIgnoreCase)
            || expr.Contains("tan", StringComparison.OrdinalIgnoreCase))
            && expr.Contains("^", StringComparison.Ordinal);
    }
}
