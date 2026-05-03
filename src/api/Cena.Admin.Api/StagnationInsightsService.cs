// =============================================================================
// Cena Platform -- Stagnation Insights Service (Job-Based)
// Submit → Queue → Actor processes → Poll result.
// Rate-limited per user, dedup identical requests, results cached in Redis.
// =============================================================================

using System.Text.Json;
using Cena.Actors.Ingest;
using Marten;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Admin.Api;

public interface IStagnationInsightsService
{
    /// <summary>Submit an analysis job. Returns existing job if identical request is cached/pending.</summary>
    Task<SubmitAnalysisResponse> SubmitAsync(AnalysisJobType type, string studentId, string conceptId, string requestedBy);

    /// <summary>Poll job status + result.</summary>
    Task<PollAnalysisResponse> PollAsync(string jobId);
}

// ── DTOs ──

public sealed record SubmitAnalysisResponse(
    string JobId,
    string Status,          // queued, processing, completed, rate_limited, cached
    string? ResultJson);    // Populated immediately if cached

public sealed record PollAnalysisResponse(
    string JobId,
    string Status,          // queued, processing, completed, failed
    int? ProcessingMs,
    string? ResultJson,
    string? ErrorMessage);

// ── Implementation ──

public sealed class StagnationInsightsService : IStagnationInsightsService
{
    private readonly IDocumentStore _store;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<StagnationInsightsService> _logger;

    // Rate limits
    private const int MaxJobsPerUserPerMinute = 10;
    private const int MaxJobsPerConceptPerMinute = 5;
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan DedupTtl = TimeSpan.FromMinutes(30);

    private const string JobQueue = "analysis:jobs:queue";
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public StagnationInsightsService(
        IDocumentStore store,
        IConnectionMultiplexer redis,
        ILogger<StagnationInsightsService> logger)
    {
        _store = store;
        _redis = redis;
        _logger = logger;
    }

    public async Task<SubmitAnalysisResponse> SubmitAsync(
        AnalysisJobType type, string studentId, string conceptId, string requestedBy)
    {
        var db = _redis.GetDatabase();
        var dedupKey = AnalysisJobDocument.BuildDedupKey(type, studentId, conceptId);

        // ── Check 1: Cached result (dedup — identical request within TTL) ──
        var cachedResult = await db.StringGetAsync($"analysis:result:dedup:{dedupKey}");
        if (cachedResult.HasValue)
        {
            _logger.LogDebug("Cache hit for {DedupKey}", dedupKey);
            return new SubmitAnalysisResponse("cached", "cached", cachedResult.ToString());
        }

        // ── Check 2: Pending/processing job for same request ──
        var pendingJobId = await db.StringGetAsync($"analysis:pending:{dedupKey}");
        if (pendingJobId.HasValue)
        {
            _logger.LogDebug("Dedup hit — reusing pending job {JobId} for {DedupKey}", pendingJobId, dedupKey);
            return new SubmitAnalysisResponse(pendingJobId.ToString(), "processing", null);
        }

        // ── Check 3: Rate limit per user ──
        var userKey = $"analysis:ratelimit:user:{requestedBy}";
        var userCount = await db.StringIncrementAsync(userKey);
        if (userCount == 1)
            await db.KeyExpireAsync(userKey, RateLimitWindow);
        if (userCount > MaxJobsPerUserPerMinute)
        {
            _logger.LogWarning("Rate limit exceeded for user {User}: {Count}/{Max}",
                requestedBy, userCount, MaxJobsPerUserPerMinute);
            return new SubmitAnalysisResponse("", "rate_limited", null);
        }

        // ── Check 4: Rate limit per concept (prevent hammering same concept) ──
        var conceptKey = $"analysis:ratelimit:concept:{studentId}:{conceptId}";
        var conceptCount = await db.StringIncrementAsync(conceptKey);
        if (conceptCount == 1)
            await db.KeyExpireAsync(conceptKey, RateLimitWindow);
        if (conceptCount > MaxJobsPerConceptPerMinute)
        {
            _logger.LogWarning("Rate limit exceeded for concept {Student}/{Concept}: {Count}/{Max}",
                studentId, conceptId, conceptCount, MaxJobsPerConceptPerMinute);
            return new SubmitAnalysisResponse("", "rate_limited", null);
        }

        // ── Submit new job ──
        var jobId = $"aj-{Guid.NewGuid():N}";
        var job = new AnalysisJobDocument
        {
            Id = jobId,
            JobType = type,
            Status = AnalysisJobStatus.Queued,
            StudentId = studentId,
            ConceptId = conceptId,
            RequestedBy = requestedBy,
            DedupKey = dedupKey,
            SubmittedAt = DateTimeOffset.UtcNow
        };

        await using var session = _store.LightweightSession();
        session.Store(job);
        await session.SaveChangesAsync();

        // Mark as pending for dedup
        await db.StringSetAsync($"analysis:pending:{dedupKey}", jobId, DedupTtl);

        // Push to queue
        await db.ListLeftPushAsync(JobQueue, jobId);

        _logger.LogInformation("Analysis job {JobId} submitted: {Type} for {Student}/{Concept} by {User}",
            jobId, type, studentId, conceptId, requestedBy);

        return new SubmitAnalysisResponse(jobId, "queued", null);
    }

    public async Task<PollAnalysisResponse> PollAsync(string jobId)
    {
        // Fast path: check Redis cache first
        var db = _redis.GetDatabase();
        var cached = await db.StringGetAsync($"analysis:result:{jobId}");
        if (cached.HasValue)
        {
            return new PollAnalysisResponse(jobId, "completed", null, cached.ToString(), null);
        }

        // Slow path: query Marten
        await using var session = _store.QuerySession();
        var job = await session.LoadAsync<AnalysisJobDocument>(jobId);

        if (job is null)
            return new PollAnalysisResponse(jobId, "not_found", null, null, "Job not found");

        return new PollAnalysisResponse(
            jobId,
            job.Status.ToString().ToLowerInvariant(),
            job.ProcessingMs > 0 ? job.ProcessingMs : null,
            job.ResultJson,
            job.ErrorMessage);
    }
}
