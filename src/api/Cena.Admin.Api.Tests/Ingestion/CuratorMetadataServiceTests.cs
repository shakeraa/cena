// =============================================================================
// Cena Platform -- CuratorMetadataService tests (Phase 1C / RDY-019e-IMPL)
//
// Tests the service layer in isolation — Marten session is NSubstitute'd but
// behaviour is fully exercised (not the DB). Covers the state machine
// (pending → auto_extracted → awaiting_review → confirmed), PATCH merge
// rules, field deletion, and auto-extract persistence.
// =============================================================================

using Cena.Actors.Ingest;
using Cena.Admin.Api.Ingestion;
using Cena.Api.Contracts.Admin.Ingestion;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Cena.Admin.Api.Tests.Ingestion;

public sealed class CuratorMetadataServiceTests
{
    private readonly IDocumentStore _store = Substitute.For<IDocumentStore>();
    private readonly IDocumentSession _session = Substitute.For<IDocumentSession>();
    private readonly ICuratorMetadataExtractor _extractor =
        Substitute.For<ICuratorMetadataExtractor>();

    private readonly CuratorMetadataService _service;
    private PipelineItemDocument? _current;

    public CuratorMetadataServiceTests()
    {
        _store.LightweightSession().Returns(_session);
        _session.LoadAsync<PipelineItemDocument>(
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => _current);

        _service = new CuratorMetadataService(
            _store, _extractor, NullLogger<CuratorMetadataService>.Instance);
    }

    private PipelineItemDocument MakeItem(string state = "pending")
    {
        var item = new PipelineItemDocument
        {
            Id = "pi-abc",
            SourceFilename = "upload.pdf",
            MetadataState = state,
        };
        _current = item;
        return item;
    }

    // --- GET ---------------------------------------------------------------
    [Fact]
    public async Task GetAsync_Returns_Null_When_Item_Missing()
    {
        _current = null;
        var response = await _service.GetAsync("missing");
        Assert.Null(response);
    }

    [Fact]
    public async Task GetAsync_Transitions_AutoExtracted_To_AwaitingReview()
    {
        var item = MakeItem(state: "auto_extracted");
        item.CuratorMetadata = new PipelineCuratorMetadata { Subject = "math" };

        var response = await _service.GetAsync("pi-abc");

        Assert.NotNull(response);
        Assert.Equal("awaiting_review", response!.MetadataState);
        await _session.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAsync_Reports_MissingRequired_When_Incomplete()
    {
        var item = MakeItem(state: "awaiting_review");
        item.CuratorMetadata = new PipelineCuratorMetadata { Subject = "math" };

        var response = await _service.GetAsync("pi-abc");

        Assert.NotNull(response);
        Assert.Contains("Language",   response!.MissingRequired);
        Assert.Contains("SourceType", response.MissingRequired);
        Assert.DoesNotContain("Subject", response.MissingRequired);
    }

    // --- PATCH -------------------------------------------------------------
    [Fact]
    public async Task PatchAsync_Merges_Only_NonNull_Fields()
    {
        var item = MakeItem(state: "auto_extracted");
        item.CuratorMetadata = new PipelineCuratorMetadata
        {
            Subject = "math",
            Language = "he",
            Track = "5u",
        };

        var response = await _service.PatchAsync(
            "pi-abc",
            new CuratorMetadataPatch(SourceType: "bagrut_reference"),   // only this
            curatorId: "curator-1");

        Assert.NotNull(response);
        Assert.Equal("math",             response!.Current!.Subject);
        Assert.Equal("he",               response.Current.Language);
        Assert.Equal("5u",               response.Current.Track);       // untouched
        Assert.Equal("bagrut_reference", response.Current.SourceType);   // patched
    }

    [Fact]
    public async Task PatchAsync_Transitions_To_Confirmed_When_All_Required_Set()
    {
        var item = MakeItem(state: "awaiting_review");
        item.CuratorMetadata = new PipelineCuratorMetadata
        {
            Subject = "math",
            Language = "he",
        };

        var response = await _service.PatchAsync(
            "pi-abc",
            new CuratorMetadataPatch(SourceType: "bagrut_reference"),
            curatorId: "curator-1");

        Assert.NotNull(response);
        Assert.Equal("confirmed", response!.MetadataState);
        Assert.Empty(response.MissingRequired);
        Assert.NotNull(item.MetadataConfirmedAt);
        Assert.Equal("curator-1", item.MetadataConfirmedBy);
    }

    [Fact]
    public async Task PatchAsync_Null_Body_Throws()
    {
        MakeItem();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.PatchAsync("pi-abc", null!, "curator-1"));
    }

