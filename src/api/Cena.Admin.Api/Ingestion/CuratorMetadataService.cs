// =============================================================================
// Cena Platform — Curator Metadata Service (RDY-019e-IMPL / Phase 1C)
//
// Owns the read/patch/delete surface for CuratorMetadata on a pipeline item
// plus the auto-extraction hook called during upload ingestion. Transitions
// the PipelineItemDocument.MetadataState per RDY-019e §4 state machine:
//
//   pending
//      │ upload + extractor produces ≥1 field
//      ▼
//   auto_extracted
//      │ curator opens the item (GET)           (GET flips to awaiting_review
//      ▼                                         if any required field missing)
//   awaiting_review
//      │ PATCH sets all required
//      ▼
//   confirmed     (cascade may use the hints)
//
// All required fields = { Subject, Language, SourceType }. Track is optional
// in the DB (a 3u/4u/5u exam may genuinely be pre-track). TaxonomyNode is
// curator-only (never auto-set).
//
// NO STUBS / NO MOCKS. Real Marten reads + writes; extractor is the real
// pipeline. No in-memory canned data.
// =============================================================================

using Cena.Actors.Ingest;
using Cena.Api.Contracts.Admin.Ingestion;
using Marten;
using Microsoft.Extensions.Logging;

namespace Cena.Admin.Api.Ingestion;

public interface ICuratorMetadataService
{
    /// <summary>
    /// Returns the current metadata view + auto-extracted snapshot for a
    /// pipeline item. Null = item not found.
    /// </summary>
    Task<CuratorMetadataResponse?> GetAsync(string itemId, CancellationToken ct = default);

    /// <summary>
    /// Merges a partial patch into the curator-owned values. Null patch
    /// fields are left alone (explicit clearance uses DeleteFieldAsync).
    /// Returns null if the item doesn't exist.
    /// </summary>
    Task<CuratorMetadataResponse?> PatchAsync(
        string itemId, CuratorMetadataPatch patch, string curatorId, CancellationToken ct = default);

    /// <summary>
    /// Clears a single field from the curator-owned values. Valid field
    /// names: subject, language, track, sourceType, taxonomyNode,
    /// expectedFigures. Returns null if item not found, false if field
    /// name is unknown.
    /// </summary>
    Task<CuratorMetadataResponse?> DeleteFieldAsync(
        string itemId, string fieldName, string curatorId, CancellationToken ct = default);

    /// <summary>
    /// Runs the auto-extractor on an upload and persists the result on the
    /// pipeline item document. Called by IngestionPipelineService during
    /// UploadFromRequestAsync. Idempotent — safe to call twice.
    /// </summary>
    Task<AutoExtractedMetadata?> AutoExtractAsync(
        string itemId,
        string filename,
        byte[] fileBytes,
        string contentType,
        CancellationToken ct = default);
}

public sealed class CuratorMetadataService : ICuratorMetadataService
{
    private static readonly string[] RequiredFields =
    {
        nameof(CuratorMetadata.Subject),
        nameof(CuratorMetadata.Language),
        nameof(CuratorMetadata.SourceType),
    };

    private readonly IDocumentStore _store;
    private readonly ICuratorMetadataExtractor _extractor;
    private readonly ILogger<CuratorMetadataService> _logger;

    public CuratorMetadataService(
        IDocumentStore store,
        ICuratorMetadataExtractor extractor,
        ILogger<CuratorMetadataService> logger)
    {
        _store = store;
        _extractor = extractor;
        _logger = logger;
    }

    public async Task<CuratorMetadataResponse?> GetAsync(string itemId, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();
        var item = await session.LoadAsync<PipelineItemDocument>(itemId, ct);
        if (item is null) return null;

        // GET during auto_extracted flips state to awaiting_review so the
        // UI can show the "please review" badge. We don't persist this
        // transition silently — any mutation saves via PATCH/DELETE.
        var state = item.MetadataState;
        if (state == "auto_extracted")
        {
            item.MetadataState = "awaiting_review";
            item.UpdatedAt = DateTimeOffset.UtcNow;
            session.Store(item);
            await session.SaveChangesAsync(ct);
            state = "awaiting_review";
        }

        return BuildResponse(item, state);
    }

    public async Task<CuratorMetadataResponse?> PatchAsync(
        string itemId, CuratorMetadataPatch patch, string curatorId, CancellationToken ct = default)
    {
        if (patch is null) throw new ArgumentNullException(nameof(patch));

        await using var session = _store.LightweightSession();
        var item = await session.LoadAsync<PipelineItemDocument>(itemId, ct);
        if (item is null) return null;

        var current = item.CuratorMetadata ?? new PipelineCuratorMetadata();
        if (patch.Subject         is not null) current.Subject         = patch.Subject;
        if (patch.Language        is not null) current.Language        = patch.Language;
        if (patch.Track           is not null) current.Track           = patch.Track;
        if (patch.SourceType      is not null) current.SourceType      = patch.SourceType;
        if (patch.TaxonomyNode    is not null) current.TaxonomyNode    = patch.TaxonomyNode;
        if (patch.ExpectedFigures is not null) current.ExpectedFigures = patch.ExpectedFigures;

        item.CuratorMetadata = current;
        item.UpdatedAt = DateTimeOffset.UtcNow;

        var state = DetermineState(current, isConfirmedTransition: false);
        if (state == "confirmed" && item.MetadataState != "confirmed")
        {
            item.MetadataConfirmedAt = DateTimeOffset.UtcNow;
            item.MetadataConfirmedBy = curatorId;
        }
        item.MetadataState = state;

        session.Store(item);
        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "CuratorMetadata patch: item={Item} curator={Curator} newState={State}",
            itemId, curatorId, state);

