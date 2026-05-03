// =============================================================================
// Cena Platform — Isomorph Generator Contract (prr-201, stage 2)
//
// Minimal seam so the Cena.Actors orchestrator does not reverse-reference
// Cena.Admin.Api (home of AiGenerationService). The Admin.Api layer
// provides the concrete implementation (AiIsomorphGenerator) which delegates
// to IAiGenerationService.BatchGenerateAsync; tests provide an in-memory
// fake.
//
// The contract is intentionally tiny: given the cell and a set of stage-1
// seed variants (used as few-shot exemplars), produce up to `need` new
// candidates. The CAS gate and ministry-similarity checker are applied by
// the orchestrator, NOT inside this contract, so the implementation can
// stay focused on LLM I/O.
//
// ADR-0026 note: the concrete implementation is tagged tier3 with task
// name question_generation via its delegation to AiGenerationService.
// Test doubles are allowlisted.
// =============================================================================

using Cena.Actors.QuestionBank.Templates;

namespace Cena.Actors.QuestionBank.Coverage;

/// <summary>
/// A raw stage-2 candidate — not yet CAS-verified, not yet dedup-checked.
/// The orchestrator runs it through the gate before accepting.
/// </summary>
public sealed record IsomorphCandidate(
    string Stem,
    string AnswerExpr,
    IReadOnlyList<IsomorphDistractor> Distractors,
    string? RawModelOutput);

public sealed record IsomorphDistractor(
    string MisconceptionId,
    string Text);

/// <summary>
/// Estimated cost context for the budget gate. The orchestrator fills this
/// in before the call and uses it to decide whether to bill the institute.
/// </summary>
public sealed record IsomorphRequest(
    CoverageCell Cell,
    IReadOnlyList<ParametricVariant> SeedVariants,
    int NeededCount,
    string InstituteId);

public enum IsomorphVerdict
{
    Ok,
    CircuitOpen,
    GeneratorError
}

public sealed record IsomorphResult(
    IsomorphVerdict Verdict,
    IReadOnlyList<IsomorphCandidate> Candidates,
    double EstimatedCostUsd,
    string? ErrorDetail);

/// <summary>
/// Cena.Actors-side contract for stage-2 LLM isomorph generation.
/// Production implementation lives in
/// <c>Cena.Admin.Api.QuestionBank.Coverage.AiIsomorphGenerator</c>.
/// </summary>
public interface IIsomorphGenerator
{
    Task<IsomorphResult> GenerateAsync(IsomorphRequest request, CancellationToken ct = default);
}
