// =============================================================================
// Cena Platform -- BagrutIngestHandler tests (RDY-057 / Phase 3)
//
// Unit-tests the handler logic behind POST /api/admin/ingestion/bagrut.
// The endpoint itself is a thin minimal-API wrapper that parses the
// multipart form and defers to BagrutIngestHandler.HandleAsync; the
// authz gate (SuperAdminOnly) is covered by the existing AuthPolicyTests.
//
// These tests drive HandleAsync directly so no TestServer is needed.
// =============================================================================

using Cena.Admin.Api.Ingestion;
using Cena.Infrastructure.Ocr;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Cena.Admin.Api.Tests.Ingestion;

public sealed class BagrutIngestEndpointsTests
{
    private readonly IBagrutPdfIngestionService _service = Substitute.For<IBagrutPdfIngestionService>();

    private static byte[] PdfBytes(int size = 64)
    {
        var b = new byte[size];
        Array.Copy(new byte[] { 0x25, 0x50, 0x44, 0x46 }, b, 4);  // %PDF header
        return b;
    }

    private static int StatusOf(IResult result)
    {
        // IResult types from Results.Json / Results.Ok expose StatusCode via
        // reflection on common implementations. We use the production
        // executor pattern: write into a dummy HttpContext and read back.
        var ctx = new DefaultHttpContext
        {
            RequestServices = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
                .AddLogging()
                .BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
        result.ExecuteAsync(ctx).GetAwaiter().GetResult();
        return ctx.Response.StatusCode;
    }

    private static string BodyOf(IResult result)
    {
        var ctx = new DefaultHttpContext
        {
            RequestServices = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
                .AddLogging()
                .BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
        result.ExecuteAsync(ctx).GetAwaiter().GetResult();
        ctx.Response.Body.Position = 0;
        using var sr = new StreamReader(ctx.Response.Body);
        return sr.ReadToEnd();
    }

    // -------------------------------------------------------------------
    [Fact]
    public async Task Happy_Path_Returns_200_With_PdfIngestionResult()
    {
        var expected = new PdfIngestionResult(
            PdfId: "pdf-abc", ExamCode: "math-5u-2023", TotalPages: 1,
            QuestionsExtracted: 2, FiguresExtracted: 0,
            Drafts: Array.Empty<IngestionDraftQuestion>(),
            Warnings: Array.Empty<string>());
        _service.IngestAsync(Arg.Any<byte[]>(), "math-5u-2023", "admin-1", Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await BagrutIngestHandler.HandleAsync(
            examCode: "math-5u-2023",
            fileContentType: "application/pdf",
            fileBytes: PdfBytes(),
            uploadedBy: "admin-1",
            fallbackUserId: null,
            service: _service);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        Assert.Contains("pdf-abc", BodyOf(result));
    }

    [Fact]
    public async Task ExamCode_Uppercase_Is_Normalized()
    {
        _service.IngestAsync(Arg.Any<byte[]>(), "math-5u-2023", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PdfIngestionResult("pdf-x", "math-5u-2023", 0, 0, 0,
                Array.Empty<IngestionDraftQuestion>(), Array.Empty<string>()));

        var result = await BagrutIngestHandler.HandleAsync(
            examCode: "MATH-5U-2023",
            fileContentType: "application/pdf",
            fileBytes: PdfBytes(),
            uploadedBy: null,
            fallbackUserId: "admin-x",
            service: _service);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        await _service.Received(1).IngestAsync(
            Arg.Any<byte[]>(), "math-5u-2023", "admin-x", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]          // blank
    [InlineData("ab")]        // too short
    [InlineData("bad space")] // space disallowed
    [InlineData("a!b@c")]     // special chars
    [InlineData(null)]        // null
    public async Task Invalid_ExamCode_Returns_400(string? examCode)
    {
        var result = await BagrutIngestHandler.HandleAsync(
            examCode, "application/pdf", PdfBytes(), null, "admin", _service);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains("invalid_exam_code", BodyOf(result));
    }

    [Fact]
    public async Task Missing_File_Returns_400()
    {
        var result = await BagrutIngestHandler.HandleAsync(
            "math-5u-2023", "application/pdf", fileBytes: null,
            uploadedBy: null, fallbackUserId: "admin", service: _service);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains("missing_file", BodyOf(result));
    }

    [Fact]
    public async Task Empty_File_Returns_400()
    {
        var result = await BagrutIngestHandler.HandleAsync(
            "math-5u-2023", "application/pdf", Array.Empty<byte>(),
            null, "admin", _service);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains("missing_file", BodyOf(result));
    }

    [Fact]
    public async Task Non_Pdf_Content_Type_Returns_400()
    {
        var result = await BagrutIngestHandler.HandleAsync(
            "math-5u-2023", "image/png", PdfBytes(),
            null, "admin", _service);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains("invalid_content_type", BodyOf(result));
    }

    [Fact]
    public async Task OcrCircuitOpen_Returns_503()
    {
        _service.IngestAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new OcrCircuitOpenException());

        var result = await BagrutIngestHandler.HandleAsync(
            "math-5u-2023", "application/pdf", PdfBytes(),
            null, "admin", _service);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, StatusOf(result));
        Assert.Contains("ocr_circuit_open", BodyOf(result));
    }

    [Fact]
    public async Task OcrInputException_Returns_400()
    {
        _service.IngestAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new OcrInputException("malformed PDF"));

        var result = await BagrutIngestHandler.HandleAsync(
            "math-5u-2023", "application/pdf", PdfBytes(),
            null, "admin", _service);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains("ocr_input_error", BodyOf(result));
    }

    [Fact]
    public async Task Service_ArgumentException_Returns_400()
    {
        _service.IngestAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new ArgumentException("PDF bytes are empty"));

        var result = await BagrutIngestHandler.HandleAsync(
            "math-5u-2023", "application/pdf", PdfBytes(),
            null, "admin", _service);

        Assert.Equal(StatusCodes.Status400BadRequest, StatusOf(result));
        Assert.Contains("invalid_request", BodyOf(result));
    }

