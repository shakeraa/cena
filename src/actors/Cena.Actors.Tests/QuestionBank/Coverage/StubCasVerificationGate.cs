// =============================================================================
// Cena Platform — Stub CAS gate for waterfall tests (prr-201)
//
// Minimal ICasVerificationGate stand-in. Tests configure a predicate
// ShouldVerify(stem, answer) that decides whether the stub returns Verified
// or Failed. No Marten / SymPy / MathNet dependencies; suitable for
// unit-testing the orchestrator in isolation.
// =============================================================================

using Cena.Actors.Cas;
using Cena.Infrastructure.Documents;

namespace Cena.Actors.Tests.QuestionBank.Coverage;

internal sealed class StubCasVerificationGate : ICasVerificationGate
{
    public Func<string, string, CasGateOutcome> ShouldVerify { get; set; } =
        (_, _) => CasGateOutcome.Verified;

    public int CallCount { get; private set; }

    public Task<CasGateResult> VerifyForCreateAsync(
        string questionId,
        string subject,
        string stem,
        string correctAnswerRaw,
        string? variable,
        CancellationToken ct = default)
    {
        CallCount++;
        var outcome = ShouldVerify(stem, correctAnswerRaw);
        var hash = string.IsNullOrEmpty(correctAnswerRaw)
            ? ""
            : Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(correctAnswerRaw)))
              .ToLowerInvariant();
        var binding = new QuestionCasBinding
        {
            Id = questionId,
            QuestionId = questionId,
            Engine = CasEngine.SymPy,
            CanonicalAnswer = correctAnswerRaw,
            CorrectAnswerRaw = correctAnswerRaw,
            CorrectAnswerHash = hash,
            Status = outcome switch
            {
                CasGateOutcome.Verified => CasBindingStatus.Verified,
                CasGateOutcome.Failed => CasBindingStatus.Failed,
                CasGateOutcome.Unverifiable => CasBindingStatus.Unverifiable,
                _ => CasBindingStatus.Unverifiable
            },
            LatencyMs = 0,
            FailureReason = outcome == CasGateOutcome.Verified ? null : "stub-failure",
            VerifiedAt = DateTimeOffset.UtcNow
        };
        var result = new CasGateResult(
            outcome,
            Engine: outcome == CasGateOutcome.CircuitOpen ? "none" : "sympy",
            CanonicalAnswer: correctAnswerRaw,
            CorrectAnswerHash: hash,
            LatencyMs: 0,
            FailureReason: binding.FailureReason,
            Binding: binding);
        return Task.FromResult(result);
    }
}
