// =============================================================================
// Cena Platform — IRefundUsageProbe (EPIC-PRR-I PRR-306)
//
// Abstraction over "how much did this parent's household use the product
// during the 30-day refund window". The RefundPolicy needs two counts —
// photo diagnostics and hint requests — and those counts live in two
// different bounded contexts (PhotoDiagnostic + StudentActor event log).
// This probe lets the policy be tested in isolation from either
// subsystem; the Marten-backed impl composes over the real sources.
//
// Why "probe" and not "counter": the policy needs a SINGLE snapshot
// summed across every linked student across the refund window. A
// counter would be a per-student or per-event seam; a probe is exactly
// the shape the policy consumes. Keeps the policy callsite concise.
// =============================================================================

namespace Cena.Actors.Subscriptions;

/// <summary>Counts returned by <see cref="IRefundUsageProbe"/>.</summary>
/// <param name="DiagnosticUploads">Sum across all linked students, whole window.</param>
/// <param name="HintRequests">Sum across all linked students, whole window.</param>
public sealed record RefundUsageCounts(long DiagnosticUploads, long HintRequests)
{
    /// <summary>Zero-usage snapshot for probes that return nothing.</summary>
    public static readonly RefundUsageCounts Zero = new(0, 0);
}

/// <summary>Probes the per-household usage counts that feed the abuse rule.</summary>
public interface IRefundUsageProbe
{
    /// <summary>
    /// Sum photo-diagnostic uploads + hint requests across every student
    /// linked to the subscription, between <paramref name="windowStartUtc"/>
    /// and <paramref name="windowEndUtc"/>.
    /// </summary>
    Task<RefundUsageCounts> GetAsync(
        IReadOnlyList<LinkedStudent> linkedStudents,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken ct);
}

/// <summary>
/// Zero-return probe used only in unit tests where policy logic is
/// exercised without a real usage backing. Not registered in production DI.
/// </summary>
public sealed class NoopRefundUsageProbe : IRefundUsageProbe
{
    /// <inheritdoc />
    public Task<RefundUsageCounts> GetAsync(
        IReadOnlyList<LinkedStudent> linkedStudents,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken ct) => Task.FromResult(RefundUsageCounts.Zero);
}
