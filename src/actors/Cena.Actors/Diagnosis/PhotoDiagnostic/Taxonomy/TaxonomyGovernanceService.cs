// =============================================================================
// Cena Platform — TaxonomyGovernanceService (EPIC-PRR-J PRR-375)
//
// Thin application service composing ITaxonomyVersionStore + IDisputeMetricsService
// to surface the governance workflow:
//
//   FlagHighDisputeTemplatesAsync(threshold, ct)
//     Pulls the 7-day dispute snapshot and returns the slice keys whose
//     upheld-rate is >= threshold. The slice dimension today is
//     DisputeReason (projected to its enum name as a string) because the
//     existing DiagnosticDisputeDocument does not carry template/item/
//     locale ids. This is the same honest-scope caveat DisputeRateAggregator
//     documents — template-keyed slicing lands when the diagnostic→template
//     correlation ships, at which point this method projects real
//     template keys without the caller noticing. Until then the returned
//     strings are DisputeReason enum names; support triage treats them
//     as reason-family flags against which to prioritize taxonomy review.
//
//   RecordReviewAsync(templateKey, reviewer, approve, ct)
//     Workflow nudge used by the admin dashboard when an SME signs off (or
//     rejects) the latest Proposed row for a template. Approve=true routes
//     through ApproveAsync (which enforces ≥2 reviewers before transitioning
//     to Approved). Approve=false adds a non-approving reviewer mention as
//     a RolledBack-style signal — v1 simply skips the mutation and logs
//     the rejection via the return value (no state change), because the
//     Proposed→Proposed rejection path is a dashboard concern not a store
//     concern. Hosts that want explicit "SME rejected this proposal"
//     persistence can layer a TaxonomyReviewCommentDocument on top later;
//     that is deliberately out of scope for v1.
//
// Why no background worker:
//   The flag-and-review workflow is demand-driven (admin opens the
//   dashboard; endpoint calls FlagHighDisputeTemplatesAsync). A polling
//   worker would waste cycles; if alert-surface timeliness ever demands
//   proactive notification we layer it on top as an IHostedService
//   sibling without changing this interface.
//
// Honest-not-complimentary (memory 2026-04-20):
//   The returned flag list is the raw above-threshold slice with no
//   smoothing, no filtering-for-small-N, no "don't scare the SMEs with
//   2-dispute samples" clamp. The spec asks for >= threshold; we return
//   >= threshold. If that produces a noisy feed the right fix is a
//   minimum-sample gate on the caller's side (dashboard), not here.
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic.Taxonomy;

/// <summary>Application-service shape for the governance workflow.</summary>
public interface ITaxonomyGovernanceService
{
    /// <summary>
    /// Return the dispute slice keys whose upheld rate is ≥
    /// <paramref name="threshold"/> over the 7-day window.
    /// </summary>
    Task<IReadOnlyList<string>> FlagHighDisputeTemplatesAsync(
        double threshold, CancellationToken ct);

    /// <summary>
    /// Record a reviewer sign-off (approve=true) or rejection
    /// (approve=false) for the latest Proposed row of
    /// <paramref name="templateKey"/>. Returns the current state of the row
    /// after the action (or the unchanged latest row if there is no
    /// Proposed version to act on).
    /// </summary>
    Task<TaxonomyVersionDocument> RecordReviewAsync(
        string templateKey, string reviewer, bool approve, CancellationToken ct);
}

/// <inheritdoc />
public sealed class TaxonomyGovernanceService : ITaxonomyGovernanceService
{
    /// <summary>
    /// Default threshold — mirrors DisputeRateAggregator.DefaultAlertThreshold
    /// so the governance service and the dispute dashboard speak in the
    /// same units. 5% upheld-rate = flag for SME review.
    /// </summary>
    public const double DefaultFlagThreshold = 0.05;

    private readonly ITaxonomyVersionStore _store;
    private readonly IDisputeMetricsService _disputeMetrics;

    public TaxonomyGovernanceService(
        ITaxonomyVersionStore store,
        IDisputeMetricsService disputeMetrics)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _disputeMetrics = disputeMetrics
            ?? throw new ArgumentNullException(nameof(disputeMetrics));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> FlagHighDisputeTemplatesAsync(
        double threshold, CancellationToken ct)
    {
        if (threshold < 0.0 || threshold > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(threshold), threshold,
                "threshold must be in [0, 1].");
        }

        var snap = await _disputeMetrics
            .GetAsync(AggregationWindow.SevenDay, ct)
            .ConfigureAwait(false);

        var flagged = new List<string>();
        foreach (var kv in snap.PerReasonUpheldRate)
        {
            // Require a non-zero denominator so a zero/zero per-reason slice
            // does not fire a false positive. The upheld-rate dictionary is
            // built from the dense enum set — slices with zero disputes in
            // the window have rate 0.0, which trivially fails >= threshold
            // for any threshold > 0. But for threshold == 0 we'd flag every
            // zero slice; require at least one dispute in the per-reason
            // denominator before flagging.
            if (!snap.PerReasonCounts.TryGetValue(kv.Key, out var count)
                || count <= 0)
            {
                continue;
            }
            if (kv.Value >= threshold)
            {
                flagged.Add(kv.Key.ToString());
            }
        }
        return flagged;
    }

    /// <inheritdoc />
    public async Task<TaxonomyVersionDocument> RecordReviewAsync(
        string templateKey, string reviewer, bool approve, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(templateKey))
            throw new ArgumentException("templateKey is required.", nameof(templateKey));
        if (string.IsNullOrWhiteSpace(reviewer))
            throw new ArgumentException("reviewer is required.", nameof(reviewer));

        var versions = await _store
            .ListVersionsAsync(templateKey, ct)
            .ConfigureAwait(false);

        // Find the latest Proposed row — that's the one up for review.
        TaxonomyVersionDocument? proposed = null;
        foreach (var v in versions)
        {
            if (v.Status == TaxonomyVersionStatus.Proposed)
            {
                proposed = v;
                break; // list is desc-ordered, first Proposed = latest
            }
        }

        if (proposed is null)
        {
            // Nothing to review. Return the latest row (or synthesize an
            // empty document if the key is unknown) so callers get a
            // deterministic response shape.
            return versions.Count > 0
                ? versions[0]
                : new TaxonomyVersionDocument
                {
                    Id = Guid.Empty,
                    TaxonomyVersion = 0,
                    TemplateKey = templateKey,
                    TemplateContent = "",
                    Status = TaxonomyVersionStatus.Proposed,
                    AuthoredBy = "",
                    AuthoredAtUtc = DateTimeOffset.MinValue,
                    Reviewers = Array.Empty<string>(),
                    ApprovedAtUtc = null,
                };
        }

        if (!approve)
        {
            // v1: a rejection does not mutate the store. The dashboard
            // uses this signal to surface "reviewer X pushed back" but the
            // row stays Proposed until someone either approves or the
            // author supersedes with a new proposal. Explicit
            // TaxonomyReviewCommentDocument is a follow-up if needed.
            return proposed;
        }

        return await _store
            .ApproveAsync(proposed.Id, reviewer, DateTimeOffset.UtcNow, ct)
            .ConfigureAwait(false);
    }
}
