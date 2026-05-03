// =============================================================================
// Cena Platform — Reference-Calibrated Recreation Service (RDY-019b, Phase 3.2)
//
// Closes the ministry-reference → AI-authored CAS-gated recreation loop. Reads
// the analysis.json produced by scripts/bagrut-reference-analyzer.py (aggregate
// topic × format hits per track — NO raw question text), plans per-cluster
// recreation bundles weighted by the reference distribution, and runs them
// through the existing IAiGenerationService.BatchGenerateAsync — which already
// routes every candidate through IQualityGateService + the CAS gate + the
// single gated writer (CasGatedQuestionPersister).
//
// NO NEW WRITE PATHS. This service is a coordinator on top of the existing
// gated generation pipeline; it does not talk to Marten for questions at all.
//
// Legal posture (memory:bagrut_reference_only, decided 2026-04-15):
//   Ministry exams are REFERENCE MATERIAL ONLY. Raw question text never
//   enters the student-facing corpus. Every student-facing item is an
//   AI-authored CAS-gated recreation with Provenance=recreation tags on
//   the source paper's aggregate cluster — never on any specific question.
//
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;
using Cena.Actors.Events;
using Cena.Admin.Api.QualityGate;
using Marten;
using Microsoft.Extensions.Logging;
using QualityGateServices = Cena.Admin.Api.QualityGate;

namespace Cena.Admin.Api.Content;

// ─── Request / Plan / Outcome / Response records ─────────────────────────────

/// <summary>
/// Caller-supplied request. Defaults to a dry-run plan against the canonical
/// <c>corpus/bagrut/reference/analysis.json</c> path — operators must
/// explicitly opt in to spend via <see cref="DryRun"/> = false.
/// </summary>
public sealed record ReferenceRecreationRequest(
    string? AnalysisJsonPath = null,
    IReadOnlyList<string>? TargetTracks = null,     // null = all tracks in analysis.json
    IReadOnlyList<string>? TargetTopics = null,     // null = all topics
    int MaxCandidatesPerCluster = 3,                // clamped to [1, 20]
    int MaxTotalCandidates = 25,                    // clamped to [1, 200]
    string? Language = null,                        // null → derived from track (default "en")
    bool DryRun = true);

public sealed record ReferenceClusterPlan(
    string Track,
    string Topic,
    string Format,
    int Hits,                      // how often this topic+format was observed in the reference corpus
    int WouldGenerate,             // count we would ask the LLM for (0 if skipped)
    float MinDifficulty,
    float MaxDifficulty,
    int BloomsLevel,
    string? SkipReason);

public sealed record ReferenceClusterOutcome(
    string Track,
    string Topic,
    string Format,
    int Attempted,
    int PassedCas,
    int Dropped,
    string? Error);

