// =============================================================================
// Cena Platform — InMemoryTaxonomyVersionStore (EPIC-PRR-J PRR-375)
//
// Production-grade in-memory implementation of ITaxonomyVersionStore. NOT a
// stub: every interface method honours the full contract including the
// two-reviewer guardrail, atomic next-version assignment, and audit-trail
// preservation on rollback/deprecate. This is the dev/test default and is
// safe for single-process hosts. Multi-host production environments wire
// MartenTaxonomyVersionStore instead.
//
// Concurrency model:
//   - _rows is a ConcurrentDictionary keyed by document Id (GUID).
//   - _keyLocks is a per-TemplateKey lock so Propose/Approve/Rollback/
//     Deprecate on the same key serialize their read-modify-write. Different
//     keys proceed in parallel. The per-key lock scope is small (a few
//     dictionary operations), so contention is negligible even at 1000x
//     dev-test load.
//   - The lock is acquired synchronously inside async methods because the
//     in-memory store has no I/O; returning Task.FromResult after a short
//     lock is well within the "in-memory = synchronous under the hood"
//     idiom used elsewhere (see InMemoryDiagnosticDisputeRepository,
//     InMemoryDiagnosticCreditLedger).
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic.Taxonomy;

/// <summary>
/// In-memory, concurrency-safe implementation of ITaxonomyVersionStore.
/// Registered as the default by PhotoDiagnosticServiceRegistration.AddPhotoDiagnostic.
/// </summary>
public sealed class InMemoryTaxonomyVersionStore : ITaxonomyVersionStore
{
    private readonly ConcurrentDictionary<Guid, TaxonomyVersionDocument> _rows = new();
    private readonly ConcurrentDictionary<string, object> _keyLocks =
        new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task UpsertAsync(TaxonomyVersionDocument doc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(doc);
        if (doc.Id == Guid.Empty)
            throw new ArgumentException("Id must be set.", nameof(doc));
        if (string.IsNullOrWhiteSpace(doc.TemplateKey))
            throw new ArgumentException("TemplateKey must be set.", nameof(doc));

        _rows[doc.Id] = doc;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<TaxonomyVersionDocument?> GetLatestApprovedAsync(
        string templateKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(templateKey))
            throw new ArgumentException("templateKey is required.", nameof(templateKey));

        TaxonomyVersionDocument? best = null;
        foreach (var row in _rows.Values)
        {
            if (!string.Equals(row.TemplateKey, templateKey, StringComparison.Ordinal))
                continue;
            if (row.Status != TaxonomyVersionStatus.Approved)
                continue;
            if (best is null || row.TaxonomyVersion > best.TaxonomyVersion)
                best = row;
        }
        return Task.FromResult(best);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TaxonomyVersionDocument>> ListVersionsAsync(
        string templateKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(templateKey))
            throw new ArgumentException("templateKey is required.", nameof(templateKey));

        var list = _rows.Values
            .Where(r => string.Equals(r.TemplateKey, templateKey, StringComparison.Ordinal))
            .OrderByDescending(r => r.TaxonomyVersion)
            .ToArray();
        return Task.FromResult<IReadOnlyList<TaxonomyVersionDocument>>(list);
    }

    /// <inheritdoc />
    public Task<TaxonomyVersionDocument> ProposeAsync(
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

        var keyLock = _keyLocks.GetOrAdd(templateKey, _ => new object());
        TaxonomyVersionDocument newRow;
        lock (keyLock)
        {
            var nextVersion = 1 + _rows.Values
                .Where(r => string.Equals(r.TemplateKey, templateKey, StringComparison.Ordinal))
                .Select(r => r.TaxonomyVersion)
                .DefaultIfEmpty(0)
                .Max();

            newRow = new TaxonomyVersionDocument
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
            _rows[newRow.Id] = newRow;
        }
        return Task.FromResult(newRow);
    }

