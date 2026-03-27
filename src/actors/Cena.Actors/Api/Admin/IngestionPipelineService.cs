// =============================================================================
// Cena Platform -- Ingestion Pipeline Service
// ADM-009: Pipeline dashboard and management
// =============================================================================

using Marten;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Actors.Api.Admin;

public interface IIngestionPipelineService
{
    Task<PipelineStatusResponse> GetPipelineStatusAsync();
    Task<PipelineItemDetailResponse?> GetItemDetailAsync(string id);
    Task<bool> RetryItemAsync(string id);
    Task<bool> RejectPipelineItemAsync(string id, string reason);
    Task<UploadFileResponse> UploadFileAsync(string filename, string contentType);
    Task<PipelineStatsResponse> GetStatsAsync();
}

public sealed class IngestionPipelineService : IIngestionPipelineService
{
    private readonly IDocumentStore _store;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<IngestionPipelineService> _logger;
    private static readonly List<PipelineStage> _mockStages = GenerateMockStages();

    public IngestionPipelineService(
        IDocumentStore store,
        IConnectionMultiplexer redis,
        ILogger<IngestionPipelineService> logger)
    {
        _store = store;
        _redis = redis;
        _logger = logger;
    }

    public async Task<PipelineStatusResponse> GetPipelineStatusAsync()
    {
        return new PipelineStatusResponse(
            DateTimeOffset.UtcNow,
            _mockStages);
    }

    public async Task<PipelineItemDetailResponse?> GetItemDetailAsync(string id)
    {
        var random = new Random(id.GetHashCode());
        var stageNames = new[] { "Incoming", "OCR Processing", "Segmented", "Normalized", "Classified", "Deduplicated", "Re-Created", "In Review", "Published" };
        var currentStageIndex = random.Next(0, stageNames.Length);

        var stageHistory = new List<StageProcessingInfo>();
        for (int i = 0; i <= currentStageIndex; i++)
        {
            var startedAt = DateTimeOffset.UtcNow.AddHours(-(currentStageIndex - i) * 2 - random.Next(0, 60));
            DateTimeOffset? completedAt = i < currentStageIndex ? startedAt.AddMinutes(random.Next(5, 30)) : null;

            stageHistory.Add(new StageProcessingInfo(
                stageNames[i],
                startedAt,
                completedAt,
                completedAt.HasValue ? completedAt.Value - startedAt : null,
                i < currentStageIndex ? "completed" : "processing",
                null));
        }

        return new PipelineItemDetailResponse(
            Id: id,
            SourceFilename: $"batch-{random.Next(1000)}.pdf",
            SourceType: random.NextSingle() > 0.5 ? "batch" : "photo",
            SourceUrl: $"https://s3.cena.edu/uploads/{id}",
            SubmittedAt: DateTimeOffset.UtcNow.AddHours(-12),
            CompletedAt: currentStageIndex == stageNames.Length - 1 ? DateTimeOffset.UtcNow.AddHours(-1) : null,
            CurrentStage: stageNames[currentStageIndex],
            StageHistory: stageHistory,
            OcrResult: random.NextSingle() > 0.3 ? new OcrOutput(
                $"https://s3.cena.edu/scans/{id}.jpg",
                "Extracted text sample with mathematical content...",
                0.85f + random.NextSingle() * 0.1f,
                new List<OcrRegion>
                {
                    new(10, 10, 200, 50, "Question 1:", 0.95f),
                    new(10, 70, 400, 100, "Solve for x: 2x + 5 = 13", 0.88f)
                }) : null,
            Quality: new QualityScores(
                MathCorrectness: random.Next(70, 95),
                LanguageQuality: random.Next(75, 95),
                PedagogicalQuality: random.Next(65, 90),
                PlagiarismScore: random.Next(0, 20)),
            ExtractedQuestions: Enumerable.Range(0, random.Next(3, 10))
                .Select(i => new ExtractedQuestion(i, $"Question {i + 1} text...", "Answer", 0.7f + random.NextSingle() * 0.25f))
                .ToList());
    }

    public async Task<bool> RetryItemAsync(string id)
    {
        _logger.LogInformation("Retrying pipeline item {ItemId}", id);
        return true;
    }

