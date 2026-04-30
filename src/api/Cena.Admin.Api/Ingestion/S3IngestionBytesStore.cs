// =============================================================================
// Cena Platform — S3 implementation of IIngestionBytesStore (PRR-RETRY-IMPL).
// Lives here (Cena.Admin.Api) rather than Cena.Actors because AWSSDK.S3 is
// a project reference here, not in Actors. Both projects coexist in the
// same hosts (Actor.Host + Admin.Api.Host) so the runtime DI graph
// resolves IIngestionBytesStore from whichever impl was registered.
//
// Reuses the same Lazy<IAmazonS3> wired by CloudDirectoryServiceCollectionExtensions
// (ADR-0058) — no double-registration, no second AWS client.
// =============================================================================

using Amazon.S3;
using Amazon.S3.Model;
using Cena.Actors.Ingest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Admin.Api.Ingestion;

public sealed class S3IngestionBytesStore : IIngestionBytesStore
{
    private readonly Lazy<IAmazonS3> _s3;
    private readonly string _bucket;
    private readonly ILogger<S3IngestionBytesStore> _logger;

    public S3IngestionBytesStore(
        Lazy<IAmazonS3> s3,
        IOptions<IngestionBytesStoreOptions> options,
        ILogger<S3IngestionBytesStore> logger)
    {
        _s3 = s3;
        _bucket = options.Value.S3Bucket
            ?? throw new InvalidOperationException(
                "Cena:Ingestion:BytesStore:S3Bucket must be configured when Backend=s3");
        _logger = logger;
    }

    public async Task<bool> PutAsync(string key, byte[] bytes, string contentType, CancellationToken ct)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            await _s3.Value.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key,
                InputStream = ms,
                ContentType = string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType,
                AutoCloseStream = false,
            }, ct);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "S3BytesStore: PUT failed for s3://{Bucket}/{Key}", _bucket, key);
            return false;
        }
    }

    public async Task<byte[]?> GetAsync(string key, CancellationToken ct)
    {
        try
        {
            using var resp = await _s3.Value.GetObjectAsync(_bucket, key, ct);
            using var ms = new MemoryStream();
            await resp.ResponseStream.CopyToAsync(ms, ct);
            return ms.ToArray();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        try
        {
            await _s3.Value.DeleteObjectAsync(_bucket, key, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "S3BytesStore: DELETE failed for s3://{Bucket}/{Key}", _bucket, key);
        }
    }
}
