// =============================================================================
// Cena Platform — AI Isomorph Generator (prr-201, stage 2 adapter)
//
// Production implementation of IIsomorphGenerator. Delegates to
// IAiGenerationService.BatchGenerateAsync — the single existing tool-use
// LLM path (Sonnet, tier3). The wrapper translates the cell + seed variants
// into a BatchGenerateRequest and unpacks the result into the contract shape
// the waterfall orchestrator expects. CAS + similarity gating stay upstream
// in the orchestrator so the LLM adapter focuses on I/O only.
//
// ADR-0026: the underlying AiGenerationService already carries a tier3
// task-routing tag for question_generation; this adapter is not itself
// a call site (it does no direct LLM I/O), so it's covered transitively.
// =============================================================================

using Cena.Actors.QuestionBank.Coverage;
using Cena.Actors.QuestionBank.Templates;
using Cena.Admin.Api.QualityGate;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.QuestionBank.Coverage;

public sealed class AiIsomorphGenerator : IIsomorphGenerator
{
    private readonly IAiGenerationService _ai;
    private readonly IQualityGateService _qualityGate;
    private readonly ILogger<AiIsomorphGenerator> _logger;

    // Cost estimate per generated candidate (tier-3 Sonnet, ~4k tokens out,
    // ~2k tokens in — matches AiGenerationService's own pricing constants).
    // Used only for telemetry reporting; the budget gate uses a separate
    // conservative reserve in the orchestrator.
    private const double EstimatedCostPerCandidateUsd = 0.015;

    public AiIsomorphGenerator(
        IAiGenerationService ai,
        IQualityGateService qualityGate,
        ILogger<AiIsomorphGenerator> logger)
    {
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _qualityGate = qualityGate ?? throw new ArgumentNullException(nameof(qualityGate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IsomorphResult> GenerateAsync(IsomorphRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var cell = request.Cell;

        // Map cell difficulty to the 0..1 float range that BatchGenerateRequest
        // uses. Easy→0.2, Medium→0.5, Hard→0.8 — same convention as the
        // parametric template's difficulty seeding.
        var (minDiff, maxDiff) = cell.Difficulty switch
        {
            TemplateDifficulty.Easy => (0.1f, 0.3f),
            TemplateDifficulty.Medium => (0.4f, 0.6f),
            TemplateDifficulty.Hard => (0.7f, 0.9f),
            _ => (0.4f, 0.6f)
        };

        var bloomsLevel = BloomForDifficulty(cell.Difficulty);

        var batchReq = new BatchGenerateRequest(
            Count: Math.Clamp(request.NeededCount, 1, 20),
            Subject: cell.Subject,
            Topic: cell.Topic,
            Grade: GradeForTrack(cell.Track),
            BloomsLevel: bloomsLevel,
            MinDifficulty: minDiff,
            MaxDifficulty: maxDiff,
            Language: cell.Language);

        BatchGenerateResponse response;
        try
        {
            response = await _ai.BatchGenerateAsync(batchReq, _qualityGate);
        }
        catch (CircuitOpenException ex)
        {
            _logger.LogWarning(ex,
                "[ISOMORPH_GEN] circuit open for cell={Cell}", cell.Address);
            return new IsomorphResult(
                IsomorphVerdict.CircuitOpen,
                Array.Empty<IsomorphCandidate>(),
                0,
                ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[ISOMORPH_GEN] generator error for cell={Cell}", cell.Address);
            return new IsomorphResult(
                IsomorphVerdict.GeneratorError,
                Array.Empty<IsomorphCandidate>(),
                0,
                ex.Message);
        }

        if (!response.Success)
        {
            return new IsomorphResult(
                IsomorphVerdict.GeneratorError,
                Array.Empty<IsomorphCandidate>(),
                0,
                response.Error);
        }

        // Translate batched questions → IsomorphCandidate list. The
        // waterfall orchestrator will re-run CAS + similarity + dedupe
        // against these before admitting them.
        var candidates = response.Results
            .Select(r => new IsomorphCandidate(
                Stem: r.Question.Stem,
                AnswerExpr: r.Question.Options
                    .FirstOrDefault(o => o.IsCorrect)?.Text ?? string.Empty,
                Distractors: r.Question.Options
                    .Where(o => !o.IsCorrect)
                    .Select(o => new IsomorphDistractor(
                        MisconceptionId: o.DistractorRationale ?? "unclassified",
                        Text: o.Text))
                    .ToArray(),
                RawModelOutput: null))
            .Where(c => !string.IsNullOrWhiteSpace(c.Stem))
            .ToArray();

        var cost = EstimatedCostPerCandidateUsd * candidates.Length;

        return new IsomorphResult(
            IsomorphVerdict.Ok,
            candidates,
            cost,
            null);
    }

    private static int BloomForDifficulty(TemplateDifficulty d) => d switch
    {
        TemplateDifficulty.Easy => 2,
        TemplateDifficulty.Medium => 3,
        TemplateDifficulty.Hard => 4,
        _ => 3
    };

    private static string GradeForTrack(TemplateTrack t) => t switch
    {
        TemplateTrack.FourUnit => "4 Units",
        TemplateTrack.FiveUnit => "5 Units",
        _ => "4 Units"
    };
}
