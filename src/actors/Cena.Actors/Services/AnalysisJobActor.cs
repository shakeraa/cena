// =============================================================================
// Cena Platform -- Analysis Job Actor
// Proto.Actor virtual actor that processes stagnation analysis jobs from a
// Redis queue. One actor per job type. Rate-limited, deduped, cached.
//
// Flow: API submits job → Redis LIST push → Actor pops → computes → stores result
// =============================================================================

using System.Diagnostics;
using System.Text.Json;
using Cena.Actors.Events;
using Cena.Actors.Ingest;
using Marten;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Cena.Actors.Services;

/// <summary>
/// Background service that processes analysis jobs from a Redis queue.
/// Runs as a single consumer — no parallel processing to prevent DB overload.
/// </summary>
public sealed class AnalysisJobProcessor : BackgroundService
{
    private readonly IDocumentStore _store;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<AnalysisJobProcessor> _logger;

    private const string JobQueue = "analysis:jobs:queue";
    private const string ProcessingSet = "analysis:jobs:processing";
    private const int MaxConcurrentJobs = 3;
    private const int PollIntervalMs = 500;
    private static readonly TimeSpan ResultCacheTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan JobTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public AnalysisJobProcessor(
        IDocumentStore store,
        IConnectionMultiplexer redis,
        ILogger<AnalysisJobProcessor> logger)
    {
        _store = store;
        _redis = redis;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("AnalysisJobProcessor started. MaxConcurrent={Max}, PollInterval={Interval}ms",
            MaxConcurrentJobs, PollIntervalMs);

        var semaphore = new SemaphoreSlim(MaxConcurrentJobs);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var db = _redis.GetDatabase();
                var jobId = await db.ListRightPopAsync(JobQueue);

                if (jobId.IsNullOrEmpty)
                {
                    await Task.Delay(PollIntervalMs, ct);
                    continue;
                }

                await semaphore.WaitAsync(ct);

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessJobAsync(jobId.ToString(), ct);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process analysis job {JobId}", jobId);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AnalysisJobProcessor loop error");
                await Task.Delay(2000, ct);
            }
        }
    }

    private async Task ProcessJobAsync(string jobId, CancellationToken ct)
    {
        await using var session = _store.LightweightSession();
        var job = await session.LoadAsync<AnalysisJobDocument>(jobId, ct);
        if (job is null)
        {
            _logger.LogWarning("Job {JobId} not found in store", jobId);
            return;
        }

        if (job.Status != AnalysisJobStatus.Queued)
        {
            _logger.LogDebug("Job {JobId} already in status {Status}, skipping", jobId, job.Status);
            return;
        }

        var sw = Stopwatch.StartNew();
        job.Status = AnalysisJobStatus.Processing;
        job.StartedAt = DateTimeOffset.UtcNow;
        session.Store(job);
        await session.SaveChangesAsync(ct);

        try
        {
            var resultJson = job.JobType switch
            {
                AnalysisJobType.StagnationInsights => await ComputeStagnationInsightsAsync(job, ct),
                AnalysisJobType.StagnationTimeline => await ComputeStagnationTimelineAsync(job, ct),
                _ => throw new NotSupportedException($"Unknown job type: {job.JobType}")
            };

            sw.Stop();
            job.Status = AnalysisJobStatus.Completed;
            job.ResultJson = resultJson;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ProcessingMs = (int)sw.ElapsedMilliseconds;

            // Cache the result in Redis for fast poll responses
            var db = _redis.GetDatabase();
            await db.StringSetAsync($"analysis:result:{jobId}", resultJson, ResultCacheTtl);
            // Also cache by dedup key so identical requests hit cache
            await db.StringSetAsync($"analysis:result:dedup:{job.DedupKey}", resultJson, ResultCacheTtl);

            _logger.LogInformation("Job {JobId} completed in {Ms}ms ({Type} for {Student}/{Concept})",
                jobId, sw.ElapsedMilliseconds, job.JobType, job.StudentId, job.ConceptId);
        }
        catch (Exception ex)
        {
            sw.Stop();
            job.Status = AnalysisJobStatus.Failed;
            job.ErrorMessage = ex.Message;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ProcessingMs = (int)sw.ElapsedMilliseconds;

            _logger.LogError(ex, "Job {JobId} failed after {Ms}ms", jobId, sw.ElapsedMilliseconds);
        }

        await using var updateSession = _store.LightweightSession();
        updateSession.Store(job);
        await updateSession.SaveChangesAsync(ct);
    }

    // ── Stagnation Insights Computation ──
    // (Extracted from the synchronous StagnationInsightsService)

    private async Task<string> ComputeStagnationInsightsAsync(AnalysisJobDocument job, CancellationToken ct)
    {
        var attempts = await LoadAttempts(job.StudentId, job.ConceptId, ct);

        if (attempts.Count == 0)
            return JsonSerializer.Serialize(new { factors = new { primaryFactor = "none", explanation = "No attempts found" } }, JsonOpts);

        var diffScore = AnalyzeDifficultyMismatch(attempts);
        var focusScore = AnalyzeFocusDegradation(attempts);
        var prereqScore = AnalyzePrerequisiteGaps(attempts);
        var methScore = AnalyzeMethodologyIneffectiveness(attempts);
        var errorScore = AnalyzeErrorRepetition(attempts);

        var factors = new (string name, float score)[]
        {
            ("difficulty_mismatch", diffScore), ("focus", focusScore),
            ("prerequisites", prereqScore), ("methodology", methScore),
            ("error_repetition", errorScore)
        };
        var primary = factors.OrderByDescending(f => f.score).First();

        var correct = attempts.Count(a => a.IsCorrect);

        var result = new
        {
            studentId = job.StudentId,
            conceptId = job.ConceptId,
            factors = new
            {
                difficultyMismatchScore = diffScore,
                focusDegradationScore = focusScore,
                prerequisiteGapScore = prereqScore,
                methodologyIneffectivenessScore = methScore,
                errorRepetitionScore = errorScore,
                primaryFactor = primary.name,
                explanation = BuildExplanation(primary.name, attempts)
            },
            recommendations = BuildRecommendations(diffScore, focusScore, prereqScore, methScore, errorScore, attempts),
            summary = new
            {
                totalAttempts = attempts.Count,
                correctCount = correct,
                accuracyRate = attempts.Count > 0 ? (float)correct / attempts.Count : 0f,
                stretchAttempts = attempts.Count(a => a.DifficultyGap > 0.25f),
                regressionAttempts = attempts.Count(a => a.DifficultyGap < -0.25f),
                focusDegradedAttempts = attempts.Count(a => a.FocusState is "Declining" or "Degrading" or "Critical"),
                avgDifficultyGap = attempts.Where(a => a.QuestionDifficulty > 0).Select(a => a.DifficultyGap).DefaultIfEmpty(0).Average(),
                avgResponseTimeMs = (float)attempts.Average(a => a.ResponseTimeMs),
                methodologiesUsed = attempts.Select(a => a.MethodologyActive).Distinct().ToList(),
                errorTypes = attempts.Where(a => !string.IsNullOrEmpty(a.ErrorType) && a.ErrorType != "None")
                    .Select(a => a.ErrorType).Distinct().ToList()
            }
        };

        return JsonSerializer.Serialize(result, JsonOpts);
    }

    private async Task<string> ComputeStagnationTimelineAsync(AnalysisJobDocument job, CancellationToken ct)
    {
        var attempts = await LoadAttempts(job.StudentId, job.ConceptId, ct);

        var timeline = attempts.OrderByDescending(a => a.Timestamp).Take(50).Select(a => new
        {
            timestamp = a.Timestamp, questionId = a.QuestionId, isCorrect = a.IsCorrect,
            priorMastery = (float)a.PriorMastery, posteriorMastery = (float)a.PosteriorMastery,
            questionDifficulty = a.QuestionDifficulty, difficultyGap = a.DifficultyGap,
            difficultyFrame = a.DifficultyFrame, focusState = a.FocusState,
            methodology = a.MethodologyActive, errorType = a.ErrorType,
            responseTimeMs = a.ResponseTimeMs, hintsUsed = a.HintCountUsed
        }).ToList();

        return JsonSerializer.Serialize(new { studentId = job.StudentId, conceptId = job.ConceptId, timeline }, JsonOpts);
    }

    // ── Shared query + analysis (same logic as StagnationInsightsService) ──

    private async Task<List<ConceptAttempted_V1>> LoadAttempts(string studentId, string conceptId, CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var events = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.StreamKey == studentId && e.EventTypeName == "ConceptAttempted_V1")
            .OrderByDescending(e => e.Timestamp)
            .Take(200)
            .ToListAsync(ct);

        return events.Select(e => e.Data).OfType<ConceptAttempted_V1>()
            .Where(a => a.ConceptId == conceptId).ToList();
    }

    private static float AnalyzeDifficultyMismatch(List<ConceptAttempted_V1> attempts)
    {
        var withDiff = attempts.Where(a => a.QuestionDifficulty > 0).ToList();
        if (withDiff.Count == 0) return 0f;
        var mismatch = (float)(withDiff.Count(a => a.DifficultyGap > 0.25f) + withDiff.Count(a => a.DifficultyGap < -0.25f)) / withDiff.Count;
        var wrongHigh = withDiff.Count(a => !a.IsCorrect && a.DifficultyGap > 0.2f);
        var wrongAll = withDiff.Count(a => !a.IsCorrect);
        var corr = wrongAll > 0 ? (float)wrongHigh / wrongAll : 0f;
        return Math.Min(1f, mismatch * 0.6f + corr * 0.4f);
    }

    private static float AnalyzeFocusDegradation(List<ConceptAttempted_V1> attempts)
    {
        var withFocus = attempts.Where(a => a.FocusState is not null).ToList();
        if (withFocus.Count == 0) return 0f;
        var degraded = withFocus.Count(a => a.FocusState is "Declining" or "Degrading" or "Critical");
        var critical = withFocus.Count(a => a.FocusState == "Critical");
        var weighted = (float)(degraded + critical) / withFocus.Count;
        var wrongDeg = withFocus.Count(a => !a.IsCorrect && a.FocusState is "Declining" or "Degrading" or "Critical");
        var wrongAll = withFocus.Count(a => !a.IsCorrect);
        var corr = wrongAll > 0 ? (float)wrongDeg / wrongAll : 0f;
        return Math.Min(1f, weighted * 0.5f + corr * 0.5f);
    }

    private static float AnalyzePrerequisiteGaps(List<ConceptAttempted_V1> attempts)
    {
        if (attempts.Count < 5) return 0f;
        var recent = attempts.Take(10).ToList();
        var avg = recent.Average(a => a.PosteriorMastery);
        var variance = recent.Max(a => a.PosteriorMastery) - recent.Min(a => a.PosteriorMastery);
        if (avg < 0.3 && variance < 0.05) return 0.9f;
        if (avg < 0.5 && variance < 0.1) return 0.6f;
        return Math.Max(0f, 0.3f - (float)avg) * 2f;
    }

    private static float AnalyzeMethodologyIneffectiveness(List<ConceptAttempted_V1> attempts)
    {
        var methods = attempts.Select(a => a.MethodologyActive).Distinct().ToList();
        if (methods.Count <= 1) return 0.2f;
        var byMethod = attempts.GroupBy(a => a.MethodologyActive)
            .ToDictionary(g => g.Key, g => g.Count(a => a.IsCorrect) / (float)g.Count());
        var spread = byMethod.Values.Max() - byMethod.Values.Min();
        if (spread < 0.15f && byMethod.Values.Max() < 0.6f) return 0.8f;
        return Math.Max(0f, 0.5f - spread);
    }

    private static float AnalyzeErrorRepetition(List<ConceptAttempted_V1> attempts)
    {
        var errors = attempts.Where(a => !a.IsCorrect && a.ErrorType is not (null or "None")).Select(a => a.ErrorType).ToList();
        if (errors.Count < 3) return 0f;
        var dominant = errors.GroupBy(e => e).OrderByDescending(g => g.Count()).First();
        var rate = (float)dominant.Count() / errors.Count;
        return rate > 0.6f ? 0.9f : rate;
    }

    private static string BuildExplanation(string factor, List<ConceptAttempted_V1> attempts) => factor switch
    {
        "difficulty_mismatch" => $"{attempts.Count(a => a.DifficultyGap > 0.3f)} of {attempts.Count} attempts were stretch questions.",
        "focus" => $"{attempts.Count(a => a.FocusState is "Declining" or "Degrading" or "Critical")} of {attempts.Count} attempts occurred during poor focus.",
        "prerequisites" => "Prior mastery plateau suggests prerequisite gaps.",
        "methodology" => $"{attempts.Select(a => a.MethodologyActive).Distinct().Count()} approaches tried without improvement.",
        "error_repetition" => $"Dominant error: {attempts.Where(a => !a.IsCorrect && a.ErrorType is not (null or "None")).GroupBy(a => a.ErrorType).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "unknown"}.",
        _ => "Multiple factors may be contributing."
    };

    private static List<object> BuildRecommendations(float diff, float focus, float prereq, float meth, float err, List<ConceptAttempted_V1> attempts)
    {
        var recs = new List<object>();
        if (diff > 0.5f) recs.Add(new { action = "reduce_difficulty", reason = "Questions outside ZPD. Narrow difficulty range.", confidence = diff });
        if (focus > 0.5f) recs.Add(new { action = "suggest_break", reason = "Errors correlate with poor focus. Shorter sessions recommended.", confidence = focus });
        if (prereq > 0.5f) recs.Add(new { action = "review_prerequisites", reason = "Mastery plateau at low level. Review foundational concepts.", confidence = prereq });
        if (meth > 0.5f) recs.Add(new { action = "switch_methodology", reason = "Current approach isn't producing improvement.", confidence = meth });
        if (err > 0.5f) recs.Add(new { action = "investigate_errors", reason = "Same error type repeating. Targeted remediation needed.", confidence = err });
        return recs.OrderByDescending(r => ((dynamic)r).confidence).Cast<object>().ToList();
    }
}
