// =============================================================================
// Cena Platform -- Question Pipeline Service (CNT-002)
// Tracks questions through: Generated -> QualityGated -> InReview -> Approved -> Published
// Provides aggregate stats and bulk approve/reject operations.
// =============================================================================

using Cena.Actors.Questions;
using Cena.Admin.Api.QualityGate;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api;

// ── Pipeline DTOs ──

public sealed record QuestionPipelineStatusResponse(
    int Generated,
    int QualityGated,
    int InReview,
    int Approved,
    int Published,
    int AutoRejected,
    IReadOnlyList<PipelineStageDetail> Stages,
    DateTimeOffset AsOf);

public sealed record PipelineStageDetail(
    string Stage,
    int Count,
    float AvgQualityScore,
    int OldestAgeHours);

public sealed record BulkApproveRequest(
    IReadOnlyList<string> QuestionIds,
    string? ReviewNote);

public sealed record BulkRejectRequest(
    IReadOnlyList<string> QuestionIds,
    string Reason);

public sealed record BulkOperationResult(
    int Succeeded,
    int Failed,
    IReadOnlyList<string> FailedIds,
    string Message);

// ── Service Interface ──

public interface IQuestionPipelineService
{
    Task<QuestionPipelineStatusResponse> GetStatusAsync();
    Task<BulkOperationResult> BulkApproveAsync(BulkApproveRequest request, string userId);
    Task<BulkOperationResult> BulkRejectAsync(BulkRejectRequest request, string userId);
}

// ── Implementation ──

public sealed class QuestionPipelineService : IQuestionPipelineService
{
    private readonly IDocumentStore _store;
    private readonly IQuestionBankService _questionBankService;
    private readonly ILogger<QuestionPipelineService> _logger;

    public QuestionPipelineService(
        IDocumentStore store,
        IQuestionBankService questionBankService,
        ILogger<QuestionPipelineService> logger)
    {
        _store = store;
        _questionBankService = questionBankService;
        _logger = logger;
    }

    public async Task<QuestionPipelineStatusResponse> GetStatusAsync()
    {
        await using var session = _store.QuerySession();

        var allQuestions = await session.Query<QuestionReadModel>().ToListAsync();
        var now = DateTimeOffset.UtcNow;

        // Map QuestionReadModel status strings to pipeline stages
        var byStatus = allQuestions
            .GroupBy(q => NormalizePipelineStage(q.Status))
            .ToDictionary(g => g.Key, g => g.ToList());

        var generated    = Count(byStatus, "Generated");
        var qualityGated = Count(byStatus, "QualityGated");
        var inReview     = Count(byStatus, "InReview");
        var approved     = Count(byStatus, "Approved");
        var published    = Count(byStatus, "Published");
        var autoRejected = Count(byStatus, "AutoRejected");

        var stages = new List<PipelineStageDetail>
        {
            BuildStageDetail("Generated",    byStatus, now),
            BuildStageDetail("QualityGated", byStatus, now),
            BuildStageDetail("InReview",     byStatus, now),
            BuildStageDetail("Approved",     byStatus, now),
            BuildStageDetail("Published",    byStatus, now),
            BuildStageDetail("AutoRejected", byStatus, now),
        };

        return new QuestionPipelineStatusResponse(
            Generated:    generated,
            QualityGated: qualityGated,
            InReview:     inReview,
            Approved:     approved,
            Published:    published,
            AutoRejected: autoRejected,
            Stages:       stages,
            AsOf:         now);
    }

    public async Task<BulkOperationResult> BulkApproveAsync(BulkApproveRequest request, string userId)
    {
        if (request.QuestionIds == null || request.QuestionIds.Count == 0)
            return new BulkOperationResult(0, 0, Array.Empty<string>(), "No question IDs provided.");

        if (request.QuestionIds.Count > 100)
            return new BulkOperationResult(0, request.QuestionIds.Count, request.QuestionIds,
                "Maximum 100 questions per bulk operation.");

        var succeeded = 0;
        var failedIds = new List<string>();

        foreach (var id in request.QuestionIds)
        {
            try
            {
                var ok = await _questionBankService.ApproveAsync(id);
                if (ok)
                    succeeded++;
                else
                    failedIds.Add(id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bulk approve failed for question {QuestionId}", id);
                failedIds.Add(id);
            }
        }

        _logger.LogInformation(
            "Bulk approve by {UserId}: {Succeeded}/{Total} succeeded",
            userId, succeeded, request.QuestionIds.Count);

        return new BulkOperationResult(
            Succeeded: succeeded,
            Failed: failedIds.Count,
            FailedIds: failedIds,
            Message: $"Approved {succeeded} of {request.QuestionIds.Count} questions.");
    }

    public async Task<BulkOperationResult> BulkRejectAsync(BulkRejectRequest request, string userId)
    {
        if (request.QuestionIds == null || request.QuestionIds.Count == 0)
            return new BulkOperationResult(0, 0, Array.Empty<string>(), "No question IDs provided.");

        if (request.QuestionIds.Count > 100)
            return new BulkOperationResult(0, request.QuestionIds.Count, request.QuestionIds,
                "Maximum 100 questions per bulk operation.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            return new BulkOperationResult(0, request.QuestionIds.Count, request.QuestionIds,
                "A rejection reason is required.");

        var succeeded = 0;
        var failedIds = new List<string>();

        foreach (var id in request.QuestionIds)
        {
            try
            {
                var ok = await _questionBankService.DeprecateQuestionAsync(id,
                    new DeprecateBankQuestionRequest(request.Reason, RemoveFromServing: true));
                if (ok)
                    succeeded++;
                else
                    failedIds.Add(id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Bulk reject failed for question {QuestionId}", id);
                failedIds.Add(id);
            }
        }

        _logger.LogInformation(
            "Bulk reject by {UserId}: {Succeeded}/{Total} succeeded. Reason: {Reason}",
            userId, succeeded, request.QuestionIds.Count, request.Reason);

        return new BulkOperationResult(
            Succeeded: succeeded,
            Failed: failedIds.Count,
            FailedIds: failedIds,
            Message: $"Rejected {succeeded} of {request.QuestionIds.Count} questions.");
    }

    // ── Helpers ──

    private static string NormalizePipelineStage(string status) => status?.ToLowerInvariant() switch
    {
        "draft"      => "Generated",
        "inreview"   => "InReview",
        "in_review"  => "InReview",
        "approved"   => "Approved",
        "published"  => "Published",
        "deprecated" => "AutoRejected",
        _            => "QualityGated"  // Unknown/intermediate stages group as QualityGated
    };

    private static int Count(Dictionary<string, List<QuestionReadModel>> byStatus, string stage) =>
        byStatus.TryGetValue(stage, out var list) ? list.Count : 0;

    private static PipelineStageDetail BuildStageDetail(
        string stage,
        Dictionary<string, List<QuestionReadModel>> byStatus,
        DateTimeOffset now)
    {
        if (!byStatus.TryGetValue(stage, out var items) || items.Count == 0)
            return new PipelineStageDetail(stage, 0, 0f, 0);

        var avgQuality = items
            .Select(q => (float)q.QualityScore)
            .DefaultIfEmpty(0f)
            .Average();

        var oldestAgeHours = items
            .Select(q => (int)(now - q.CreatedAt).TotalHours)
            .DefaultIfEmpty(0)
            .Max();

        return new PipelineStageDetail(stage, items.Count, avgQuality, oldestAgeHours);
    }
}