    [Fact]
    public async Task Corpus_Service_Receives_Items_When_Metadata_Provided()
    {
        // prr-242: when subject + paper codes are present on the form, the
        // handler calls corpus.UpsertManyAsync with extractor-produced items.
        var drafts = new[]
        {
            new IngestionDraftQuestion("d-1", 1, "פתרו את x + 1 = 0", null,
                Array.Empty<string>(), null, "bagrut-math-5u",
                null, 0.9, Array.Empty<string>()),
        };
        _service.IngestAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PdfIngestionResult("pdf-xyz", "bagrut-math-5u", 1, 1, 0,
                drafts, Array.Empty<string>()));

        var corpus = Substitute.For<IBagrutCorpusService>();

        var result = await BagrutIngestHandler.HandleAsync(
            examCode: "bagrut-math-5u",
            fileContentType: "application/pdf",
            fileBytes: PdfBytes(),
            uploadedBy: "admin-1",
            fallbackUserId: null,
            service: _service,
            corpusService: corpus,
            ministrySubjectCode: "035",
            ministryQuestionPaperCode: "035581",
            units: 5,
            year: 2024,
            topicId: "algebra.quadratics",
            sourceFilename: "bagrut-math-5u-2024-summer-moedA.pdf");

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        await corpus.Received(1).UpsertManyAsync(
            Arg.Is<IReadOnlyList<Cena.Infrastructure.Documents.BagrutCorpusItemDocument>>(
                items => items.Count == 1
                         && items[0].MinistrySubjectCode == "035"
                         && items[0].MinistryQuestionPaperCode == "035581"
                         && items[0].Year == 2024
                         && items[0].TopicId == "algebra.quadratics"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Corpus_Service_Skipped_When_Metadata_Missing()
    {
        // Missing ministrySubjectCode → the handler skips the corpus write.
        _service.IngestAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PdfIngestionResult("pdf-xyz", "bagrut-math-5u", 1, 1, 0,
                new[] { new IngestionDraftQuestion("d-1", 1, "x", null,
                    Array.Empty<string>(), null, "bagrut-math-5u", null, 0.9,
                    Array.Empty<string>()) },
                Array.Empty<string>()));

        var corpus = Substitute.For<IBagrutCorpusService>();

        var result = await BagrutIngestHandler.HandleAsync(
            examCode: "bagrut-math-5u",
            fileContentType: "application/pdf",
            fileBytes: PdfBytes(),
            uploadedBy: "admin-1",
            fallbackUserId: null,
            service: _service,
            corpusService: corpus,
            ministrySubjectCode: null,
            ministryQuestionPaperCode: null);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
        await corpus.DidNotReceive().UpsertManyAsync(
            Arg.Any<IReadOnlyList<Cena.Infrastructure.Documents.BagrutCorpusItemDocument>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fallback_User_Id_Used_When_UploadedBy_Blank()
    {
        _service.IngestAsync(Arg.Any<byte[]>(), Arg.Any<string>(), "admin-sub",
                Arg.Any<CancellationToken>())
            .Returns(new PdfIngestionResult("pdf-x", "math-5u-2023", 0, 0, 0,
                Array.Empty<IngestionDraftQuestion>(), Array.Empty<string>()));

        var result = await BagrutIngestHandler.HandleAsync(
            examCode: "math-5u-2023",
            fileContentType: "application/pdf",
            fileBytes: PdfBytes(),
            uploadedBy: "   ",           // whitespace only → falls back
            fallbackUserId: "admin-sub",
            service: _service);

        Assert.Equal(StatusCodes.Status200OK, StatusOf(result));
    }
}
