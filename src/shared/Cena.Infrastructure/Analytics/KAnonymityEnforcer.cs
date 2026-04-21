// =============================================================================
// Cena Platform — k-Anonymity Enforcer (prr-026)
//
// Platform primitive that enforces a minimum-group-size floor on every
// teacher/classroom/institute aggregate surface. Below the floor, the
// enforcer throws <see cref="InsufficientAnonymityException"/>; the endpoint
// layer translates the exception into a 404 (NOT 403 — we do not leak
// classroom existence across the anonymity boundary, see runbook below).
//
// Why a dedicated primitive (vs. inline checks):
//
//   Before prr-026, anonymity floors were scattered:
//     - ClassMasteryService hard-coded MinStudentsForAnonymity = 10
//     - TeacherDashboardEndpoint hard-coded MinClassSizeForAggregates = 3
//     - HeatmapEndpoint had no explicit floor at all (RDY-070 Phase 1A)
//
//   The persona-privacy + persona-redteam consensus on prr-026 is that every
//   aggregate teacher/classroom/institute surface that produces a statistical
//   claim (mean, band count, heatmap cell, ranking) MUST route through a
//   single enforcer so the floor is auditable and non-bypassable. Individual
//   endpoints can still raise the floor (e.g. the teacher dashboard keeps
//   k=3 for a teacher's direct roster of the minors they personally
//   coach), but ONLY by calling the enforcer with a higher k — never by
//   skipping it.
//
// Floor rationale (k=10 for classroom-teacher aggregates, per prr-026):
//
//   - k=10 is the Israel PPL Amendment 13 "small-group guidance" threshold
//     for education-sector statistical releases (MoH/Knesset 2026 pilot).
//   - k=10 is the Sweeney (2002) "Achieving k-Anonymity" paper's cited
//     practical floor for classroom-scale cohorts in the presence of a
//     quasi-identifier (grade + subject + date ≈ quasi-identifier for a
//     student in a small district).
//   - prr-049's teacher-dashboard floor of 3 is intentionally kept
//     lower because that payload is the teacher's direct roster of the
//     minors they personally coach — the teacher has a legitimate 1:1
//     pedagogical relationship. The k=10 floor applies to aggregates
//     shown to a teacher that are not in that 1:1 relationship (e.g. the
//     "institute rollup across classrooms", "grade-level cross-section",
//     "week-over-week class mean").
//
// Runbook — "the enforcer threw at 03:00 on a Bagrut exam morning":
//
//   1. The endpoint returns 404 with diagnostic_reason=below_anonymity_floor
//      to the teacher. No student data leaks. The teacher sees a "classroom
//      too small for class-wide view" message — pedagogically honest.
//   2. Dashboard: cena_k_anonymity_suppressed_total{surface,k} counter
//      shows which surface was hit. Non-zero is EXPECTED for small
//      cohorts — it is not an alert.
//   3. If the teacher insists they need the data: escalate to admin, who
//      can view per-student data via the direct student-detail endpoints
//      which DO NOT go through the enforcer (individual-detail surfaces
//      are governed by consent + IDOR guards, not k-anonymity).
//   4. No student's Bagrut stream is interrupted by a k-anonymity miss;
//      the /session/* and /attempt/* paths are not aggregate surfaces.
//
// Tenancy (ADR-0001): the enforcer itself is tenant-agnostic — it only
// counts group size. Tenant scoping is the caller's responsibility and is
// enforced at the endpoint layer via the same school_id claim guards that
// already police the teacher console.
//
// Session scope (ADR-0003): the enforcer is a pure in-memory size check;
// it never persists state, caches per-call data, or holds misconception
// tags. Safe to call from any surface, including session-scoped handlers.
//
// See tasks/pre-release-review/TASK-PRR-026-k-anonymity-floor-k-10-for-classroom-teacher-aggregates.md.
// =============================================================================

using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Analytics;

