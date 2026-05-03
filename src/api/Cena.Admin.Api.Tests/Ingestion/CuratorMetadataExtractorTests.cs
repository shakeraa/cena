// =============================================================================
// Cena Platform -- CuratorMetadataExtractor tests (Phase 1C / RDY-019e-IMPL)
//
// The extractor has three real strategies; tests cover strategy 1 (filename)
// and strategy 2 (PDF metadata) end-to-end. Strategy 3 (one-page OCR
// preview) is covered indirectly by the service tests — the integration
// with the cascade is exercised there.
//
// NO STUBS. Real filename regex, real PdfPig metadata parsing.
// =============================================================================

using Cena.Admin.Api.Ingestion;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig.Writer;

namespace Cena.Admin.Api.Tests.Ingestion;

public sealed class CuratorMetadataExtractorTests
{
    private readonly CuratorMetadataExtractor _extractor = new(
        NullLogger<CuratorMetadataExtractor>.Instance);

    // --- Filename strategy --------------------------------------------------
    [Theory]
    [InlineData("bagrut_mathematics_2024_summer_hebrew_5units.pdf", "math", "he", "5u", "bagrut_reference")]
    [InlineData("bagrut_physics_2023_winter_4u.pdf",                "physics", null, "4u", "bagrut_reference")]
    [InlineData("math_3u_english_practice.pdf",                     "math", "en", "3u", null)]
    [InlineData("random.pdf",                                        null,   null, null, null)]
    [InlineData("",                                                  null,   null, null, null)]
    public async Task ExtractAsync_Filename_Strategy_Classifies_Tokens(
        string filename, string? subject, string? language, string? track, string? sourceType)
    {
        var result = await _extractor.ExtractAsync(filename, Array.Empty<byte>(), "application/pdf");

        if (subject is null && language is null && track is null && sourceType is null)
        {
            Assert.Null(result);
            return;
        }

        Assert.NotNull(result);
        Assert.Equal(subject,    result!.Extracted.Subject);
        Assert.Equal(language,   result.Extracted.Language);
        Assert.Equal(track,      result.Extracted.Track);
        Assert.Equal(sourceType, result.Extracted.SourceType);
        Assert.Equal("filename", result.ExtractionStrategy);
    }

    [Fact]
    public async Task ExtractAsync_Filename_Strategy_Records_Per_Field_Confidences()
    {
        var result = await _extractor.ExtractAsync(
            "bagrut_mathematics_5u_hebrew.pdf",
            Array.Empty<byte>(),
            "application/pdf");

        Assert.NotNull(result);
        Assert.Contains("Subject",    result!.FieldConfidences.Keys);
        Assert.Contains("Language",   result.FieldConfidences.Keys);
        Assert.Contains("Track",      result.FieldConfidences.Keys);
        Assert.Contains("SourceType", result.FieldConfidences.Keys);

        foreach (var (_, conf) in result.FieldConfidences)
            Assert.InRange(conf, 0.0, 1.0);
    }

    // --- Normalization ------------------------------------------------------
    [Theory]
    [InlineData("math",        "math")]
    [InlineData("Mathematics", "math")]
    [InlineData("CHEM",        "chemistry")]
    [InlineData("Chemistry",   "chemistry")]
    public void NormalizeSubject_Maps_Known_Aliases(string raw, string expected) =>
        Assert.Equal(expected, CuratorMetadataExtractor.NormalizeSubject(raw));

    [Theory]
    [InlineData("he",      "he")]
    [InlineData("Hebrew",  "he")]
    [InlineData("ar",      "ar")]
    [InlineData("arabic",  "ar")]
    [InlineData("English", "en")]
    public void NormalizeLanguage_Maps_Known_Aliases(string raw, string expected) =>
        Assert.Equal(expected, CuratorMetadataExtractor.NormalizeLanguage(raw));

