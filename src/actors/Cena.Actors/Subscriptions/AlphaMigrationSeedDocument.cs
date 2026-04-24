// =============================================================================
// Cena Platform — AlphaMigrationSeedDocument + MartenAlphaMigrationSeedSource
//                 (EPIC-PRR-I PRR-344)
//
// Why this exists. The InMemoryAlphaMigrationSeedSource is enough for a
// single-host dev box, but production (Actor Host + Admin Api Host) runs on
// multiple replicas. If the operator POSTs the seed to replica A and the
// migration worker ticks on replica B, B would see an empty seed and grant
// zero markers. Worse, a pod restart between POST and cron would silently
// drop the seed — an alpha user promised 60 days of Premium would open the
// app to Unsubscribed caps. That is exactly the class of "silently lose
// operator input" bug the 2026-04-11 no-stubs memory banned.
//
// The fix: persist the current seed as a single Marten document keyed by
// the literal "current" id. Replicas share the same canonical list; the
// seed survives pod restarts; overwrite semantics match the interface
// contract. The uploader + timestamp are recorded for the audit trail
// surfaced by GET /api/admin/alpha-migration/status.
//
// Encryption note. ParentSubjectIdsEncrypted carries subject ids that are
// already encrypted at the wire boundary (ADR-0038 crypto-shred + the
// Subscriptions bounded-context convention of postfix _Encrypted). The
// document stores them as-is — Marten sees opaque strings. Decryption, if
// ever needed, happens at the consumer boundary via ISubjectKeyStore.
// =============================================================================

using Marten;

namespace Cena.Actors.Subscriptions;

/// <summary>
/// Marten document holding the current alpha-migration seed list. Single
/// row, id = <see cref="CurrentId"/>. Overwritten on every admin upload.
/// </summary>
public sealed class AlphaMigrationSeedDocument
{
    /// <summary>Singleton document id. There is exactly one row per tenant.</summary>
    public const string CurrentId = "current";

    /// <summary>Marten identity. Always <see cref="CurrentId"/>.</summary>
    public string Id { get; set; } = CurrentId;

    /// <summary>
    /// The encrypted parent-subject-id list that should receive grace.
    /// Stored as-is per the ADR-0038 wire-encryption convention.
    /// </summary>
    public IReadOnlyList<string> ParentSubjectIdsEncrypted { get; set; } =
        Array.Empty<string>();

    /// <summary>Encrypted subject id of the admin that uploaded this seed.</summary>
    public string UploadedBy { get; set; } = string.Empty;

    /// <summary>When the seed was last overwritten (UTC).</summary>
    public DateTimeOffset UploadedAtUtc { get; set; }
}

/// <summary>
/// Marten-backed <see cref="IAlphaMigrationSeedSource"/>. Used in production
/// so the seed list survives pod restarts and is shared across replicas.
/// Reads are lightweight (single document load by id); writes overwrite
/// the singleton row and call <c>SaveChangesAsync</c> once.
/// </summary>
public sealed class MartenAlphaMigrationSeedSource : IAlphaMigrationSeedSource
{
    private readonly IDocumentStore _store;
    private readonly TimeProvider _clock;

    public MartenAlphaMigrationSeedSource(IDocumentStore store, TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> GetSeedParentIdsAsync(CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        var doc = await session.LoadAsync<AlphaMigrationSeedDocument>(
            AlphaMigrationSeedDocument.CurrentId, ct);
        if (doc is null) return Array.Empty<string>();
        return doc.ParentSubjectIdsEncrypted.ToArray();
    }

    /// <inheritdoc/>
    public async Task SeedAsync(
        IReadOnlyList<string> parentSubjectIdsEncrypted,
        string uploadedByAdminSubjectId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parentSubjectIdsEncrypted);
        if (string.IsNullOrWhiteSpace(uploadedByAdminSubjectId))
        {
            throw new ArgumentException(
                "uploadedByAdminSubjectId is required for audit trail.",
                nameof(uploadedByAdminSubjectId));
        }

        var clean = parentSubjectIdsEncrypted
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        await using var session = _store.LightweightSession();
        var doc = new AlphaMigrationSeedDocument
        {
            Id = AlphaMigrationSeedDocument.CurrentId,
            ParentSubjectIdsEncrypted = clean,
            UploadedBy = uploadedByAdminSubjectId,
            UploadedAtUtc = _clock.GetUtcNow(),
        };
        // Store overwrites; Marten upserts on id match.
        session.Store(doc);
        await session.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Load the full document (including uploader + timestamp). Used by
    /// the admin /status endpoint to render audit info; not part of the
    /// core <see cref="IAlphaMigrationSeedSource"/> contract because the
    /// worker does not care who uploaded, only what to grant.
    /// </summary>
    public async Task<AlphaMigrationSeedDocument?> LoadDocumentAsync(CancellationToken ct)
    {
        await using var session = _store.QuerySession();
        return await session.LoadAsync<AlphaMigrationSeedDocument>(
            AlphaMigrationSeedDocument.CurrentId, ct);
    }
}