/// <summary>
/// Thrown when an aggregate surface attempts to serve a group smaller than
/// the configured k-anonymity floor. Endpoints MUST translate this into
/// HTTP 404 with diagnostic_reason=below_anonymity_floor — 403 would leak
/// existence of the classroom/institute/grade-level cohort.
/// </summary>
/// <remarks>
/// The exception message deliberately does NOT include the actual group
/// size — the caller already knows, and surfacing the count in an HTTP
/// response body or log would itself be a k-anonymity leak (an attacker
/// probing "class size &lt; k" is the canonical attack this floor blocks).
/// </remarks>
public sealed class InsufficientAnonymityException : Exception
{
    /// <summary>The surface-name that tripped the floor (for logs only).</summary>
    public string Surface { get; }

    /// <summary>The configured k threshold on that surface.</summary>
    public int K { get; }

    public InsufficientAnonymityException(string surface, int k)
        : base(
            $"Aggregate group size below k-anonymity floor for surface '{surface}' " +
            $"(k={k}). Endpoint MUST return 404 with diagnostic_reason=below_anonymity_floor.")
    {
        Surface = surface;
        K = k;
    }
}

/// <summary>
/// Platform primitive for k-anonymity floor enforcement on aggregate
/// teacher/classroom/institute surfaces. See file header for rationale.
/// </summary>
public interface IKAnonymityEnforcer
{
    /// <summary>
    /// Default classroom-teacher aggregate floor (prr-026). Used by the
    /// institute rollup and the new /analytics/aggregate surfaces. Surfaces
    /// with a legitimate 1:1 pedagogical relationship (teacher's own
    /// roster) may pass a lower k — but they MUST still call the enforcer
    /// so suppressions are counted.
    /// </summary>
    public const int DefaultClassroomAggregateK = 10;

    /// <summary>
    /// Assert that <paramref name="group"/> has at least
    /// <paramref name="k"/> distinct members. Throws
    /// <see cref="InsufficientAnonymityException"/> on violation after
    /// incrementing <c>cena_k_anonymity_suppressed_total{surface,k}</c>.
    /// </summary>
    /// <typeparam name="T">Element type; counted with deduplication.</typeparam>
    /// <param name="group">Group to measure. Null is treated as empty (fails the floor).</param>
    /// <param name="k">Required minimum size. Must be &gt;= 1.</param>
    /// <param name="surface">
    /// Human-readable surface name for the metric label and exception
    /// message. Stable across callers of the same endpoint so the
    /// dashboard groups hits correctly (e.g.
    /// <c>"/classrooms/{id}/analytics/mastery-aggregate"</c>).
    /// </param>
    /// <param name="equalityComparer">
    /// Optional comparer used to count distinct members. Defaults to
    /// <see cref="EqualityComparer{T}.Default"/>. Callers passing a list of
    /// student-ids should supply <see cref="StringComparer.Ordinal"/> to
    /// avoid accidental case-folding collisions.
    /// </param>
    void AssertMinimumGroupSize<T>(
        IEnumerable<T>? group,
        int k,
        string surface,
        IEqualityComparer<T>? equalityComparer = null);

    /// <summary>
    /// Non-throwing variant. Returns true iff the group meets the floor.
    /// Callers that prefer to serve a partially-suppressed payload (e.g.
    /// "hide this row but show the rest") use this overload and increment
    /// the suppression counter themselves via <see cref="RecordSuppression"/>.
    /// </summary>
    bool MeetsFloor<T>(
        IEnumerable<T>? group,
        int k,
        IEqualityComparer<T>? equalityComparer = null);

    /// <summary>
    /// Record a suppression event for observability purposes without
    /// throwing. Used by partial-suppression paths (e.g. heatmap row
    /// dropped because a single topic has fewer than k attempters).
    /// </summary>
    void RecordSuppression(string surface, int k);
}

