// =============================================================================
// Cena Platform — Coverage Cell Variant Counter (prr-210)
//
// Exposes the per-cell variant-count metric that dashboards + the CI
// ship-gate both read. The metric is intentionally a simple gauge sourced
// from an in-process concurrent dictionary: the prr-201 projection updates
// it as variants land in the store, and prometheus scrapes it.
//
//   cena_coverage_cell_variant_count{topic,difficulty,methodology,track,questionType,language}
//
// The gauge is Long (variant counts are integers), process-scoped. The
// snapshot the ship-gate walks is produced out-of-band by the projection
// job writing ops/reports/coverage-variants-snapshot.json — this class
// exists so the runtime view (grafana / alertmanager) matches the CI view
// (coverage-slo.mjs) and drift between the two is observable.
//
// Usage:
//   _counter.Record(cell, variantCount);    // projection worker
//   _counter.MarkBelowSlo(cell, true);      // orchestrator when CuratorQueued
//
// Why a class and not a static helper:
//   Callers are tests + wiring — an injectable singleton is the standard
//   pattern in Cena.Actors and plays nicely with the ObservableGauge
//   lifetime (Meter instruments are cheap but the backing dictionary needs
//   a stable owner). See ADR-0026 for the metric-naming convention.
// =============================================================================

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Cena.Actors.QuestionBank.Coverage;

public interface ICoverageCellVariantCounter
{
    /// <summary>
    /// Record the current ready-variant count for a cell. Idempotent; calls
    /// overwrite the prior value. Use 0 to represent an empty cell — do not
    /// rely on the absence of a call to imply zero.
    /// </summary>
    void Record(CoverageCell cell, int variantCount);

    /// <summary>
    /// Mark whether the cell is currently below its SLO target. Surfaces the
    /// prr-201 "CuratorQueued" state to the metric so dashboards can alert
    /// independently of the CI ship-gate run cadence. The boolean is a
    /// companion to <see cref="Record"/>, not a replacement.
    /// </summary>
    void MarkBelowSlo(CoverageCell cell, bool belowSlo);

    /// <summary>
    /// Snapshot for diagnostics and the prr-201 projection job that writes
    /// the ops snapshot JSON the ship-gate reads. Ordering is not guaranteed.
    /// </summary>
    IReadOnlyCollection<CoverageCellVariantRecord> Snapshot();
}

public sealed record CoverageCellVariantRecord(
    CoverageCell Cell,
    int VariantCount,
    bool BelowSlo,
    DateTimeOffset UpdatedAt);

public sealed class CoverageCellVariantCounter : ICoverageCellVariantCounter
{
    private static readonly Meter Meter = new("Cena.Coverage", "1.0");

    // Address → (count, belowSlo, updatedAt, cell)
    private readonly ConcurrentDictionary<string, Entry> _entries = new();

    // Keep strong refs to the gauges so the Meter keeps publishing them.
    // We create two: one for the variant count (the headline metric) and
    // one for the below-SLO flag (0/1) so dashboards can AND them with the
    // count to alert on "cell in the red".
    private readonly ObservableGauge<long> _variantCountGauge;
    private readonly ObservableGauge<long> _belowSloGauge;

    public CoverageCellVariantCounter()
    {
        _variantCountGauge = Meter.CreateObservableGauge(
            "cena_coverage_cell_variant_count",
            () => EmitVariantCount(),
            description: "Ready, CAS-verified, author-owned variants per coverage cell (prr-210 SLO axis).");

        _belowSloGauge = Meter.CreateObservableGauge(
            "cena_coverage_cell_below_slo",
            () => EmitBelowSlo(),
            description: "1 if the cell is currently below its SLO target (prr-210), 0 otherwise.");
    }

    public void Record(CoverageCell cell, int variantCount)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (variantCount < 0)
            throw new ArgumentOutOfRangeException(nameof(variantCount));

        _entries.AddOrUpdate(
            cell.Address,
            _ => new Entry(cell, variantCount, false, DateTimeOffset.UtcNow),
            (_, prev) => prev with
            {
                VariantCount = variantCount,
                UpdatedAt = DateTimeOffset.UtcNow
            });
    }

    public void MarkBelowSlo(CoverageCell cell, bool belowSlo)
    {
        ArgumentNullException.ThrowIfNull(cell);

        _entries.AddOrUpdate(
            cell.Address,
            _ => new Entry(cell, 0, belowSlo, DateTimeOffset.UtcNow),
            (_, prev) => prev with
            {
                BelowSlo = belowSlo,
                UpdatedAt = DateTimeOffset.UtcNow
            });
    }

    public IReadOnlyCollection<CoverageCellVariantRecord> Snapshot()
    {
        return _entries.Values
            .Select(e => new CoverageCellVariantRecord(e.Cell, e.VariantCount, e.BelowSlo, e.UpdatedAt))
            .ToArray();
    }

    private IEnumerable<Measurement<long>> EmitVariantCount()
    {
        foreach (var e in _entries.Values)
        {
            yield return new Measurement<long>(
                e.VariantCount,
                LabelsFor(e.Cell));
        }
    }

    private IEnumerable<Measurement<long>> EmitBelowSlo()
    {
        foreach (var e in _entries.Values)
        {
            yield return new Measurement<long>(
                e.BelowSlo ? 1L : 0L,
                LabelsFor(e.Cell));
        }
    }

    private static KeyValuePair<string, object?>[] LabelsFor(CoverageCell cell) =>
    [
        new KeyValuePair<string, object?>("topic", cell.Topic),
        new KeyValuePair<string, object?>("difficulty", cell.Difficulty.ToString()),
        new KeyValuePair<string, object?>("methodology", cell.Methodology.ToString()),
        new KeyValuePair<string, object?>("track", cell.Track.ToString()),
        new KeyValuePair<string, object?>("questionType", cell.QuestionType),
        new KeyValuePair<string, object?>("language", cell.Language ?? "en"),
    ];

    private sealed record Entry(
        CoverageCell Cell,
        int VariantCount,
        bool BelowSlo,
        DateTimeOffset UpdatedAt);
}
