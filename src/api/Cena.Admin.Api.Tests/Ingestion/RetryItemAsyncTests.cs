// =============================================================================
// Cena Platform — IngestionPipelineService.RetryItemAsync tests (PRR-RETRY-IMPL).
//
// Service-layer behaviour with an NSubstitute'd Marten session, mirroring the
// pattern in CuratorMetadataServiceTests. Covers the three relevant branches:
//   1. doc missing → returns false
//   2. BytesPersisted=false → throws BYTES_NOT_PERSISTED, doc NOT mutated
//   3. BytesPersisted=true → resets to retriable state (RetryCount++, status,
//      stage, LastError null, UpdatedAt bumped) and returns true
// =============================================================================

using Cena.Actors.Ingest;
using Cena.Admin.Api;
using Cena.Admin.Api.Ingestion;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;

namespace Cena.Admin.Api.Tests.Ingestion;

public sealed class RetryItemAsyncTests
{
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IDocumentSession _session = Substitute.For<IDocumentSession>();
    private readonly IConnectionMultiplexer _redis = Substitute.For<IConnectionMultiplexer>();
    private readonly ICloudDirectoryProviderRegistry _registry =
        Substitute.For<ICloudDirectoryProviderRegistry>();

    private readonly IngestionPipelineService _service;
    private PipelineItemDocument? _current;

    public RetryItemAsyncTests()
    {
        _store.LightweightSession().Returns(_session);
        _session.LoadAsync<PipelineItemDocument>(
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => _current);

        _service = new IngestionPipelineService(
            store: _store,
            redis: _redis,
            logger: NullLogger<IngestionPipelineService>.Instance,
            cloudDirRegistry: _registry);
    }

    [Fact]
    public async Task Returns_False_When_Item_Missing()
    {
        _current = null;
        var ok = await _service.RetryItemAsync("missing");
        Assert.False(ok);
    }

    [Fact]
    public async Task Refuses_BytesNotPersisted_With_Distinct_Error()
    {
        _current = new PipelineItemDocument
        {
            Id = "pi-legacy",
            BytesPersisted = false,
            Status = "failed",
            CurrentStage = PipelineStage.Failed,
            RetryCount = 0,
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _service.RetryItemAsync("pi-legacy"));

        Assert.StartsWith("BYTES_NOT_PERSISTED", ex.Message);
        // doc must NOT be mutated when refusing — saving a stale state
        // would mislead the kanban view.
        await _session.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resets_To_Retriable_State_When_Bytes_Persisted()
    {
        var before = DateTimeOffset.UtcNow;
        _current = new PipelineItemDocument
        {
            Id = "pi-good",
            BytesPersisted = true,
            Status = "failed",
            CurrentStage = PipelineStage.Failed,
            RetryCount = 0,
            LastError = "OCR sidecar timeout",
            UpdatedAt = before.AddMinutes(-10),
        };

        var ok = await _service.RetryItemAsync("pi-good");

        Assert.True(ok);
        Assert.Equal(1, _current.RetryCount);
        Assert.Equal("processing", _current.Status);
        Assert.Equal(PipelineStage.Incoming, _current.CurrentStage);
        Assert.Null(_current.LastError);
        Assert.True(_current.UpdatedAt >= before);
        await _session.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Increments_RetryCount_On_Successive_Calls()
    {
        _current = new PipelineItemDocument
        {
            Id = "pi-multi",
            BytesPersisted = true,
            Status = "failed",
            CurrentStage = PipelineStage.Failed,
            RetryCount = 2,
        };

        await _service.RetryItemAsync("pi-multi");

        Assert.Equal(3, _current.RetryCount);
    }
}