/// <summary>
/// Default <see cref="IKAnonymityEnforcer"/>. Emits the
/// <c>cena_k_anonymity_suppressed_total</c> counter on every suppression
/// and logs a structured warning (no raw group contents).
/// </summary>
public sealed class KAnonymityEnforcer : IKAnonymityEnforcer
{
    /// <summary>Meter name the OTLP collector looks for.</summary>
    public const string MeterName = "Cena.Analytics.KAnonymity";

    /// <summary>
    /// Fully-qualified counter emitted to Prometheus. Matches the
    /// k-anonymity dashboard. Non-zero values are EXPECTED on small-cohort
    /// surfaces and are not an alert — they are the forensic trail that the
    /// floor fired.
    /// </summary>
    public const string CounterName = "cena_k_anonymity_suppressed_total";

    private readonly Counter<long> _suppressionCounter;
    private readonly ILogger<KAnonymityEnforcer> _logger;

    public KAnonymityEnforcer(IMeterFactory meterFactory, ILogger<KAnonymityEnforcer> logger)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        var meter = meterFactory.Create(MeterName, "1.0.0");
        _suppressionCounter = meter.CreateCounter<long>(
            CounterName,
            unit: "events",
            description:
                "k-anonymity suppression events, by surface+k. " +
                "Non-zero values are the forensic trail of floor firings — " +
                "expected on small-cohort surfaces, not an alert (prr-026).");
    }

    public void AssertMinimumGroupSize<T>(
        IEnumerable<T>? group,
        int k,
        string surface,
        IEqualityComparer<T>? equalityComparer = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surface);
        if (k < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(k), k,
                "k-anonymity floor must be >= 1. Callers passing 0 are almost " +
                "certainly using the wrong abstraction — the enforcer is not " +
                "the place for 'any size allowed' semantics.");
        }

        if (!MeetsFloor(group, k, equalityComparer))
        {
            RecordSuppression(surface, k);
            throw new InsufficientAnonymityException(surface, k);
        }
    }

    public bool MeetsFloor<T>(
        IEnumerable<T>? group,
        int k,
        IEqualityComparer<T>? equalityComparer = null)
    {
        if (k < 1) return true;         // non-positive k: everything passes.
        if (group is null) return false;

        // Distinct count with a short-circuit: we stop as soon as we see the
        // k-th distinct element so a huge enumerable with k=10 completes in
        // O(k) time, not O(n).
        var comparer = equalityComparer ?? EqualityComparer<T>.Default;
        var seen = new HashSet<T>(comparer);
        foreach (var item in group)
        {
            if (seen.Add(item) && seen.Count >= k) return true;
        }
        return false;
    }

    public void RecordSuppression(string surface, int k)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surface);
        if (k < 1) return;

        _suppressionCounter.Add(
            1,
            new KeyValuePair<string, object?>("surface", surface),
            new KeyValuePair<string, object?>("k", k));

        // Log category only — never the raw group. Callers already know
        // the surface + k; surfacing the actual size here would itself be
        // a leak.
        _logger.LogInformation(
            "[K_ANONYMITY_SUPPRESSED] surface={Surface} k={K} — " +
            "endpoint returning 404 below_anonymity_floor (prr-026).",
            surface, k);
    }
}

/// <summary>
/// No-op enforcer for unit tests that want to assert on call-site behaviour
/// without wiring a meter + logger. Production hosts NEVER register this.
/// </summary>
public sealed class NullKAnonymityEnforcer : IKAnonymityEnforcer
{
    /// <summary>Shared instance — stateless.</summary>
    public static readonly NullKAnonymityEnforcer Instance = new();

    private NullKAnonymityEnforcer() { }

    public void AssertMinimumGroupSize<T>(
        IEnumerable<T>? group,
        int k,
        string surface,
        IEqualityComparer<T>? equalityComparer = null)
    {
        // No-op; matches the "tests don't care" shape of NullPiiPromptScrubber.
    }

    public bool MeetsFloor<T>(
        IEnumerable<T>? group,
        int k,
        IEqualityComparer<T>? equalityComparer = null) => true;

    public void RecordSuppression(string surface, int k) { /* no-op */ }
}
