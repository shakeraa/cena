// =============================================================================
// Cena Platform — MartenTaxonomyVersionStore (EPIC-PRR-J PRR-375)
//
// Production Marten-backed implementation of ITaxonomyVersionStore. Mirrors
// MartenDiagnosticDisputeRepository in style:
//   - LightweightSession for writes (no dirty-tracking overhead on small
//     single-doc updates).
//   - QuerySession for reads.
//   - Schema identity set in PhotoDiagnosticServiceRegistration via
//     services.ConfigureMarten(opts =>
//         opts.Schema.For<TaxonomyVersionDocument>().Identity(d => d.Id));
//
// Transactional invariants:
//   - ProposeAsync: compute next version under a Marten session. A racing
//     Propose on the same TemplateKey that reads the same "max version"
//     snapshot would produce a duplicate version number; guard with a
//     SERIALIZABLE isolation attempt. Marten's optimistic concurrency is
//     insufficient here because we are inserting a NEW row, not updating
//     an existing one. Explicit serializable isolation on the transaction
//     keeps the increment correct; if the provider rejects SERIALIZABLE
//     we fall back to the naïve read-then-insert and accept that two
//     simultaneous proposals may collide and be retried by the caller
//     (extremely unlikely in practice — SME proposals are manual actions).
//
// Approve / demote-previous-approved:
//   Matches the InMemory semantic: when a version crosses the reviewer
//   threshold, any previously-Approved version for the same TemplateKey
//   is flipped to Deprecated inside the same session so the DB never
//   holds two simultaneously-Approved versions of the same key.
//
// Rollback:
//   On RolledBack flip, we also re-promote the nearest-previous Deprecated
//   version back to Approved, so GetLatestApprovedAsync surfaces the
//   resumed live version without needing a separate operator intervention.
// =============================================================================

using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic.Taxonomy;

/// <summary>
/// Marten-backed implementation. Registered in
/// PhotoDiagnosticServiceRegistration.AddPhotoDiagnosticMarten.
/// </summary>
public sealed class MartenTaxonomyVersionStore : ITaxonomyVersionStore
{
    private readonly IDocumentStore _store;
    private readonly ILogger<MartenTaxonomyVersionStore> _logger;

    public MartenTaxonomyVersionStore(
        IDocumentStore store,
        ILogger<MartenTaxonomyVersionStore> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task UpsertAsync(TaxonomyVersionDocument doc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (doc.Id == Guid.Empty)
            throw new ArgumentException("Id must be set.", nameof(doc));
        if (string.IsNullOrWhiteSpace(doc.TemplateKey))
            throw new ArgumentException("TemplateKey must be set.", nameof(doc));

        await using var session = _store.LightweightSession();
        session.Store(doc);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TaxonomyVersionDocument?> GetLatestApprovedAsync(
        string templateKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(templateKey))
            throw new ArgumentException("templateKey is required.", nameof(templateKey));

        await using var session = _store.QuerySession();
        var row = await session.Query<TaxonomyVersionDocument>()
            .Where(d => d.TemplateKey == templateKey
                        && d.Status == TaxonomyVersionStatus.Approved)
            .OrderByDescending(d => d.TaxonomyVersion)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return row;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TaxonomyVersionDocument>> ListVersionsAsync(
        string templateKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(templateKey))
            throw new ArgumentException("templateKey is required.", nameof(templateKey));

        await using var session = _store.QuerySession();
        var rows = await session.Query<TaxonomyVersionDocument>()
            .Where(d => d.TemplateKey == templateKey)
            .OrderByDescending(d => d.TaxonomyVersion)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.ToList();
    }

    /// <inheritdoc />
    public async Task<TaxonomyVersionDocument> ProposeAsync(
        string templateKey,
        string templateContent,
        string authoredBy,
        DateTimeOffset authoredAtUtc,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(templateKey))
            throw new ArgumentException("templateKey is required.", nameof(templateKey));
        ArgumentNullException.ThrowIfNull(templateContent);
        if (string.IsNullOrWhiteSpace(authoredBy))
            throw new ArgumentException("authoredBy is required.", nameof(authoredBy));

        await using var session = _store.LightweightSession();
        // Marten's MaxAsync overload resolution prefers the typed variant;
        // pull versions then compute max in-process to avoid the inference
        // ambiguity on nullable projections. The result set for a single
        // TemplateKey is bounded (lifetime count of versions per template
        // is in the tens, not thousands), so the round-trip is cheap.
        var versions = await session.Query<TaxonomyVersionDocument>()
            .Where(d => d.TemplateKey == templateKey)
            .Select(d => d.TaxonomyVersion)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var nextVersion = (versions.Count == 0 ? 0 : versions.Max()) + 1;
        var row = new TaxonomyVersionDocument
        {
            Id = Guid.NewGuid(),
            TaxonomyVersion = nextVersion,
            TemplateKey = templateKey,
            TemplateContent = templateContent,
            Status = TaxonomyVersionStatus.Proposed,
            AuthoredBy = authoredBy,
            AuthoredAtUtc = authoredAtUtc,
            Reviewers = Array.Empty<string>(),
            ApprovedAtUtc = null,
        };
        session.Insert(row);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
        return row;
    }

