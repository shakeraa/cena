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
    CasVerifyResult CasResult
);

public interface IStepVerifierService
{
    /// <summary>
    /// Verify a single student step against the canonical trace.
    /// </summary>
    Task<StepVerificationResult> VerifyStepAsync(
        SolutionStep studentStep,
        SolutionStep? previousStep,
        CanonicalTrace canonical,
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
        CancellationToken ct = default)
    {
        StepsVerified.Add(1);

        // Step 1: Check if the step preserves mathematical equality
        // (i.e., the transformation from previous expression to this one is valid)
        var fromExpr = previousStep?.Expression ?? canonical.ProblemExpression;

        var validityCheck = await _cas.VerifyAsync(new CasVerifyRequest(
            Operation: CasOperation.StepValidity,
            ExpressionA: fromExpr,
            ExpressionB: studentStep.Expression
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
                ExpressionB: canonicalStep.Expression
            ), ct);
            isCanonical = equivalenceCheck.Verified;
        }

        return new StepVerificationResult(
            StepNumber: studentStep.StepNumber,
            IsValid: true,
            IsCanonical: isCanonical,
            DivergenceDescription: isCanonical ? null : "Valid but non-canonical approach",
            SuggestedNextStep: null,
            CasResult: validityCheck
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
            ExpressionB: canonicalAnswer
        ), ct);

        if (result.Verified) return result;

        // Fall back to numerical tolerance for floating-point answers
        return await _cas.VerifyAsync(new CasVerifyRequest(
            Operation: CasOperation.NumericalTolerance,
            ExpressionA: studentAnswer,
            ExpressionB: canonicalAnswer,
            Tolerance: 1e-6
        ), ct);
    }

    private static string? FindCanonicalHint(int stepNumber, CanonicalTrace canonical)
    {
        if (stepNumber > 0 && stepNumber <= canonical.Steps.Count)
        {
            var hint = canonical.Steps[stepNumber - 1];
            return hint.Operation ?? hint.Justification;
        }
        return null;
    }
}