public sealed record ReferenceRecreationResponse(
    string RunId,
    bool DryRun,
    string AnalysisPath,
    int PapersAnalyzed,
    IReadOnlyList<ReferenceClusterPlan> Plan,
    IReadOnlyList<ReferenceClusterOutcome>? Outcomes,
    int TotalPlannedCandidates,
    int TotalAttempted,
    int TotalPassedCas,
    int TotalDropped,
    string StartedBy,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

// ─── Service contract ────────────────────────────────────────────────────────

public interface IReferenceCalibratedGenerationService
{
    /// <summary>
    /// Plan (dry-run) or execute (wet-run) a reference-calibrated recreation
    /// run. Throws <see cref="ArgumentException"/> on invalid request bounds
    /// and <see cref="FileNotFoundException"/> when the analysis.json is
    /// missing — fail-fast, no fallback.
    /// </summary>
    Task<ReferenceRecreationResponse> RecreateAsync(
        ReferenceRecreationRequest request,
        string startedBy,
        CancellationToken ct = default);
}

// ─── analysis.json DTOs (matches bagrut-reference-analyzer.py output) ────────

internal sealed class ReferenceAnalysisFile
{
    [JsonPropertyName("schema_version")]
    public string? SchemaVersion { get; set; }

    [JsonPropertyName("papers_analyzed")]
    public int PapersAnalyzed { get; set; }

    [JsonPropertyName("by_track")]
    public Dictionary<string, ReferenceTrackSummary> ByTrack { get; set; } = new();
}

internal sealed class ReferenceTrackSummary
{
    [JsonPropertyName("papers")]
    public int Papers { get; set; }

    [JsonPropertyName("topic_hits")]
    public Dictionary<string, int> TopicHits { get; set; } = new();

    [JsonPropertyName("format_hits")]
    public Dictionary<string, int> FormatHits { get; set; } = new();
}

// ─── Implementation ──────────────────────────────────────────────────────────

public sealed class ReferenceCalibratedGenerationService : IReferenceCalibratedGenerationService
{
    private const string DefaultAnalysisPath = "corpus/bagrut/reference/analysis.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IAiGenerationService _ai;
    private readonly IQualityGateService _qualityGate;
    private readonly IDocumentStore _store;
    private readonly ILogger<ReferenceCalibratedGenerationService> _logger;

    public ReferenceCalibratedGenerationService(
        IAiGenerationService ai,
        IQualityGateService qualityGate,
        IDocumentStore store,
        ILogger<ReferenceCalibratedGenerationService> logger)
    {
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _qualityGate = qualityGate ?? throw new ArgumentNullException(nameof(qualityGate));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ReferenceRecreationResponse> RecreateAsync(
        ReferenceRecreationRequest request,
        string startedBy,
        CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(startedBy))
            throw new ArgumentException("startedBy is required.", nameof(startedBy));

        // Validation — mirror the CorpusExpanderHandler guard style.
        var maxPerCluster = Math.Clamp(request.MaxCandidatesPerCluster, 1, 20);
        var maxTotal = Math.Clamp(request.MaxTotalCandidates, 1, 200);

        if (request.MaxCandidatesPerCluster < 1)
            throw new ArgumentException("MaxCandidatesPerCluster must be >= 1.");
        if (request.MaxTotalCandidates < 1)
            throw new ArgumentException("MaxTotalCandidates must be >= 1.");

        var analysisPath = string.IsNullOrWhiteSpace(request.AnalysisJsonPath)
            ? DefaultAnalysisPath
            : request.AnalysisJsonPath!;

        var runId = $"run-{Guid.NewGuid():N}";
        var startedAt = DateTimeOffset.UtcNow;

        var analysis = LoadAnalysis(analysisPath);
        _logger.LogInformation(
            "[BAGRUT_RECREATION] run={RunId} analysis={Path} papers={PapersAnalyzed} dryRun={DryRun} by={By}",
            runId, analysisPath, analysis.PapersAnalyzed, request.DryRun, startedBy);

        // ── Plan ─────────────────────────────────────────────────────────
        var plan = BuildPlan(analysis, request, maxPerCluster, maxTotal);
        var totalPlanned = plan.Sum(p => p.WouldGenerate);

        // ── Dry-run short-circuit ────────────────────────────────────────
        if (request.DryRun)
        {
            var completedDry = DateTimeOffset.UtcNow;
            await AppendRunEventBestEffort(runId, analysisPath, analysis.PapersAnalyzed,
                plan.Count, totalAttempted: 0, totalPassedCas: 0, totalDropped: 0,
                request.DryRun, startedBy, startedAt, completedDry, ct);

            return new ReferenceRecreationResponse(
                RunId: runId,
                DryRun: true,
                AnalysisPath: analysisPath,
                PapersAnalyzed: analysis.PapersAnalyzed,
                Plan: plan,
                Outcomes: null,
                TotalPlannedCandidates: totalPlanned,
                TotalAttempted: 0,
                TotalPassedCas: 0,
                TotalDropped: 0,
                StartedBy: startedBy,
                StartedAt: startedAt,
                CompletedAt: completedDry);
        }

        // ── Wet run — drive BatchGenerateAsync per executable cluster ────
        var outcomes = new List<ReferenceClusterOutcome>(plan.Count);
        var totalAttempted = 0;
        var totalPassedCas = 0;
        var totalDropped = 0;

        foreach (var cluster in plan)
        {
            if (cluster.WouldGenerate <= 0)
            {
                outcomes.Add(new ReferenceClusterOutcome(
                    cluster.Track, cluster.Topic, cluster.Format,
                    Attempted: 0, PassedCas: 0, Dropped: 0,
                    Error: cluster.SkipReason));
                continue;
            }

            if (ct.IsCancellationRequested) break;

            int attempted = 0, passedCas = 0, dropped = 0;
            string? error = null;

            try
            {
                var batchRequest = new BatchGenerateRequest(
                    Count: cluster.WouldGenerate,
                    Subject: "math",                           // analyzer is math-only
                    Topic: cluster.Topic,
                    Grade: GradeForTrack(cluster.Track),
                    BloomsLevel: cluster.BloomsLevel,
                    MinDifficulty: cluster.MinDifficulty,
                    MaxDifficulty: cluster.MaxDifficulty,
                    Language: request.Language ?? "en");

                var response = await _ai.BatchGenerateAsync(batchRequest, _qualityGate);

                attempted = response.TotalGenerated;
                passedCas = response.PassedQualityGate;
                dropped = response.DroppedForCasFailure + response.AutoRejected;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[BAGRUT_RECREATION] run={RunId} cluster=({Track},{Topic},{Format}) failed",
                    runId, cluster.Track, cluster.Topic, cluster.Format);
                error = ex.GetType().Name;
            }

            outcomes.Add(new ReferenceClusterOutcome(
                cluster.Track, cluster.Topic, cluster.Format,
                attempted, passedCas, dropped, error));

            totalAttempted += attempted;
            totalPassedCas += passedCas;
            totalDropped += dropped;
        }

        var completedAt = DateTimeOffset.UtcNow;

        await AppendRunEventBestEffort(runId, analysisPath, analysis.PapersAnalyzed,
            plan.Count, totalAttempted, totalPassedCas, totalDropped,
            request.DryRun, startedBy, startedAt, completedAt, ct);

        _logger.LogInformation(
            "[BAGRUT_RECREATION] run={RunId} complete clusters={Clusters} attempted={Attempted} passed={Passed} dropped={Dropped} durationMs={Duration}",
            runId, plan.Count, totalAttempted, totalPassedCas, totalDropped,
            (completedAt - startedAt).TotalMilliseconds);

        return new ReferenceRecreationResponse(
            RunId: runId,
            DryRun: false,
            AnalysisPath: analysisPath,
            PapersAnalyzed: analysis.PapersAnalyzed,
            Plan: plan,
            Outcomes: outcomes,
            TotalPlannedCandidates: totalPlanned,
            TotalAttempted: totalAttempted,
            TotalPassedCas: totalPassedCas,
            TotalDropped: totalDropped,
            StartedBy: startedBy,
            StartedAt: startedAt,
            CompletedAt: completedAt);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Reads and parses the analysis.json file. Throws FileNotFoundException
    /// if it's missing — we never silently fall back to synthetic data,
    /// because that would defeat the whole reference-calibrated posture.
    /// </summary>
    internal static ReferenceAnalysisFile LoadAnalysis(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Reference analysis not found at '{path}'. Run scripts/bagrut-reference-analyzer.py first.",
                path);

        using var stream = File.OpenRead(path);
        var parsed = JsonSerializer.Deserialize<ReferenceAnalysisFile>(stream, JsonOpts);

        if (parsed is null)
            throw new InvalidDataException(
                $"Reference analysis at '{path}' failed to deserialize.");

        return parsed;
    }

    /// <summary>
    /// Expand track × topic × format tuples (with hits &gt; 0) into cluster
    /// plans. Budget halt applies across the flat plan in declaration order
    /// (tracks iterated, then topics within track, then formats within topic)
    /// so the first-listed track's clusters get preference when the total
    /// budget is tight — callers can reorder <see cref="ReferenceRecreationRequest.TargetTracks"/>
    /// to steer priority.
    /// </summary>
    internal static List<ReferenceClusterPlan> BuildPlan(
        ReferenceAnalysisFile analysis,
        ReferenceRecreationRequest request,
        int maxPerCluster,
        int maxTotal)
    {
        var plan = new List<ReferenceClusterPlan>();
        var runningTotal = 0;

        var trackOrder = request.TargetTracks?.ToList()
            ?? analysis.ByTrack.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        var topicFilter = request.TargetTopics?.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var track in trackOrder)
        {
            if (!analysis.ByTrack.TryGetValue(track, out var trackSummary))
                continue;

            var bandMin = DifficultyFloorForTrack(track);
            var bandMax = DifficultyCeilingForTrack(track);

            // Iterate topics deterministically (hit count desc, then alpha).
            var topics = trackSummary.TopicHits
                .Where(kv => kv.Value > 0)
                .Where(kv => topicFilter is null || topicFilter.Contains(kv.Key))
                .OrderByDescending(kv => kv.Value)
                .ThenBy(kv => kv.Key, StringComparer.Ordinal);

            foreach (var topicKv in topics)
            {
                var formats = trackSummary.FormatHits
                    .Where(kv => kv.Value > 0)
                    .OrderByDescending(kv => kv.Value)
                    .ThenBy(kv => kv.Key, StringComparer.Ordinal)
                    .ToList();

                // Fallback: if the analyzer found no format signal, generate
                // multiple_choice recreations by default — the most common
                // Bagrut format.
                if (formats.Count == 0)
                    formats.Add(new KeyValuePair<string, int>("multiple_choice", 1));

                foreach (var fmtKv in formats)
                {
                    var hits = topicKv.Value + fmtKv.Value;
                    var wouldGenerate = maxPerCluster;
                    string? skipReason = null;

                    if (runningTotal + wouldGenerate > maxTotal)
                    {
                        // Partial allocation when we still have budget left.
                        var remaining = Math.Max(0, maxTotal - runningTotal);
                        if (remaining == 0)
                        {
                            wouldGenerate = 0;
                            skipReason = "budget_exhausted";
                        }
                        else
                        {
                            wouldGenerate = remaining;
                            skipReason = $"budget_clamped:{remaining}";
                        }
                    }

                    plan.Add(new ReferenceClusterPlan(
                        Track: track,
                        Topic: topicKv.Key,
                        Format: fmtKv.Key,
                        Hits: hits,
                        WouldGenerate: wouldGenerate,
                        MinDifficulty: bandMin,
                        MaxDifficulty: bandMax,
                        BloomsLevel: BloomForFormat(fmtKv.Key),
                        SkipReason: skipReason));

                    runningTotal += wouldGenerate;
                }
            }
        }

        return plan;
    }

    private async Task AppendRunEventBestEffort(
        string runId,
        string analysisPath,
        int papersAnalyzed,
        int clustersPlanned,
        int totalAttempted, int totalPassedCas, int totalDropped,
        bool dryRun,
        string startedBy,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CancellationToken ct)
    {
        try
        {
            await using var session = _store.LightweightSession();
            var streamKey = $"bagrut-recreation:{runId}";
            session.Events.Append(streamKey, new ReferenceRecreationRun_V1(
                RunId: runId,
                AnalysisPath: analysisPath,
                PapersAnalyzed: papersAnalyzed,
                ClustersPlanned: clustersPlanned,
                TotalAttempted: totalAttempted,
                TotalPassedCas: totalPassedCas,
                TotalDropped: totalDropped,
                DryRun: dryRun,
                StartedBy: startedBy,
                StartedAt: startedAt,
                CompletedAt: completedAt));
            await session.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[BAGRUT_RECREATION] failed to append ReferenceRecreationRun_V1 for run={RunId}",
                runId);
        }
    }

    // Difficulty and grade mappings — intentionally simple; tuneable via
    // config later if analytics show skew. Bagrut tracks (3u/4u/5u) are
    // end-of-Year-12 exams, so grade is always "12".

    internal static float DifficultyFloorForTrack(string track) => track switch
    {
        "3u" => 0.3f,
        "4u" => 0.5f,
        "5u" => 0.7f,
        _    => 0.4f,
    };

    internal static float DifficultyCeilingForTrack(string track) => track switch
    {
        "3u" => 0.5f,
        "4u" => 0.7f,
        "5u" => 0.9f,
        _    => 0.6f,
    };

    internal static string GradeForTrack(string track) => "12";

    // Bloom inference from the format pattern the analyzer captured.
    //   proof          → 5 (Evaluate)
    //   computation    → 3 (Apply)
    //   multiple_choice→ 2 (Understand)  default
    internal static int BloomForFormat(string format) => format switch
    {
        "proof"       => 5,
        "computation" => 3,
        _             => 2,
    };
}