    /// <inheritdoc />
    public Task<TaxonomyVersionDocument> ApproveAsync(
        Guid versionId,
        string reviewer,
        DateTimeOffset approvedAtUtc,
        CancellationToken ct)
    {
        if (versionId == Guid.Empty)
            throw new ArgumentException("versionId is required.", nameof(versionId));
        if (string.IsNullOrWhiteSpace(reviewer))
            throw new ArgumentException("reviewer is required.", nameof(reviewer));

        if (!_rows.TryGetValue(versionId, out var existing))
            throw new InvalidOperationException($"Version {versionId} not found.");

        // Terminal states cannot be re-approved.
        if (existing.Status is TaxonomyVersionStatus.Approved
            or TaxonomyVersionStatus.Deprecated
            or TaxonomyVersionStatus.RolledBack)
        {
            throw new InvalidOperationException(
                $"Version {versionId} is in terminal state {existing.Status}; "
                + "create a new version instead of re-approving.");
        }

        var keyLock = _keyLocks.GetOrAdd(existing.TemplateKey, _ => new object());
        TaxonomyVersionDocument updated;
        lock (keyLock)
        {
            // Re-read under lock to avoid a racing approve flipping the row
            // while we were preparing the update.
            if (!_rows.TryGetValue(versionId, out var current))
                throw new InvalidOperationException($"Version {versionId} not found.");

            // Dedupe reviewer list case-insensitively. Preserve insertion
            // order so the audit trail reads as "alice then bob".
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

            var reachesThreshold = distinctCount >= TaxonomyVersionDocument.MinReviewersForApproval;

            updated = current with
            {
                Reviewers = reviewers.AsReadOnly(),
                Status = reachesThreshold
                    ? TaxonomyVersionStatus.Approved
                    : TaxonomyVersionStatus.Proposed,
                ApprovedAtUtc = reachesThreshold ? approvedAtUtc : current.ApprovedAtUtc,
            };

            if (reachesThreshold)
            {
                // Demote any previously-Approved version of the same
                // TemplateKey to Deprecated so GetLatestApprovedAsync does
                // not return two "live" versions. This matches the deploy
                // semantic: a new approved version supersedes the old one.
                foreach (var kv in _rows.ToArray())
                {
                    var r = kv.Value;
                    if (r.Id == versionId) continue;
                    if (!string.Equals(r.TemplateKey, current.TemplateKey, StringComparison.Ordinal))
                        continue;
                    if (r.Status == TaxonomyVersionStatus.Approved)
                    {
                        _rows[kv.Key] = r with { Status = TaxonomyVersionStatus.Deprecated };
                    }
                }
            }

            _rows[versionId] = updated;
        }
        return Task.FromResult(updated);
    }

    /// <inheritdoc />
    public Task<TaxonomyVersionDocument> RollbackAsync(
        Guid versionId, CancellationToken ct)
    {
        if (versionId == Guid.Empty)
            throw new ArgumentException("versionId is required.", nameof(versionId));

        if (!_rows.TryGetValue(versionId, out var existing))
            throw new InvalidOperationException($"Version {versionId} not found.");

        var keyLock = _keyLocks.GetOrAdd(existing.TemplateKey, _ => new object());
        TaxonomyVersionDocument updated;
        lock (keyLock)
        {
            if (!_rows.TryGetValue(versionId, out var current))
                throw new InvalidOperationException($"Version {versionId} not found.");

            // Restore the most recent Deprecated version of the same key
            // back to Approved so the previous-live version resumes.
            // Choose the Deprecated row with the highest TaxonomyVersion
            // strictly less than the rolled-back row's version.
            TaxonomyVersionDocument? priorToResurrect = null;
            foreach (var kv in _rows)
            {
                var r = kv.Value;
                if (r.Id == versionId) continue;
                if (!string.Equals(r.TemplateKey, current.TemplateKey, StringComparison.Ordinal))
                    continue;
                if (r.Status != TaxonomyVersionStatus.Deprecated) continue;
                if (r.TaxonomyVersion >= current.TaxonomyVersion) continue;
                if (priorToResurrect is null
                    || r.TaxonomyVersion > priorToResurrect.TaxonomyVersion)
                {
                    priorToResurrect = r;
                }
            }

            updated = current with { Status = TaxonomyVersionStatus.RolledBack };
            _rows[versionId] = updated;

            if (priorToResurrect is not null)
            {
                _rows[priorToResurrect.Id] =
                    priorToResurrect with { Status = TaxonomyVersionStatus.Approved };
            }
        }
        return Task.FromResult(updated);
    }

    /// <inheritdoc />
    public Task<TaxonomyVersionDocument> DeprecateAsync(
        Guid versionId, CancellationToken ct)
    {
        if (versionId == Guid.Empty)
            throw new ArgumentException("versionId is required.", nameof(versionId));

        if (!_rows.TryGetValue(versionId, out var existing))
            throw new InvalidOperationException($"Version {versionId} not found.");

        var keyLock = _keyLocks.GetOrAdd(existing.TemplateKey, _ => new object());
        TaxonomyVersionDocument updated;
        lock (keyLock)
        {
            if (!_rows.TryGetValue(versionId, out var current))
                throw new InvalidOperationException($"Version {versionId} not found.");

            updated = current with { Status = TaxonomyVersionStatus.Deprecated };
            _rows[versionId] = updated;
        }
        return Task.FromResult(updated);
    }
}
