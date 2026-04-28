// =============================================================================
// Cena Platform — S3DirectoryProvider unit tests (ADR-0058)
//
// Covers the guard rails that do not require a real Marten session or
// real S3: IsEnabled propagation, bucket allowlist enforcement,
// disabled-provider refusal. Full list/ingest happy-path coverage is in
// the LocalStack integration test (skip-gated by connectivity probe).
// =============================================================================

using Amazon.S3;
using Cena.Admin.Api.Ingestion;
using Cena.Api.Contracts.Admin.Ingestion;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Cena.Admin.Api.Tests.Ingestion;

public sealed class S3DirectoryProviderTests
{
    [Fact]
    public void IsEnabled_false_when_S3_section_absent()
    {
        var provider = Build(new IngestionOptions { S3 = null });
        Assert.False(provider.IsEnabled);
    }

    [Fact]
    public void IsEnabled_false_when_S3_Enabled_is_false()
    {
        var provider = Build(new IngestionOptions
        {
            S3 = new S3Options { Enabled = false, AllowedBuckets = { "cena-ingest-prod" } }
        });
        Assert.False(provider.IsEnabled);
    }

    [Fact]
    public void IsEnabled_false_when_AllowedBuckets_empty()
    {
        var provider = Build(new IngestionOptions
        {
            S3 = new S3Options { Enabled = true, AllowedBuckets = { } }
        });
        Assert.False(provider.IsEnabled);
    }

    [Fact]
    public void IsEnabled_true_when_enabled_and_allowlist_populated()
    {
        var provider = Build(new IngestionOptions
        {
            S3 = new S3Options { Enabled = true, AllowedBuckets = { "cena-ingest-prod" } }
        });
        Assert.True(provider.IsEnabled);
    }

    [Fact]
    public async Task ListAsync_throws_when_disabled()
    {
        var provider = Build(new IngestionOptions { S3 = null });

        var req = new CloudDirListRequest(
            Provider: "s3",
            BucketOrPath: "any-bucket",
            Prefix: null,
            ContinuationToken: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.ListAsync(req, CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_rejects_bucket_not_in_allowlist()
    {
        var provider = Build(new IngestionOptions
        {
            S3 = new S3Options { Enabled = true, AllowedBuckets = { "cena-ingest-prod" } }
        });

        var req = new CloudDirListRequest(
            Provider: "s3",
            BucketOrPath: "someone-elses-bucket",
            Prefix: null,
            ContinuationToken: null);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => provider.ListAsync(req, CancellationToken.None));
    }

    [Fact]
    public async Task IngestAsync_rejects_bucket_not_in_allowlist()
    {
        var provider = Build(new IngestionOptions
        {
            S3 = new S3Options { Enabled = true, AllowedBuckets = { "cena-ingest-prod" } }
        });

        var req = new CloudDirIngestRequest(
            Provider: "s3",
            BucketOrPath: "off-list-bucket",
            Prefix: null,
            FileKeys: new List<string> { "foo.pdf" });

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => provider.IngestAsync(req, CancellationToken.None));
    }

    [Fact]
    public async Task IngestAsync_throws_when_orchestrator_missing()
    {
        // S3 enabled + allowlist good, but no IIngestionOrchestrator →
        // dispatch must fail fast with a curator-readable message
        // rather than silently succeeding with filesQueued=0.
        var provider = Build(
            new IngestionOptions
            {
                S3 = new S3Options { Enabled = true, AllowedBuckets = { "cena-ingest-prod" } }
            },
            orchestrator: null);

        var req = new CloudDirIngestRequest(
            Provider: "s3",
            BucketOrPath: "cena-ingest-prod",
            Prefix: null,
            FileKeys: new List<string> { "foo.pdf" });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.IngestAsync(req, CancellationToken.None));
    }

    private static S3DirectoryProvider Build(
        IngestionOptions options,
        Cena.Actors.Ingest.IIngestionOrchestrator? orchestrator = null)
    {
        return new S3DirectoryProvider(
            s3: new Lazy<IAmazonS3>(() => Substitute.For<IAmazonS3>()),
            store: Substitute.For<IDocumentStore>(),
            options: Options.Create(options),
            logger: NullLogger<S3DirectoryProvider>.Instance,
            orchestrator: orchestrator);
    }
}