        return BuildResponse(item, state);
    }

    public async Task<CuratorMetadataResponse?> DeleteFieldAsync(
        string itemId, string fieldName, string curatorId, CancellationToken ct = default)
    {
        await using var session = _store.LightweightSession();
        var item = await session.LoadAsync<PipelineItemDocument>(itemId, ct);
        if (item is null) return null;

        var current = item.CuratorMetadata ?? new PipelineCuratorMetadata();
        var normalized = fieldName.Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "subject":          current.Subject = null; break;
            case "language":         current.Language = null; break;
            case "track":            current.Track = null; break;
            case "sourcetype":
            case "source_type":      current.SourceType = null; break;
            case "taxonomynode":
            case "taxonomy_node":    current.TaxonomyNode = null; break;
            case "expectedfigures":
            case "expected_figures": current.ExpectedFigures = null; break;
            default:
                throw new ArgumentException(
                    $"Unknown metadata field: '{fieldName}'. Valid: subject, language, track, source_type, taxonomy_node, expected_figures.",
                    nameof(fieldName));
        }

        item.CuratorMetadata = current;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        item.MetadataState = DetermineState(current, isConfirmedTransition: false);
        // Clearing a field always invalidates a prior confirmation.
        if (item.MetadataState != "confirmed")
        {
            item.MetadataConfirmedAt = null;
            item.MetadataConfirmedBy = null;
        }

        session.Store(item);
        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "CuratorMetadata delete field: item={Item} field={Field} curator={Curator} newState={State}",
            itemId, normalized, curatorId, item.MetadataState);

        return BuildResponse(item, item.MetadataState);
    }

    public async Task<AutoExtractedMetadata?> AutoExtractAsync(
        string itemId, string filename, byte[] fileBytes, string contentType, CancellationToken ct = default)
    {
        var extracted = await _extractor.ExtractAsync(filename, fileBytes, contentType, ct);
        if (extracted is null) return null;

        await using var session = _store.LightweightSession();
        var item = await session.LoadAsync<PipelineItemDocument>(itemId, ct);
        if (item is null)
        {
            _logger.LogWarning("CuratorMetadata auto-extract: item {ItemId} not found", itemId);
            return null;
        }

        item.AutoExtractedMetadata = ToPersisted(extracted.Extracted);
        item.MetadataExtractionStrategy = extracted.ExtractionStrategy;
        item.MetadataFieldConfidences =
            new Dictionary<string, double>(extracted.FieldConfidences, StringComparer.Ordinal);

        // Seed curator-facing view with the extracted values so the UI
        // shows them; the curator can edit or remove individual fields.
        item.CuratorMetadata ??= ToPersisted(extracted.Extracted);
        if (item.MetadataState == "pending") item.MetadataState = "auto_extracted";
        item.UpdatedAt = DateTimeOffset.UtcNow;

        session.Store(item);
        await session.SaveChangesAsync(ct);

        _logger.LogInformation(
            "CuratorMetadata auto-extracted: item={Item} strategy={Strategy} fields={Fields}",
            itemId, extracted.ExtractionStrategy,
            string.Join(",", extracted.FieldConfidences.Keys));

        return extracted;
    }

    // --------------------------------------------------------------------
    // helpers
    // --------------------------------------------------------------------
    private static CuratorMetadataResponse BuildResponse(PipelineItemDocument item, string state)
    {
        var current = ToContract(item.CuratorMetadata);
        var missing = MissingRequired(item.CuratorMetadata);

        AutoExtractedMetadata? auto = null;
        if (item.AutoExtractedMetadata is not null)
        {
            auto = new AutoExtractedMetadata(
                Extracted: ToContract(item.AutoExtractedMetadata)!,
                FieldConfidences: new Dictionary<string, double>(item.MetadataFieldConfidences, StringComparer.Ordinal),
                ExtractionStrategy: item.MetadataExtractionStrategy ?? "unknown");
        }

        return new CuratorMetadataResponse(
            ItemId: item.Id,
            MetadataState: state,
            AutoExtracted: auto,
            Current: current,
            MissingRequired: missing);
    }

    private static string DetermineState(PipelineCuratorMetadata current, bool isConfirmedTransition)
    {
        var missing = MissingRequired(current);
        return missing.Count == 0 ? "confirmed" : "awaiting_review";
    }

    private static IReadOnlyList<string> MissingRequired(PipelineCuratorMetadata? current)
    {
        if (current is null) return RequiredFields;
        var list = new List<string>(RequiredFields.Length);
        if (string.IsNullOrWhiteSpace(current.Subject))    list.Add(nameof(CuratorMetadata.Subject));
        if (string.IsNullOrWhiteSpace(current.Language))   list.Add(nameof(CuratorMetadata.Language));
        if (string.IsNullOrWhiteSpace(current.SourceType)) list.Add(nameof(CuratorMetadata.SourceType));
        return list;
    }

    private static PipelineCuratorMetadata ToPersisted(CuratorMetadata contract) => new()
    {
        Subject         = contract.Subject,
        Language        = contract.Language,
        Track           = contract.Track,
        SourceType      = contract.SourceType,
        TaxonomyNode    = contract.TaxonomyNode,
        ExpectedFigures = contract.ExpectedFigures,
    };

    private static CuratorMetadata? ToContract(PipelineCuratorMetadata? persisted)
    {
        if (persisted is null) return null;
        return new CuratorMetadata(
            Subject:         persisted.Subject,
            Language:        persisted.Language,
            Track:           persisted.Track,
            SourceType:      persisted.SourceType,
            TaxonomyNode:    persisted.TaxonomyNode,
            ExpectedFigures: persisted.ExpectedFigures);
    }
}
