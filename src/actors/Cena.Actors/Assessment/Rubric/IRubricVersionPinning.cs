// =============================================================================
// Cena Platform — Rubric Version Pinning service (prr-033, ADR-0052)
//
// Owns the in-memory snapshot of BagrutRubric objects and serves them to
// grading / diagnostic seams. Contract:
//
//   * PinFor(examCode) returns the current pinned rubric for a track, or
//     null if the exam has no rubric yet (callers decide whether that
//     means fail-closed or fall through to the default).
//   * PinById("EXAM@1.0.0") resolves an addressable rubric — used by
//     audit replay so a grade rendered last week under v1.0.0 can still
//     be reconstructed even after v1.1.0 ships.
//   * ReloadAsync() replaces the snapshot atomically. Fail-closed — if
//     the new YAML does not parse or fails validation, the previous
//     snapshot stays live.
//
// Sign-off invariant:
//   Every rubric returned by this service has a non-empty SignOff triple.
//   The YAML loader rejects rubrics that violate this; the interface
//   surface therefore never yields an unsigned rubric.
//
// Tenant scoping:
//   Rubrics are cross-tenant (they describe Ministry rules, not tenant
//   content). The service is registered as a singleton and is NOT keyed
//   by tenant-id — same rubric serves every institute.
// =============================================================================

using Microsoft.Extensions.Logging;

namespace Cena.Actors.Assessment.Rubric;

public interface IRubricVersionPinning
{
    /// <summary>Current immutable snapshot. Never null after successful construction.</summary>
    RubricSnapshot Current { get; }

    /// <summary>Resolve the pinned rubric for an exam code. Null if none loaded.</summary>
    BagrutRubric? PinFor(string examCode);

    /// <summary>
    /// Resolve a rubric by its addressable id (<c>"EXAM_CODE@version"</c>).
    /// Today only the currently-loaded version is resolvable; historical
    /// replay against superseded rubric versions is future work (ADR-0052 §5).
    /// </summary>
    BagrutRubric? PinById(string rubricId);

    /// <summary>
    /// Reload rubrics from disk. Failures (parse / validation) leave the
    /// previous snapshot untouched and return <c>Accepted=false</c>.
    /// </summary>
    Task<RubricReloadOutcome> ReloadAsync(CancellationToken ct = default);
}

public sealed record RubricReloadOutcome(
    int RubricsLoaded,
    IReadOnlyList<string> ExamCodes,
    IReadOnlyList<string> Warnings,
    bool Accepted);

public sealed class RubricVersionPinningService : IRubricVersionPinning
{
    private readonly string _rubricDir;
    private readonly ILogger<RubricVersionPinningService> _logger;
    private readonly TimeProvider _time;
    private readonly object _sync = new();

    private RubricSnapshot _snapshot;

    public RubricVersionPinningService(
        string rubricDir,
        ILogger<RubricVersionPinningService> logger,
        TimeProvider? time = null)
    {
        _rubricDir = rubricDir ?? throw new ArgumentNullException(nameof(rubricDir));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? TimeProvider.System;

        _snapshot = RubricYamlLoader.Load(_rubricDir, _time.GetUtcNow());
        _logger.LogInformation(
            "[RUBRIC] initial load: {Count} rubrics from {Dir} " +
            "[{Codes}]",
            _snapshot.All.Count,
            _rubricDir,
            string.Join(",", _snapshot.ByExamCode.Keys));
    }

    public RubricSnapshot Current => Volatile.Read(ref _snapshot!);

    public BagrutRubric? PinFor(string examCode)
    {
        if (string.IsNullOrWhiteSpace(examCode)) return null;
        return Current.ByExamCode.TryGetValue(examCode, out var r) ? r : null;
    }

    public BagrutRubric? PinById(string rubricId)
    {
        if (string.IsNullOrWhiteSpace(rubricId)) return null;
        var parts = rubricId.Split('@', 2);
        if (parts.Length != 2) return null;
        var rubric = PinFor(parts[0]);
        if (rubric is null) return null;
        // Today: only current version is loaded. Historical-version resolution
        // is ADR-0052 §5 future work — event-store replay using the captured
        // RubricId stamp on every graded attempt.
        return string.Equals(rubric.RubricVersion, parts[1], StringComparison.Ordinal)
            ? rubric
            : null;
    }

    public Task<RubricReloadOutcome> ReloadAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var previous = Current;

        RubricSnapshot candidate;
        try
        {
            candidate = RubricYamlLoader.Load(_rubricDir, _time.GetUtcNow());
        }
        catch (RubricLoadException ex)
        {
            _logger.LogError(ex, "[RUBRIC] reload refused — parse/validation error");
            return Task.FromResult(new RubricReloadOutcome(
                RubricsLoaded: previous.All.Count,
                ExamCodes: previous.ByExamCode.Keys.OrderBy(k => k).ToArray(),
                Warnings: new[] { $"parse_failure: {ex.Message}" },
                Accepted: false));
        }

        lock (_sync)
        {
            Volatile.Write(ref _snapshot!, candidate);
        }

        _logger.LogInformation(
            "[RUBRIC] reloaded: {Old} → {New} rubrics",
            previous.All.Count, candidate.All.Count);

        return Task.FromResult(new RubricReloadOutcome(
            RubricsLoaded: candidate.All.Count,
            ExamCodes: candidate.ByExamCode.Keys.OrderBy(k => k).ToArray(),
            Warnings: Array.Empty<string>(),
            Accepted: true));
    }
}
