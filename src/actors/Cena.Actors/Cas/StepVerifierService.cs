// =============================================================================
// Cena Platform — Step Verifier Service (CAS-002)
//
// Verifies individual solution steps via the CAS router.
// Integrates with NATS for actor-to-verifier communication.
//
// NATS subject: cena.cas.verify.step
// Called by: TutorMessageService, LearningSessionActor
// =============================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Cena.Infrastructure.Documents;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Cas;

/// <summary>
/// A single step in a student's solution.
/// </summary>
public record SolutionStep(
    int StepNumber,
    string Expression,
    string? Operation,
    string? Justification
);

/// <summary>
/// Canonical solution trace (produced by CAS from the original problem).
/// </summary>
public record CanonicalTrace(
    string ProblemExpression,
    string FinalAnswer,
    IReadOnlyList<SolutionStep> Steps
);

/// <summary>
/// Result of verifying a student's step against the canonical trace.
/// </summary>
public record StepVerificationResult(
    int StepNumber,
    bool IsValid,
    bool IsCanonical,
    string? DivergenceDescription,
    string? SuggestedNextStep,
    CasVerifyResult CasResult,
    /// <summary>PP-017: True when the student is on a valid non-canonical path at Exploratory level.</summary>
    bool IsProductiveFailurePath = false
);

public interface IStepVerifierService
{
    /// <summary>
    /// Verify a single student step against the canonical trace.
    /// PP-017: scaffoldingLevel varies messaging for productive failure (Exploratory).
    /// </summary>
    Task<StepVerificationResult> VerifyStepAsync(
        SolutionStep studentStep,
        SolutionStep? previousStep,
        CanonicalTrace canonical,
        StepScaffoldingLevel scaffoldingLevel = StepScaffoldingLevel.None,
        CancellationToken ct = default);

    /// <summary>
    /// Verify whether a student's final answer matches the canonical answer.
    /// </summary>
    Task<CasVerifyResult> VerifyFinalAnswerAsync(
        string studentAnswer,
        string canonicalAnswer,
        CancellationToken ct = default);
}

/// <summary>
/// Step verifier that uses the CAS router for mathematical validation.
/// </summary>
public sealed class StepVerifierService : IStepVerifierService
{
    private readonly ICasRouterService _cas;
    private readonly ILogger<StepVerifierService> _logger;

    private static readonly Meter Meter = new("Cena.Cas.Steps", "1.0");
    private static readonly Counter<long> StepsVerified = Meter.CreateCounter<long>(
        "cena.cas.steps.verified.total", description: "Total student steps verified");
    private static readonly Counter<long> StepsDiverged = Meter.CreateCounter<long>(
        "cena.cas.steps.diverged.total", description: "Steps that diverged from canonical");

    public StepVerifierService(ICasRouterService cas, ILogger<StepVerifierService> logger)
    {
        _cas = cas;
        _logger = logger;
    }

    public async Task<StepVerificationResult> VerifyStepAsync(
        SolutionStep studentStep,
        SolutionStep? previousStep,
        CanonicalTrace canonical,
        StepScaffoldingLevel scaffoldingLevel = StepScaffoldingLevel.None,
        CancellationToken ct = default)
    {
        StepsVerified.Add(1);

        // Step 1: Check if the step preserves mathematical equality
        // (i.e., the transformation from previous expression to this one is valid)
        var fromExpr = previousStep?.Expression ?? canonical.ProblemExpression;

        var validityCheck = await _cas.VerifyAsync(new CasVerifyRequest(
            Operation: CasOperation.StepValidity,
            ExpressionA: fromExpr,
            ExpressionB: studentStep.Expression,
            Variable: null
        ), ct);

        if (!validityCheck.Verified)
        {
            StepsDiverged.Add(1);
            _logger.LogDebug("Step {N} invalid: {From} → {To}",
                studentStep.StepNumber, fromExpr, studentStep.Expression);

            return new StepVerificationResult(
                StepNumber: studentStep.StepNumber,
                IsValid: false,
                IsCanonical: false,
                DivergenceDescription: validityCheck.ErrorMessage
                    ?? $"Step does not preserve equality: {fromExpr} → {studentStep.Expression}",
                SuggestedNextStep: FindCanonicalHint(studentStep.StepNumber, canonical),
                CasResult: validityCheck
            );
        }

        // Step 2: Check if the step matches the canonical trace
        var isCanonical = false;
        if (studentStep.StepNumber <= canonical.Steps.Count)
        {
            var canonicalStep = canonical.Steps[studentStep.StepNumber - 1];
            var equivalenceCheck = await _cas.VerifyAsync(new CasVerifyRequest(
                Operation: CasOperation.Equivalence,
                ExpressionA: studentStep.Expression,
                ExpressionB: canonicalStep.Expression,
                Variable: null
            ), ct);
            isCanonical = equivalenceCheck.Verified;
        }

        // PP-017: At Exploratory level, celebrate valid non-canonical approaches
        var isExploratoryDivergence = !isCanonical && scaffoldingLevel == StepScaffoldingLevel.Exploratory;
        var divergenceDesc = isCanonical ? null
            : isExploratoryDivergence
                ? "Great approach! This is different from the standard method — keep going to see if it leads to the answer."
                : "Valid but non-canonical approach";

        return new StepVerificationResult(
            StepNumber: studentStep.StepNumber,
            IsValid: true,
            IsCanonical: isCanonical,
            DivergenceDescription: divergenceDesc,
            SuggestedNextStep: isExploratoryDivergence ? null : null,
            CasResult: validityCheck,
            IsProductiveFailurePath: isExploratoryDivergence
        );
    }

    public async Task<CasVerifyResult> VerifyFinalAnswerAsync(
        string studentAnswer,
        string canonicalAnswer,
        CancellationToken ct = default)
    {
        // Try equivalence first, then numerical tolerance
        var result = await _cas.VerifyAsync(new CasVerifyRequest(
            Operation: CasOperation.Equivalence,
            ExpressionA: studentAnswer,
            ExpressionB: canonicalAnswer,
            Variable: null
        ), ct);

        if (result.Verified) return result;

        // Fall back to numerical tolerance for floating-point answers
        return await _cas.VerifyAsync(new CasVerifyRequest(
            Operation: CasOperation.NumericalTolerance,
            ExpressionA: studentAnswer,
            ExpressionB: canonicalAnswer,
            Variable: null,
            Tolerance: 1e-6
        ), ct);
    }

    private static string? FindCanonicalHint(int stepNumber, CanonicalTrace canonical)
    {
        if (stepNumber > 0 && stepNumber <= canonical.Steps.Count)
        {
            // PP-009: Return ONLY the operation name (e.g. "factor", "simplify"),
            // never the justification — it may leak the canonical answer.
            var op = canonical.Steps[stepNumber - 1].Operation;
            if (op is null) return null;

            // Safety net: strip the canonical answer even from operation strings
            var sanitized = op.Replace(canonical.FinalAnswer, "[answer]");
            foreach (var step in canonical.Steps)
                sanitized = sanitized.Replace(step.Expression, "[expression]");

            return sanitized;
        }
        return null;
    }
}
