// =============================================================================
// Cena Platform — Waterfall Options (prr-201)
//
// Tuning knobs injected via IOptions<T> at DI time. Every default matches
// the ADR-0043 / design-note contract and is explicit here so the admin
// ConfigMap is the only place that overrides them.
// =============================================================================

namespace Cena.Actors.QuestionBank.Coverage;

public sealed class CoverageWaterfallOptions
{
    /// <summary>
    /// Per-institute-per-day budget cap for stage-2 LLM calls, in USD.
    /// Default $50 — matches prr-201 task body. Exceeding this cap short-
    /// circuits to stage 3 (curator enqueue) without invoking the generator.
    /// </summary>
    public double InstituteDailyBudgetUsd { get; set; } = 50.0;

    /// <summary>
    /// Ministry-similarity cosine threshold above which stage-2 candidates
    /// are rejected as too close to Ministry source text (ADR-0043).
    /// Default 0.82.
    /// </summary>
    public double MinistrySimilarityThreshold { get; set; } = 0.82;

    /// <summary>
    /// Maximum number of stage-2 attempts per rung. Each attempt is one
    /// call to <see cref="IIsomorphGenerator"/>; a single call may return
    /// multiple candidates. We stop as soon as the target is met OR we hit
    /// this ceiling.
    /// </summary>
    public int MaxIsomorphAttempts { get; set; } = 3;

    /// <summary>
    /// Deadline delta for stage-3 curator tasks. The orchestrator sets the
    /// curator task's deadline to <c>releaseDate - CuratorLeadTime</c> so
    /// the human has this much time before ship.
    /// </summary>
    public TimeSpan CuratorLeadTime { get; set; } = TimeSpan.FromDays(3);

    /// <summary>
    /// Default release date used when the caller does not supply one. The
    /// orchestrator accepts an override per FillRungAsync call.
    /// </summary>
    public DateTimeOffset DefaultReleaseDate { get; set; } = DateTimeOffset.UtcNow.AddDays(30);
}
