// =============================================================================
// Cena Platform -- IngestionPipelineService Cloud-Directory partial (ADR-0058)
//
// Pure dispatch: looks up the ICloudDirectoryProvider by the request's
// Provider identifier and delegates List/Ingest to it. All provider-
// specific logic (path-traversal guards, SHA-256 dedup, ETag match,
// S3 SDK calls, batch-size gates) now lives in
// Cena.Admin.Api.Ingestion.LocalDirectoryProvider + S3DirectoryProvider.
//
// Split kept as a separate partial for historical stability of imports /
// IDE-navigation; the file is intentionally tiny now.
// =============================================================================

using Cena.Admin.Api.Ingestion;
using IngestionDto = Cena.Api.Contracts.Admin.Ingestion;

namespace Cena.Admin.Api;

public sealed partial class IngestionPipelineService
{
    public Task<IngestionDto.CloudDirListResponse> ListCloudDirectoryAsync(
        IngestionDto.CloudDirListRequest request)
    {
        var provider = _cloudDirRegistry.Resolve(request.Provider);
        return provider.ListAsync(request, CancellationToken.None);
    }

    public Task<IngestionDto.CloudDirIngestResponse> IngestCloudDirectoryAsync(
        IngestionDto.CloudDirIngestRequest request)
    {
        var provider = _cloudDirRegistry.Resolve(request.Provider);
        return provider.IngestAsync(request, CancellationToken.None);
    }
}
