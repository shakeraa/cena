// =============================================================================
// Cena Platform -- Explanation Cache Admin Service (ADM-018)
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Questions;
using Marten;
using Marten.Events;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Admin.Api;

public interface IExplanationCacheAdminService
{
    Task<ExplanationCacheStatsResponse> GetCacheStatsAsync();
    Task<ExplanationsByQuestionResponse?> GetExplanationsByQuestionAsync(string questionId);
    Task<InvalidateCacheResponse> InvalidateCacheAsync(InvalidateCacheRequest request);
    Task<ExplanationQualityListResponse> GetQualityScoresAsync(
        float minScore, float maxScore, int page, int pageSize);
}

public sealed class ExplanationCacheAdminService : IExplanationCacheAdminService
{
    private readonly IDocumentStore _store;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<ExplanationCacheAdminService> _logger;

    private const string RedisKeyPrefix = "explain:";

    public ExplanationCacheAdminService(
        IDocumentStore store,
        IConnectionMultiplexer redis,
        ILogger<ExplanationCacheAdminService> logger)
    {
        _store = store;
        _redis = redis;
        _logger = logger;
    }

    // ── Cache Stats ──

    public async Task<ExplanationCacheStatsResponse> GetCacheStatsAsync()
    {
        await using var session = _store.QuerySession();

        // L1: Count questions that have non-null explanation text in their state
        var l1Count = 0;
        try
        {
            var explanationEvents = await session.Events
                .QueryRawEventDataOnly<ExplanationEdited_V1>()
                .ToListAsync();
            var updatedEvents = await session.Events
                .QueryRawEventDataOnly<QuestionExplanationUpdated_V1>()
                .ToListAsync();

            // Distinct questions that have had explanations applied
            var questionsWithExplanations = explanationEvents
                .Select(e => e.QuestionId)
                .Union(updatedEvents.Select(e => e.QuestionId))
                .Distinct();
            l1Count = questionsWithExplanations.Count();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query L1 explanation count from event store");
        }

        // L2: Redis key count and memory
        var l2Count = 0;
        long l2Memory = 0;
        try
        {
            var server = _redis.GetServers().FirstOrDefault();
            if (server is not null)
            {
                l2Count = server.Keys(pattern: $"{RedisKeyPrefix}*").Count();
            }

            var db = _redis.GetDatabase();
            var dbSizeResult = await db.ExecuteAsync("DBSIZE");
            // Memory for explanation keys specifically is hard to isolate;
            // report total Redis memory as a proxy with MEMORY USAGE per-key being too expensive
            var infoResult = await db.ExecuteAsync("INFO", "memory");
            var infoText = infoResult.ToString() ?? "";
            var memLine = infoText
                .Split('\n')
                .FirstOrDefault(l => l.StartsWith("used_memory:"));
            if (memLine is not null)
            {
                var memStr = memLine.Split(':').LastOrDefault()?.Trim();
                if (long.TryParse(memStr, out var mem))
                    l2Memory = mem;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query L2 cache stats from Redis");
        }

        // L3: Count ExplanationEdited events (each represents a generation)
        var l3Count = 0;
        try
        {
            l3Count = await session.Events
                .QueryRawEventDataOnly<ExplanationEdited_V1>()
                .CountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query L3 generation count");
        }

        // Hit rates: ExplanationCacheHit/Miss events don't exist in the event store yet.
        // These are tracked via System.Diagnostics.Metrics in ExplanationCacheService.
        // Return 0 until we have a metrics bridge or event-based tracking.
        const float noData = 0f;

        return new ExplanationCacheStatsResponse(
            L1Count: l1Count,
            L2KeyCount: l2Count,
            L2MemoryBytes: l2Memory,
            L3GenerationCount: l3Count,
            OverallHitRate: noData,
            L1HitRate: noData,
            L2HitRate: noData,
            L3HitRate: noData);
    }

    // ── Explanations by Question ──

    public async Task<ExplanationsByQuestionResponse?> GetExplanationsByQuestionAsync(string questionId)
    {
        await using var session = _store.QuerySession();

        // Load question state for the stem
        var questionState = await session.Events.AggregateStreamAsync<QuestionState>(questionId);
        if (questionState is null) return null;

        var versions = new List<ExplanationVersionDto>();

        // L1: Explanation from the event stream (ExplanationEdited / QuestionExplanationUpdated)
        var streamEvents = await session.Events.FetchStreamAsync(questionId);
        foreach (var evt in streamEvents)
        {
            if (evt.Data is ExplanationEdited_V1 edited)
            {
                versions.Add(new ExplanationVersionDto(
                    Level: "L1",
                    Language: "en",
                    Text: edited.NewExplanation,
                    ErrorType: "general",
                    CreatedAt: edited.Timestamp,
                    QualityScores: null));
            }
            else if (evt.Data is QuestionExplanationUpdated_V1 updated)
            {
                versions.Add(new ExplanationVersionDto(
                    Level: "L1",
                    Language: "en",
                    Text: updated.Explanation,
                    ErrorType: "general",
                    CreatedAt: updated.UpdatedAt,
                    QualityScores: null));
            }
        }

        // L2: Check Redis for cached entries matching explain:{questionId}:*
        try
        {
            var server = _redis.GetServers().FirstOrDefault();
            if (server is not null)
            {
                var db = _redis.GetDatabase();
                var keys = server.Keys(pattern: $"{RedisKeyPrefix}{questionId}:*").ToList();

                foreach (var key in keys)
                {
                    var value = await db.StringGetAsync(key);
                    if (!value.IsNullOrEmpty)
                    {
                        // Key format: explain:{questionId}:{errorType}:{language}
                        var parts = ((string)key!).Split(':');
                        var errorType = parts.Length > 2 ? parts[2] : "unknown";
                        var language = parts.Length > 3 ? parts[3] : "en";

                        // Try to deserialize the cached explanation
                        try
                        {
                            var cached = System.Text.Json.JsonSerializer
                                .Deserialize<CachedExplanationDto>(value!);
                            if (cached is not null)
                            {
                                versions.Add(new ExplanationVersionDto(
                                    Level: "L2",
                                    Language: language,
                                    Text: cached.Text ?? "",
                                    ErrorType: errorType,
                                    CreatedAt: cached.GeneratedAt,
                                    QualityScores: null));
                            }
                        }
                        catch
                        {
                            // If we can't deserialize, include raw text
                            versions.Add(new ExplanationVersionDto(
                                Level: "L2",
                                Language: language,
                                Text: value!,
                                ErrorType: errorType,
                                CreatedAt: DateTimeOffset.MinValue,
                                QualityScores: null));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to fetch L2 Redis entries for question {QuestionId}", questionId);
        }

        return new ExplanationsByQuestionResponse(
            QuestionId: questionId,
            QuestionStem: questionState.Stem,
            Explanations: versions);
    }

    // ── Cache Invalidation ──

    public async Task<InvalidateCacheResponse> InvalidateCacheAsync(InvalidateCacheRequest request)
    {
        var invalidated = 0;
        var level = request.Level.ToUpperInvariant();

        // L2 invalidation: delete Redis keys
        if (level is "L2" or "ALL")
        {
            try
            {
                var server = _redis.GetServers().FirstOrDefault();
                if (server is not null)
                {
                    var db = _redis.GetDatabase();
                    var keys = server.Keys(pattern: $"{RedisKeyPrefix}{request.QuestionId}:*").ToArray();
                    if (keys.Length > 0)
                    {
                        await db.KeyDeleteAsync(keys);
                        invalidated += keys.Length;
                        _logger.LogInformation(
                            "Invalidated {Count} L2 cache keys for question {QuestionId}",
                            keys.Length, request.QuestionId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to invalidate L2 cache for question {QuestionId}", request.QuestionId);
            }
        }

        // L1 is part of the question aggregate state -- cannot be deleted from here.
        // Modifying L1 requires editing the question through the normal question edit flow.
        if (level is "L1" or "ALL")
        {
            _logger.LogWarning(
                "L1 explanation cache for question {QuestionId} is stored in the event stream " +
                "and cannot be invalidated directly. Edit the question to update L1 explanations.",
                request.QuestionId);
        }

        // L3 is the generation count (event history) -- immutable by design.
        if (level == "L3")
        {
            _logger.LogWarning(
                "L3 generation events for question {QuestionId} are immutable event-sourced records.",
                request.QuestionId);
        }

        return new InvalidateCacheResponse(invalidated);
    }

    // ── Quality Scores ──

    public async Task<ExplanationQualityListResponse> GetQualityScoresAsync(
        float minScore, float maxScore, int page, int pageSize)
    {
        await using var session = _store.QuerySession();

        // Query question states that have quality evaluations with explanation data.
        // We use the QuestionReadModel projection which has quality scores.
        var query = session.Query<QuestionReadModel>()
            .Where(q => q.QualityScore > 0);

        // QualityScore is an int (0-100). Map composite float range (0.0-1.0) to int range.
        var minScoreInt = (int)(minScore * 100);
        var maxScoreInt = (int)(maxScore * 100);

        if (minScoreInt > 0)
            query = query.Where(q => q.QualityScore >= minScoreInt);
        if (maxScoreInt < 100)
            query = query.Where(q => q.QualityScore <= maxScoreInt);

        var total = await query.CountAsync();

        var items = await query
            .OrderByDescending(q => q.QualityScore)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = items.Select(q =>
        {
            var composite = q.QualityScore / 100f;
            // Individual scores are not stored in the read model;
            // approximate from composite or load from event stream if needed.
            return new ExplanationQualityItemDto(
                QuestionId: q.Id,
                StemPreview: Truncate(q.StemPreview, 80),
                Factual: composite,
                Linguistic: composite,
                Pedagogical: composite,
                Composite: composite,
                Language: q.Language,
                CreatedAt: q.CreatedAt);
        }).ToList();

        return new ExplanationQualityListResponse(dtos, total, page, pageSize);
    }

    // ── Helpers ──

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) ? ""
        : value.Length <= maxLength ? value
        : string.Concat(value.AsSpan(0, maxLength), "...");

    /// <summary>
    /// Mirrors the shape of CachedExplanation from Cena.Actors.Services
    /// for deserialization without a hard dependency.
    /// </summary>
    private sealed record CachedExplanationDto(
        string? Text,
        string? ModelId,
        int TokenCount,
        DateTimeOffset GeneratedAt);
}