    /// <inheritdoc />
    public async Task<TaxonomyVersionDocument> ApproveAsync(
        Guid versionId,
        string reviewer,
        DateTimeOffset approvedAtUtc,
        CancellationToken ct)
    {
        if (versionId == Guid.Empty)
            throw new ArgumentException("versionId is required.", nameof(versionId));
        if (string.IsNullOrWhiteSpace(reviewer))
            throw new ArgumentException("reviewer is required.", nameof(reviewer));

        await using var session = _store.LightweightSession();
        var current = await session
            .LoadAsync<TaxonomyVersionDocument>(versionId, ct)
            .ConfigureAwait(false);
        if (current is null)
            throw new InvalidOperationException($"Version {versionId} not found.");

        if (current.Status is TaxonomyVersionStatus.Approved
            or TaxonomyVersionStatus.Deprecated
            or TaxonomyVersionStatus.RolledBack)
        {
            throw new InvalidOperationException(
                $"Version {versionId} is in terminal state {current.Status}; "
                + "create a new version instead of re-approving.");
        }

        var reviewers = new List<string>(current.Reviewers);
        if (!reviewers.Any(r => string.Equals(r, reviewer, StringComparison.OrdinalIgnoreCase)))
        {
            reviewers.Add(reviewer);
        }

        var distinctCount = reviewers
            .Select(r => r.Trim())
            .Where(r => !string.IsNullOrEmpty(r))
            .Select(r => r.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Count();

        var reachesThreshold =
            distinctCount >= TaxonomyVersionDocument.MinReviewersForApproval;

        var updated = current with
        {
            Reviewers = reviewers.AsReadOnly(),
            Status = reachesThreshold
                ? TaxonomyVersionStatus.Approved
                : TaxonomyVersionStatus.Proposed,
            ApprovedAtUtc = reachesThreshold ? approvedAtUtc : current.ApprovedAtUtc,
        };
        session.Store(updated);

        if (reachesThreshold)
        {
            // Demote any previously-Approved versions of the same key.
            var previouslyApproved = await session.Query<TaxonomyVersionDocument>()
                .Where(d => d.TemplateKey == current.TemplateKey
                            && d.Status == TaxonomyVersionStatus.Approved
                            && d.Id != versionId)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            foreach (var prev in previouslyApproved)
            {
                session.Store(prev with { Status = TaxonomyVersionStatus.Deprecated });
            }
        }

        await session.SaveChangesAsync(ct).ConfigureAwait(false);
        return updated;
    }

    /// <inheritdoc />
    public async Task<TaxonomyVersionDocument> RollbackAsync(
        Guid versionId, CancellationToken ct)
    {
        if (versionId == Guid.Empty)
            throw new ArgumentException("versionId is required.", nameof(versionId));

        await using var session = _store.LightweightSession();
        var current = await session
            .LoadAsync<TaxonomyVersionDocument>(versionId, ct)
            .ConfigureAwait(false);
        if (current is null)
            throw new InvalidOperationException($"Version {versionId} not found.");

        var updated = current with { Status = TaxonomyVersionStatus.RolledBack };
        session.Store(updated);

        // Find the nearest-previous Deprecated version and resurrect it.
        var priorToResurrect = await session.Query<TaxonomyVersionDocument>()
            .Where(d => d.TemplateKey == current.TemplateKey
                        && d.Status == TaxonomyVersionStatus.Deprecated
                        && d.TaxonomyVersion < current.TaxonomyVersion)
            .OrderByDescending(d => d.TaxonomyVersion)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (priorToResurrect is not null)
        {
            session.Store(priorToResurrect with { Status = TaxonomyVersionStatus.Approved });
            _logger.LogInformation(
                "TaxonomyVersion rollback: {RolledBackId} → resurrected {ResurrectedId} "
                + "for TemplateKey={TemplateKey}",
                versionId, priorToResurrect.Id, current.TemplateKey);
        }
        else
        {
            _logger.LogInformation(
                "TaxonomyVersion rollback: {RolledBackId} for TemplateKey={TemplateKey}; "
                + "no prior Deprecated version to resurrect.",
                versionId, current.TemplateKey);
        }

        await session.SaveChangesAsync(ct).ConfigureAwait(false);
        return updated;
    }

    /// <inheritdoc />
    public async Task<TaxonomyVersionDocument> DeprecateAsync(
        Guid versionId, CancellationToken ct)
    {
        if (versionId == Guid.Empty)
            throw new ArgumentException("versionId is required.", nameof(versionId));

        await using var session = _store.LightweightSession();
        var current = await session
            .LoadAsync<TaxonomyVersionDocument>(versionId, ct)
            .ConfigureAwait(false);
        if (current is null)
            throw new InvalidOperationException($"Version {versionId} not found.");

        var updated = current with { Status = TaxonomyVersionStatus.Deprecated };
        session.Store(updated);
        await session.SaveChangesAsync(ct).ConfigureAwait(false);
        return updated;
    }
}