    [Theory]
    [InlineData("3units", "3u")]
    [InlineData("4u",      "4u")]
    [InlineData("5 units", "5u")]
    [InlineData("5_U",     "5u")]
    public void NormalizeTrack_Compacts_Variants(string raw, string expected) =>
        Assert.Equal(expected, CuratorMetadataExtractor.NormalizeTrack(raw));

    [Theory]
    [InlineData("bagrut",       "bagrut_reference")]
    [InlineData("psychometric", "admin_upload")]
    [InlineData("SAT",          "admin_upload")]
    [InlineData("cloud",        "cloud_dir")]
    [InlineData("batch",        "admin_upload")]
    public void NormalizeSourceType_Maps_Known_Aliases(string raw, string expected) =>
        Assert.Equal(expected, CuratorMetadataExtractor.NormalizeSourceType(raw));

    // --- PDF metadata strategy ---------------------------------------------
    [Fact]
    public async Task ExtractAsync_PdfMetadata_Strategy_Reads_Embedded_Info()
    {
        var pdfBytes = BuildPdfWithMetadata(
            title:    "Bagrut 5 units math exam",
            author:   "Cena QA",
            keywords: "hebrew mathematics bagrut 5u",
            subject:  "Mathematics 5 units summer");

        // Filename is deliberately generic — the PDF metadata strategy must
        // carry the extraction on its own.
        var result = await _extractor.ExtractAsync(
            filename: "upload-xyz.pdf",
            fileBytes: pdfBytes,
            contentType: "application/pdf");

        Assert.NotNull(result);
        Assert.Equal("math", result!.Extracted.Subject);
        Assert.Equal("he",   result.Extracted.Language);
        Assert.Equal("5u",   result.Extracted.Track);
        Assert.Equal("bagrut_reference", result.Extracted.SourceType);
        // Strategy should be "pdf_metadata" since filename matched nothing.
        Assert.Equal("pdf_metadata", result.ExtractionStrategy);
    }

    [Fact]
    public async Task ExtractAsync_Combined_Strategy_When_Both_Sources_Contribute()
    {
        // Filename carries subject+track; PDF info carries language.
        var pdfBytes = BuildPdfWithMetadata(
            title:    "Generic exam",
            author:   "Cena QA",
            keywords: "arabic",
            subject:  "Arabic language exam");

        var result = await _extractor.ExtractAsync(
            filename: "bagrut_mathematics_4u.pdf",
            fileBytes: pdfBytes,
            contentType: "application/pdf");

        Assert.NotNull(result);
        Assert.Equal("math", result!.Extracted.Subject);
        Assert.Equal("ar",   result.Extracted.Language);
        Assert.Equal("4u",   result.Extracted.Track);
        Assert.Equal("combined", result.ExtractionStrategy);
    }

    [Fact]
    public async Task ExtractAsync_Non_Pdf_Content_Type_Skips_Pdf_Strategy()
    {
        var result = await _extractor.ExtractAsync(
            filename: "bagrut_math_5u.png",
            fileBytes: new byte[] { 0x89, 0x50, 0x4E, 0x47 },  // PNG magic
            contentType: "image/png");

        Assert.NotNull(result);
        Assert.Equal("filename", result!.ExtractionStrategy);
    }

    [Fact]
    public async Task ExtractAsync_Empty_Input_Returns_Null()
    {
        var result = await _extractor.ExtractAsync(
            filename: "",
            fileBytes: Array.Empty<byte>(),
            contentType: "application/pdf");

        Assert.Null(result);
    }

    // --- helpers ------------------------------------------------------------
    private static byte[] BuildPdfWithMetadata(
        string title, string author, string keywords, string subject)
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(595, 842);
        builder.DocumentInformation.Title    = title;
        builder.DocumentInformation.Author   = author;
        builder.DocumentInformation.Keywords = keywords;
        builder.DocumentInformation.Subject  = subject;
        return builder.Build();
    }
}
