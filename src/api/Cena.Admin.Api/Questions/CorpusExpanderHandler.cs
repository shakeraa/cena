// =============================================================================
// Cena Platform — Corpus Expander Handler (RDY-059)
//
// One-shot operator tool: "take my seed stems (or a selector's result set)
// and fan them out into a CAS-verified variant corpus to fill taxonomy-leaf
// coverage gaps".
//
// Composition:
//   Selector → source list                 (real Marten query, no cache)
//   Planner  → per-source work items       (reads ContentCoverageService +
//                                          stops at full leaves / budget)
//   Runner   → GenerateSimilarHandler.RunCoreAsync per plan item
//   Event    → CorpusExpansionRun_V1 (audit)
//
// Budget controls (every one is a senior-architect guard):
//   * MaxTotalCandidates   — hard stop on LLM attempts
//   * StopAfterLeafFull    — don't oversaturate a leaf
//   * DryRun (default true)— operator must opt into spend
//   * SuperAdminOnly       — enforced at the endpoint
//   * existing CostCircuitBreaker — already inside AiGenerationService
//
// Failure model: one bad source must NOT abort the run. Per-source errors
// go into the outcome record; the loop continues.
// =============================================================================

using Cena.Actors.Events;
using Cena.Actors.Questions;
using Cena.Admin.Api.Content;
using Cena.Admin.Api.QualityGate;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Questions;

// ─── Request ──────────────────────────────────────────────────────────────
public sealed record DifficultyBandConfig(float Min, float Max, int Count);

public sealed record CorpusExpansionRequest(
    string SourceSelector,                                   // "seed" | "bagrut" | "concept:CAL-003" | "all"
    IReadOnlyList<DifficultyBandConfig> DifficultyBands,
    int     StopAfterLeafFull = 5,
    int     MaxTotalCandidates = 200,
    string? Language           = null,
    bool    DryRun             = true);

// ─── Response ─────────────────────────────────────────────────────────────
public sealed record PerSourcePlan(
    string  SourceId,
    string? LeafId,
    int     CurrentLeafCount,
    int     WouldGenerate,
    string? SkipReason);

public sealed record PerSourceOutcome(
    string  SourceId,
    int     Attempted,
    int     PassedCas,
    int     Dropped,
    string? Error);

public sealed record CorpusExpansionResponse(
    string RunId,
    bool   DryRun,
    string Selector,
    IReadOnlyList<PerSourcePlan>       Plan,
    IReadOnlyList<PerSourceOutcome>?   Outcomes,
    int    TotalPlannedCandidates,
    int    TotalAttempted,
    int    TotalPassedCas,
    int    TotalDropped,
    string StartedBy,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt);

// ─── Source provider (extracted for testability) ──────────────────────────
public interface ICorpusSourceProvider
{
    Task<IReadOnlyList<QuestionReadModel>> ResolveAsync(
        string selector, int maxSources, CancellationToken ct);
}

public sealed class MartenCorpusSourceProvider : ICorpusSourceProvider
{
    private readonly IDocumentStore _store;
    public MartenCorpusSourceProvider(IDocumentStore store) => _store = store;

    public async Task<IReadOnlyList<QuestionReadModel>> ResolveAsync(
        string selector, int maxSources, CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var q = session.Query<QuestionReadModel>();

        var sel = (selector ?? "").Trim().ToLowerInvariant();

        IQueryable<QuestionReadModel> filtered;
        if (sel == "seed")
        {
            filtered = q.Where(x => x.SourceType == "seed" || x.CreatedBy == "System");
        }
        else if (sel == "bagrut")
        {
            filtered = q.Where(x => x.SourceType == "bagrut-reference" || x.SourceType == "bagrut_reference");
        }
        else if (sel == "all")
        {
            filtered = q.Where(x => x.Status == "Published");
        }
        else if (sel.StartsWith("concept:"))
        {
            // Extract + normalise OUTSIDE the expression tree — Marten's LINQ
            // provider can't evaluate Range/Index expressions server-side.
            var conceptId = sel.Substring("concept:".Length).ToUpperInvariant();
            filtered = q.Where(x => x.Concepts.Contains(conceptId));
        }
        else
        {
            return Array.Empty<QuestionReadModel>();
        }

        return await filtered.Take(maxSources).ToListAsync(ct);
    }
}

