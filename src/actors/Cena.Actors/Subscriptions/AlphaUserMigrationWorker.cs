// =============================================================================
// Cena Platform — AlphaUserMigrationWorker (EPIC-PRR-I PRR-344)
//
// One-shot hosted service that transitions pre-paywall alpha/beta users to
// the new subscription model. Policy (per PRR-344, ADR-0057 alpha-migration
// grace):
//   - Every operator-seeded alpha parent without a live subscription gets a
//     60-day grace-period marker (AlphaGraceMarker document, parent-keyed).
//   - The StudentEntitlementResolver consults IAlphaGraceMarkerReader and
//     synthesises a Premium StudentEntitlementView while the marker window
//     is active; at grace-end, the resolver falls through to Unsubscribed
//     unless a real subscription exists.
//   - Data (session history, mastery) is preserved; only the entitlement
//     side-channel flips.
//
// Seed source. Before PRR-344 the worker's CandidatesForGrace helper
// returned Array.Empty<string>() — the operator had no way to hand Cena
// the alpha-user list. IAlphaMigrationSeedSource is now injected; the
// admin endpoint POST /api/admin/alpha-migration/seed writes the list,
// and this worker emits grace markers for the delta. The worker is
// idempotent: re-running with the same seed does not duplicate markers
// (the Marten document id is the parent subject id, so Store() upserts
// but the already-granted HashSet short-circuits before we even call
// Store, keeping the write set tight for the common case).
//
// Why hosted-service-with-single-run. The migration is a one-shot event
// per deploy, but we keep it as a BackgroundService so a fresh seed
// upload + restart applies the list automatically. For in-between seed
// updates, /api/admin/alpha-migration/run-now calls RunMigrationOnceAsync
// out-of-band so ops don't have to trigger a rolling restart.
//
// Memory "No stubs — production grade" (2026-04-11): this is not a stub.
// The worker writes real documents that gate real entitlement decisions.
// The deferred bits (email template content, Vue admin view, the seed
// list itself) are explicitly documented in the PRR-344 task body.
// =============================================================================

using Marten;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Marker doc indicating a parent is inside the alpha-migration grace
/// period. The entitlement resolver treats this as Premium during the
/// window and falls back to Unsubscribed afterward.
/// </summary>
public sealed class AlphaGraceMarker
{
    public string Id { get; set; } = string.Empty;   // parentSubjectIdEncrypted
    public DateTimeOffset GraceStartAt { get; set; }
    public DateTimeOffset GraceEndAt { get; set; }
    public string Reason { get; set; } = "alpha-user";
}

/// <summary>
/// One-shot migration worker. Executes on first startup after deployment;
/// subsequent startups (or explicit /run-now calls) find markers already
/// in place and no-op against the already-granted set.
/// </summary>
public sealed class AlphaUserMigrationWorker : BackgroundService
{
    /// <summary>Grace period length (60 days per ADR-0057 alpha-migration policy).</summary>
    public static readonly TimeSpan GraceWindow = TimeSpan.FromDays(60);

    private readonly IDocumentStore _store;
    private readonly IAlphaMigrationSeedSource _seedSource;
    private readonly TimeProvider _clock;
    private readonly ILogger<AlphaUserMigrationWorker> _logger;

    public AlphaUserMigrationWorker(
        IDocumentStore store,
        IAlphaMigrationSeedSource seedSource,
        TimeProvider clock,
        ILogger<AlphaUserMigrationWorker> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _seedSource = seedSource ?? throw new ArgumentNullException(nameof(seedSource));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var count = await RunMigrationOnceAsync(stoppingToken);
            _logger.LogInformation(
                "AlphaUserMigrationWorker: granted grace to {Count} alpha parents.", count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AlphaUserMigrationWorker failed.");
        }
    }

    /// <summary>
    /// Idempotent: creates markers only for parents in the seed list that
    /// don't already have a marker and don't have an active subscription
    /// stream. Returns the number of new markers written.
    /// </summary>
    public async Task<int> RunMigrationOnceAsync(CancellationToken ct)
    {
        await using var session = _store.LightweightSession();

        // Pilot-scale: load all existing markers to skip.
        var existing = await session.Query<AlphaGraceMarker>().ToListAsync(ct);
        var alreadyGranted = existing.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);

        // Pilot-scale: load existing subscription streams to find parents with real subs.
        var events = await session.Events
            .QueryAllRawEvents()
            .Where(e => e.StreamKey != null && e.StreamKey.StartsWith(
                SubscriptionAggregate.StreamKeyPrefix))
            .ToListAsync(ct);
        var parentsWithSubs = events
            .Select(e => e.StreamKey!.Substring(SubscriptionAggregate.StreamKeyPrefix.Length))
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        // Operator-supplied seed (PRR-344 blocker fix). Empty list → no-op.
        var seed = await _seedSource.GetSeedParentIdsAsync(ct);

        var now = _clock.GetUtcNow();
        var graceEnd = now.Add(GraceWindow);
        var added = 0;

        foreach (var parentId in CandidatesForGrace(seed, alreadyGranted, parentsWithSubs))
        {
            session.Store(new AlphaGraceMarker
            {
                Id = parentId,
                GraceStartAt = now,
                GraceEndAt = graceEnd,
                Reason = "alpha-user",
            });
            added++;
        }

        if (added > 0)
        {
            await session.SaveChangesAsync(ct);
        }
        return added;
    }

    /// <summary>
    /// Pure helper for testability. Filter the <paramref name="seed"/> list
    /// down to parents that (a) don't already have an active grace marker,
    /// and (b) don't have any subscription-aggregate events on file.
    /// Parents in both the seed and the active-subscription set are skipped
    /// — they're already on a paid tier, granting grace would be
    /// double-entitling them and would confuse analytics.
    /// </summary>
    internal static IEnumerable<string> CandidatesForGrace(
        IReadOnlyList<string> seed,
        ISet<string> alreadyGranted,
        ISet<string> parentsWithSubs)
    {
        if (seed is null) yield break;
        foreach (var parentId in seed)
        {
            if (string.IsNullOrWhiteSpace(parentId)) continue;
            if (alreadyGranted.Contains(parentId)) continue;
            if (parentsWithSubs.Contains(parentId)) continue;
            yield return parentId;
        }
    }
}