    [Fact]
    public async Task PatchAsync_Returns_Null_When_Item_Missing()
    {
        _current = null;
        var response = await _service.PatchAsync(
            "missing", new CuratorMetadataPatch(Subject: "math"), "curator-1");
        Assert.Null(response);
    }

    // --- DELETE ------------------------------------------------------------
    [Theory]
    [InlineData("subject",          "math",             "Language")]
    [InlineData("language",         "he",               "Subject")]
    [InlineData("source_type",      "bagrut_reference", "Subject")]
    [InlineData("expected_figures", "true",             "Language")]
    public async Task DeleteFieldAsync_Clears_Named_Field(string field, string originalValue, string survivor)
    {
        var item = MakeItem(state: "confirmed");
        item.CuratorMetadata = new PipelineCuratorMetadata
        {
            Subject = "math",
            Language = "he",
            SourceType = "bagrut_reference",
            ExpectedFigures = true,
        };
        item.MetadataConfirmedAt = DateTimeOffset.UtcNow;
        item.MetadataConfirmedBy = "curator-1";
        _ = originalValue;  // theory data documents which field carries which value

        var response = await _service.DeleteFieldAsync("pi-abc", field, "curator-2");

        Assert.NotNull(response);
        // Clearing ANY field invalidates prior confirmation unless it's still fully set.
        // For the fields in this theory the item ends up with a required field cleared
        // → state flips to awaiting_review and confirmation is wiped.
        if (field is "subject" or "language" or "source_type")
        {
            Assert.Equal("awaiting_review", response!.MetadataState);
            Assert.Null(item.MetadataConfirmedAt);
            Assert.Null(item.MetadataConfirmedBy);
        }
        else  // expected_figures is optional → confirmation holds
        {
            Assert.Equal("confirmed", response!.MetadataState);
        }
        // The "survivor" field stays populated.
        Assert.NotNull(typeof(CuratorMetadata)
            .GetProperty(survivor)?.GetValue(response.Current));
    }

    [Fact]
    public async Task DeleteFieldAsync_Unknown_Field_Throws()
    {
        MakeItem();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.DeleteFieldAsync("pi-abc", "gibberish", "curator-1"));
    }

    [Fact]
    public async Task DeleteFieldAsync_Returns_Null_When_Item_Missing()
    {
        _current = null;
        var response = await _service.DeleteFieldAsync("missing", "subject", "curator-1");
        Assert.Null(response);
    }

    // --- AutoExtract -------------------------------------------------------
    [Fact]
    public async Task AutoExtractAsync_Persists_Extracted_And_Flips_State()
    {
        var item = MakeItem(state: "pending");
        var extracted = new AutoExtractedMetadata(
            Extracted: new CuratorMetadata("math", "he", "5u", "bagrut_reference", null, null),
            FieldConfidences: new Dictionary<string, double>
            {
                ["Subject"] = 0.85,
                ["Language"] = 0.88,
                ["Track"] = 0.9,
                ["SourceType"] = 0.8,
            },
            ExtractionStrategy: "filename");
        _extractor.ExtractAsync(
                "upload.pdf", Arg.Any<byte[]>(), "application/pdf", Arg.Any<CancellationToken>())
            .Returns(extracted);

        var result = await _service.AutoExtractAsync(
            "pi-abc", "upload.pdf", new byte[] { 1, 2, 3 }, "application/pdf");

        Assert.NotNull(result);
        Assert.Equal("auto_extracted",     item.MetadataState);
        Assert.Equal("filename",           item.MetadataExtractionStrategy);
        Assert.Equal("math",               item.AutoExtractedMetadata!.Subject);
        // Curator-facing view seeded with extracted values.
        Assert.Equal("math",               item.CuratorMetadata!.Subject);
        Assert.Equal(0.85, item.MetadataFieldConfidences["Subject"]);
    }

    [Fact]
    public async Task AutoExtractAsync_No_Result_From_Extractor_Noop()
    {
        MakeItem();
        _extractor.ExtractAsync(
                Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((AutoExtractedMetadata?)null);

        var result = await _service.AutoExtractAsync(
            "pi-abc", "random.pdf", new byte[] { 1 }, "application/pdf");

        Assert.Null(result);
        await _session.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AutoExtractAsync_Item_Missing_Returns_Null()
    {
        _current = null;
        var extracted = new AutoExtractedMetadata(
            Extracted: new CuratorMetadata("math", null, null, null, null, null),
            FieldConfidences: new Dictionary<string, double> { ["Subject"] = 0.85 },
            ExtractionStrategy: "filename");
        _extractor.ExtractAsync(
                Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(extracted);

        var result = await _service.AutoExtractAsync(
            "missing-id", "upload.pdf", new byte[] { 1 }, "application/pdf");

        Assert.Null(result);
    }
}