// ─── Handler ──────────────────────────────────────────────────────────────
public static class CorpusExpanderHandler
{
    private const int MaxSourcesResolved = 500;   // safety net on the selector

    public static async Task<CorpusExpansionResponse> RunAsync(
        CorpusExpansionRequest request,
        ICorpusSourceProvider sourceProvider,
        IContentCoverageService coverage,
        IDocumentStore store,
        IAiGenerationService ai,
        IQualityGateService qualityGate,
        string startedBy,
        ILogger logger,
        CancellationToken ct = default)
    {
        var runId = $"run-{Guid.NewGuid():N}";
        var startedAt = DateTimeOffset.UtcNow;

        // --- Validate request -------------------------------------------
        if (request.DifficultyBands is null || request.DifficultyBands.Count == 0)
            throw new ArgumentException("At least one difficulty band is required.");

        foreach (var band in request.DifficultyBands)
        {
            if (band.Min < 0 || band.Min > 1 || band.Max < 0 || band.Max > 1)
                throw new ArgumentException("Difficulty band bounds must be within [0, 1].");
            if (band.Count < 1 || band.Count > 20)
                throw new ArgumentException("Difficulty band count must be within [1, 20].");
        }

        if (request.MaxTotalCandidates < 1)
            throw new ArgumentException("MaxTotalCandidates must be >= 1.");

        // --- Resolve sources --------------------------------------------
        var sources = await sourceProvider.ResolveAsync(
            request.SourceSelector, MaxSourcesResolved, ct);

        logger.LogInformation(
            "[CORPUS_EXPAND] run={RunId} selector={Selector} sources={SourceCount} dryRun={DryRun} by={By}",
            runId, request.SourceSelector, sources.Count, request.DryRun, startedBy);

        // --- Read coverage (one query, shared across all plan decisions) -
        var report = await coverage.BuildReportAsync(
            minItemsPerLeaf: request.StopAfterLeafFull, ct);

        var leafCounts = report.Tracks
            .SelectMany(t => t.Leaves)
            .ToDictionary(l => l.LeafId, l => l.QuestionCount, StringComparer.Ordinal);

        // Invert concept→leaf for fast source → leaf lookup.
        var conceptToLeaf = report.Tracks
            .SelectMany(t => t.Leaves)
            .GroupBy(l => l.ConceptId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().LeafId, StringComparer.OrdinalIgnoreCase);

        // --- Plan --------------------------------------------------------
        var plan = new List<PerSourcePlan>(sources.Count);
        var bandSum = request.DifficultyBands.Sum(b => b.Count);
        var runningCount = 0;

        foreach (var src in sources)
        {
            var leafId = src.Concepts?
                .Select(c => conceptToLeaf.GetValueOrDefault(c))
                .FirstOrDefault(l => l is not null);
            var currentCount = leafId is null ? 0 : leafCounts.GetValueOrDefault(leafId, 0);

            if (leafId is not null && currentCount >= request.StopAfterLeafFull)
            {
                plan.Add(new PerSourcePlan(src.Id, leafId, currentCount, 0,
                    $"leaf_full:{currentCount}>={request.StopAfterLeafFull}"));
                continue;
            }

            if (runningCount + bandSum > request.MaxTotalCandidates)
            {
                plan.Add(new PerSourcePlan(src.Id, leafId, currentCount, 0,
                    "budget_exhausted"));
                continue;
            }

            plan.Add(new PerSourcePlan(src.Id, leafId, currentCount, bandSum, null));
            runningCount += bandSum;
        }

        var totalPlannedCandidates = plan.Sum(p => p.WouldGenerate);

        // --- Dry-run short-circuit --------------------------------------
        if (request.DryRun)
        {
            var completedDry = DateTimeOffset.UtcNow;
            await AppendRunEventBestEffort(store, runId, request, sources.Count,
                totalAttempted: 0, totalPassedCas: 0, totalDropped: 0,
                startedBy, startedAt, completedDry, logger, ct);

            return new CorpusExpansionResponse(
                RunId:      runId,
                DryRun:     true,
                Selector:   request.SourceSelector,
                Plan:       plan,
                Outcomes:   null,
                TotalPlannedCandidates: totalPlannedCandidates,
                TotalAttempted:         0,
                TotalPassedCas:         0,
                TotalDropped:           0,
                StartedBy:  startedBy,
                StartedAt:  startedAt,
                CompletedAt: completedDry);
        }

        // --- Wet run -----------------------------------------------------
        var outcomes = new List<PerSourceOutcome>(plan.Count);
        var totalAttempted = 0;
        var totalPassedCas = 0;
        var totalDropped   = 0;

        foreach (var planItem in plan.Where(p => p.WouldGenerate > 0))
        {
            var attempted = 0;
            var passedCas = 0;
            var dropped   = 0;
            string? sourceError = null;

            foreach (var band in request.DifficultyBands)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    var bandRequest = new GenerateSimilarRequest(
                        Count:         band.Count,
                        MinDifficulty: band.Min,
                        MaxDifficulty: band.Max,
                        Language:      request.Language);

                    var core = await GenerateSimilarHandler.RunCoreAsync(
                        planItem.SourceId, bandRequest,
                        store, ai, qualityGate, startedBy, logger, ct);

                    if (core.ErrorCode is not null || core.Response is null)
                    {
                        sourceError ??= core.ErrorCode;
                        continue;
                    }

                    attempted += core.Response.TotalGenerated;
                    passedCas += core.Response.PassedQualityGate;
                    dropped   += (core.Response.DroppedForCasFailure
                                  + core.Response.AutoRejected);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "[CORPUS_EXPAND] run={RunId} source={SourceId} band=[{Min:F2},{Max:F2}] failed",
                        runId, planItem.SourceId, band.Min, band.Max);
                    sourceError ??= ex.GetType().Name;
                }
            }

            outcomes.Add(new PerSourceOutcome(
                SourceId: planItem.SourceId,
                Attempted: attempted,
                PassedCas: passedCas,
                Dropped:   dropped,
                Error:     sourceError));

            totalAttempted += attempted;
            totalPassedCas += passedCas;
            totalDropped   += dropped;
        }

        var completedAt = DateTimeOffset.UtcNow;
        await AppendRunEventBestEffort(store, runId, request, sources.Count,
            totalAttempted, totalPassedCas, totalDropped,
            startedBy, startedAt, completedAt, logger, ct);

        logger.LogInformation(
            "[CORPUS_EXPAND] run={RunId} complete attempted={Attempted} passed={Passed} dropped={Dropped} durationMs={Duration}",
            runId, totalAttempted, totalPassedCas, totalDropped,
            (completedAt - startedAt).TotalMilliseconds);

        return new CorpusExpansionResponse(
            RunId:      runId,
            DryRun:     false,
            Selector:   request.SourceSelector,
            Plan:       plan,
            Outcomes:   outcomes,
            TotalPlannedCandidates: totalPlannedCandidates,
            TotalAttempted:         totalAttempted,
            TotalPassedCas:         totalPassedCas,
            TotalDropped:           totalDropped,
            StartedBy:  startedBy,
            StartedAt:  startedAt,
            CompletedAt: completedAt);
    }

    private static async Task AppendRunEventBestEffort(
        IDocumentStore store,
        string runId,
        CorpusExpansionRequest request,
        int sourceCount,
        int totalAttempted, int totalPassedCas, int totalDropped,
        string startedBy, DateTimeOffset startedAt, DateTimeOffset completedAt,
        ILogger logger, CancellationToken ct)
    {
        try
        {
            await using var session = store.LightweightSession();
            var streamKey = $"corpus-expansion:{runId}";
            session.Events.Append(streamKey,
                new CorpusExpansionRun_V1(
                    RunId:          runId,
                    Selector:       request.SourceSelector,
                    SourceCount:    sourceCount,
                    TotalAttempted: totalAttempted,
                    TotalPassedCas: totalPassedCas,
                    TotalDropped:   totalDropped,
                    DryRun:         request.DryRun,
                    StartedBy:      startedBy,
                    StartedAt:      startedAt,
                    CompletedAt:    completedAt));
            await session.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[CORPUS_EXPAND] failed to append CorpusExpansionRun_V1 for run={RunId}", runId);
        }
    }
}