    public async Task<bool> RejectPipelineItemAsync(string id, string reason)
    {
        _logger.LogInformation("Rejecting pipeline item {ItemId}: {Reason}", id, reason);
        return true;
    }

    public async Task<UploadFileResponse> UploadFileAsync(string filename, string contentType)
    {
        var uploadId = $"upload-{Guid.NewGuid():N}";
        return new UploadFileResponse(uploadId, "processing", $"item-{Guid.NewGuid():N}");
    }

    public async Task<PipelineStatsResponse> GetStatsAsync()
    {
        var random = new Random();
        var throughput = new List<ThroughputPoint>();
        for (int i = 23; i >= 0; i--)
        {
            throughput.Add(new ThroughputPoint(
                DateTimeOffset.UtcNow.AddHours(-i).ToString("yyyy-MM-dd HH:00"),
                random.Next(10, 100)));
        }

        var failureRates = new List<StageFailureRate>
        {
            new("Incoming", 0.02f, 1000, 20),
            new("OCR Processing", 0.05f, 980, 49),
            new("Segmented", 0.03f, 931, 28),
            new("Normalized", 0.01f, 903, 9),
            new("Classified", 0.04f, 894, 36),
            new("Deduplicated", 0.02f, 858, 17),
            new("Re-Created", 0.03f, 841, 25)
        };

        var processingTimes = new List<AvgProcessingTime>
        {
            new("Incoming", 2.5f, 5.0f),
            new("OCR Processing", 45.0f, 120.0f),
            new("Segmented", 8.0f, 20.0f),
            new("Normalized", 12.0f, 30.0f),
            new("Classified", 15.0f, 40.0f),
            new("Deduplicated", 10.0f, 25.0f),
            new("Re-Created", 30.0f, 75.0f)
        };

        var queueTrend = new QueueDepthTrend(
            Enumerable.Range(0, 24)
                .Select(i => new DepthPoint(
                    DateTimeOffset.UtcNow.AddHours(-23 + i).ToString("yyyy-MM-dd HH:00"),
                    random.Next(10, 50),
                    random.Next(5, 30),
                    random.Next(20, 100)))
                .ToList());

        return new PipelineStatsResponse(throughput, failureRates, processingTimes, queueTrend);
    }

    private static List<PipelineStage> GenerateMockStages()
    {
        var random = new Random(42);
        var stages = new List<PipelineStage>
        {
            new("incoming", "Incoming", random.Next(5, 20), "healthy", new List<PipelineItem>()),
            new("ocr", "OCR Processing", random.Next(3, 15), "healthy", new List<PipelineItem>()),
            new("segmented", "Segmented", random.Next(2, 10), "healthy", new List<PipelineItem>()),
            new("normalized", "Normalized", random.Next(2, 8), "healthy", new List<PipelineItem>()),
            new("classified", "Classified", random.Next(1, 6), "healthy", new List<PipelineItem>()),
            new("deduplicated", "Deduplicated", random.Next(1, 5), "healthy", new List<PipelineItem>()),
            new("recreated", "Re-Created", random.Next(1, 5), "slow", new List<PipelineItem>()),
            new("review", "In Review", random.Next(10, 30), "healthy", new List<PipelineItem>()),
            new("published", "Published", random.Next(100, 500), "healthy", new List<PipelineItem>())
        };

        // Populate items for each stage
        for (int i = 0; i < stages.Count; i++)
        {
            var items = new List<PipelineItem>();
            for (int j = 0; j < Math.Min(stages[i].Count, 10); j++)
            {
                items.Add(new PipelineItem(
                    Id: $"item-{i}-{j}",
                    SourceFilename: $"batch-{random.Next(1000)}.pdf",
                    SourceType: random.NextSingle() > 0.5 ? "batch" : "photo",
                    QuestionCount: random.Next(1, 20),
                    QualityScore: random.Next(60, 95),
                    Timestamp: DateTimeOffset.UtcNow.AddHours(-random.Next(1, 48)),
                    ErrorMessage: i == 6 && j == 0 ? "OCR confidence too low" : null,
                    HasError: i == 6 && j == 0));
            }
            stages[i] = stages[i] with { Items = items };
        }

        return stages;
    }
}
