// =============================================================================
// Cena Platform — Cloud Directory Provider abstraction (ADR-0058)
//
// Seam used by IngestionPipelineService to list and ingest from external
// sources. Two implementations ship today:
//   • LocalDirectoryProvider — filesystem directory under CloudWatchDirs
//     allowlist (dev, CI, and cluster-local NFS drops)
//   • S3DirectoryProvider    — AWS S3 (or LocalStack in dev) under
//     S3Buckets allowlist
//
// Adding Azure Blob / GCS later: implement this interface; register in
// the DI extension. No changes to IngestionPipelineCloudDir.cs.
// =============================================================================

using IngestionDto = Cena.Api.Contracts.Admin.Ingestion;

namespace Cena.Admin.Api.Ingestion;

public interface ICloudDirectoryProvider
{
    /// <summary>
    /// Canonical provider identifier matching the `Provider` field in
    /// <see cref="IngestionDto.CloudDirListRequest"/> / <see cref="IngestionDto.CloudDirIngestRequest"/>.
    /// Examples: <c>"local"</c>, <c>"s3"</c>.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Whether the provider is wired for actual use (credentials present,
    /// allowlist non-empty, feature-flag on). Disabled providers throw at
    /// dispatch time so admins get an actionable error instead of silent
    /// empty results.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// List files at the given path/prefix. Returns a flat list with
    /// <c>AlreadyIngested</c> populated (based on provider-specific
    /// identity — ETag for S3, content-hash for local). Pagination via
    /// <see cref="IngestionDto.CloudDirListResponse.ContinuationToken"/>.
    /// </summary>
    Task<IngestionDto.CloudDirListResponse> ListAsync(
        IngestionDto.CloudDirListRequest request,
        CancellationToken ct);

    /// <summary>
    /// Download selected files and hand each one to the ingestion
    /// orchestrator. Enforces the provider's allowlist, batch-size gate,
    /// and dedup (skips already-ingested content).
    /// </summary>
    Task<IngestionDto.CloudDirIngestResponse> IngestAsync(
        IngestionDto.CloudDirIngestRequest request,
        CancellationToken ct);
}

/// <summary>
/// Registry looked up by <see cref="IngestionPipelineService"/> to route
/// cloud-directory requests to the correct provider. One instance per
/// application; populated by DI at startup with all registered
/// <see cref="ICloudDirectoryProvider"/>s.
/// </summary>
public interface ICloudDirectoryProviderRegistry
{
    /// <summary>
    /// Resolve a provider by its identifier string. Throws
    /// <see cref="InvalidOperationException"/> with a curator-readable
    /// message if the provider is unknown or disabled.
    /// </summary>
    ICloudDirectoryProvider Resolve(string providerId);

    /// <summary>
    /// All registered providers (enabled or disabled). Exposed for
    /// health-check / admin-UI surfaces that display provider status.
    /// </summary>
    IReadOnlyList<ICloudDirectoryProvider> All { get; }
}
