// =============================================================================
// Cena Platform — LocalDirectoryProvider unit tests (ADR-0058)
//
// Guard rails that don't require Marten: IsEnabled propagation,
// path-traversal rejection. Full list/ingest happy-path coverage is in
// the existing Ingestion endpoint tests (which exercise the dispatch
// path end-to-end).
// =============================================================================

using Cena.Admin.Api.Ingestion;
using Cena.Api.Contracts.Admin.Ingestion;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Cena.Admin.Api.Tests.Ingestion;

public sealed class LocalDirectoryProviderTests
{
    [Fact]
    public void IsEnabled_false_when_allowlist_empty()
    {
        var provider = Build(new IngestionOptions { CloudWatchDirs = { } });
        Assert.False(provider.IsEnabled);
    }

    [Fact]
    public void IsEnabled_true_when_allowlist_has_at_least_one_entry()
    {
        var tmp = Directory.CreateTempSubdirectory();
        try
        {
            var provider = Build(new IngestionOptions { CloudWatchDirs = { tmp.FullName } });
            Assert.True(provider.IsEnabled);
        }
        finally
        {
            tmp.Delete();
        }
    }

    [Fact]
    public async Task ListAsync_throws_when_disabled()
    {
        var provider = Build(new IngestionOptions { CloudWatchDirs = { } });

        var req = new CloudDirListRequest(
            Provider: "local",
            BucketOrPath: "/some/path",
            Prefix: null,
            ContinuationToken: null);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.ListAsync(req, CancellationToken.None));
    }

    [Fact]
    public async Task ListAsync_rejects_path_outside_allowlist()
    {
        var allowed = Directory.CreateTempSubdirectory();
        try
        {
            var provider = Build(new IngestionOptions { CloudWatchDirs = { allowed.FullName } });

            // A path that is NOT under the allowed root. Using /etc on
            // non-Windows and C:\Windows on Windows — both are real
            // absolute paths that won't be a subpath of our temp dir.
            var outsidePath = OperatingSystem.IsWindows()
                ? @"C:\Windows"
                : "/etc";

            var req = new CloudDirListRequest(
                Provider: "local",
                BucketOrPath: outsidePath,
                Prefix: null,
                ContinuationToken: null);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => provider.ListAsync(req, CancellationToken.None));
        }
        finally
        {
            allowed.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task IngestAsync_rejects_path_outside_allowlist()
    {
        var allowed = Directory.CreateTempSubdirectory();
        try
        {
            var provider = Build(new IngestionOptions { CloudWatchDirs = { allowed.FullName } });

            var outsidePath = OperatingSystem.IsWindows()
                ? @"C:\Windows"
                : "/etc";

            var req = new CloudDirIngestRequest(
                Provider: "local",
                BucketOrPath: outsidePath,
                Prefix: null,
                FileKeys: new List<string> { "passwd" });

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => provider.IngestAsync(req, CancellationToken.None));
        }
        finally
        {
            allowed.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task IngestAsync_throws_when_orchestrator_missing()
    {
        var allowed = Directory.CreateTempSubdirectory();
        try
        {
            var provider = Build(
                new IngestionOptions { CloudWatchDirs = { allowed.FullName } },
                orchestrator: null);

            var req = new CloudDirIngestRequest(
                Provider: "local",
                BucketOrPath: allowed.FullName,
                Prefix: null,
                FileKeys: new List<string>());

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.IngestAsync(req, CancellationToken.None));
        }
        finally
        {
            allowed.Delete(recursive: true);
        }
    }

    private static LocalDirectoryProvider Build(
        IngestionOptions options,
        Cena.Actors.Ingest.IIngestionOrchestrator? orchestrator = null)
    {
        return new LocalDirectoryProvider(
            store: Substitute.For<IDocumentStore>(),
            options: Options.Create(options),
            logger: NullLogger<LocalDirectoryProvider>.Instance,
            scopeFactory: Substitute.For<IServiceScopeFactory>(),
            orchestrator: orchestrator);
    }
}
