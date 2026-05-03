// =============================================================================
// Cena Platform — PdfTriage tests
//
// Exercises the C# port against synthetic PDFs built in-line with PdfPig's
// writer. Verifies the five triage categories classify correctly + invariant
// checks (pages, chars, image counts, ratios).
// =============================================================================

using System.Text;
using Cena.Infrastructure.Ocr.Contracts;
using Cena.Infrastructure.Ocr.PdfTriage;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Cena.Infrastructure.Tests.Ocr;

public class PdfTriageTests
{
    private readonly IPdfTriage _triage = new Cena.Infrastructure.Ocr.PdfTriage.PdfTriage();

    [Fact]
    public void Empty_Bytes_Returns_Unreadable()
    {
        var result = _triage.Classify(ReadOnlyMemory<byte>.Empty);
        Assert.Equal(PdfTriageVerdict.Unreadable, result.Verdict);
        Assert.Contains("empty_bytes", result.Reasons);
    }

    [Fact]
    public void Garbage_Bytes_Returns_Unreadable()
    {
        var bytes = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 };
        var result = _triage.Classify(bytes);
        Assert.Equal(PdfTriageVerdict.Unreadable, result.Verdict);
    }

    [Fact]
    public void Clean_Text_Pdf_Classifies_As_Text()
    {
        // Must contain enough common English/Hebrew tokens to pass the
        // gibberish heuristic (≥2 hits in the dictionary).
        var pdf = BuildTextPdf(
            "Problem 1: Solve the equation. Find and compute the given values.");

        var result = _triage.Classify(pdf);
        Assert.Equal(PdfTriageVerdict.Text, result.Verdict);
        Assert.True(result.TextChars >= 50);
        Assert.Equal(1, result.Pages);
        Assert.Equal(0, result.ImageCount);
    }

    [Fact]
    public void Text_Without_Common_Tokens_Classifies_As_ScannedBadOcr()
    {
        // Has enough chars to pass the minTextChars threshold, but no
        // recognisable common tokens from either language's dictionary.
        // Triggers the "common_tokens=false" branch of the classifier.
        var pdf = BuildTextPdf(
            "xvqzjk qzhxw yxzqwv vqzxjk wqvjzx wqjxzv qxzwjv xvqjwz xzwqjv qzxwvj " +
            "kjqxvz wvzxjq zjxwqv xjwqzv qzwvxj wjxzqv zqjxvw vwxzjq xzqwvj wjqvzx");

        var result = _triage.Classify(pdf);
        Assert.Equal(PdfTriageVerdict.ScannedBadOcr, result.Verdict);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    public void Below_Threshold_Returns_ImageOnly(string tinyText)
    {
        var pdf = BuildTextPdf(tinyText);
        var result = _triage.Classify(pdf);
        Assert.Equal(PdfTriageVerdict.ImageOnly, result.Verdict);
    }

    [Fact]
    public void Classify_Is_Idempotent_Same_Input_Same_Output()
    {
        var pdf = BuildTextPdf("Problem 1: Solve the equation. Compute the answer.");

        var first = _triage.Classify(pdf);
        var second = _triage.Classify(pdf);

        Assert.Equal(first.Verdict, second.Verdict);
        Assert.Equal(first.TextChars, second.TextChars);
        Assert.Equal(first.HebrewRatio, second.HebrewRatio);
    }

    // -------------------------------------------------------------------------
    // Helpers — build tiny in-memory PDFs with PdfPig's writer
    // -------------------------------------------------------------------------
    private static byte[] BuildTextPdf(string text)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(595, 842);
        if (!string.IsNullOrEmpty(text))
        {
            var font = builder.AddStandard14Font(Standard14Font.Helvetica);
            page.AddText(text, 12, new PdfPoint(50, 780), font);
        }
        return builder.Build();
    }

    // The encrypted-PDF path is covered end-to-end by the integration-test
    // suite that runs against the committed dev-fixture pdf-encrypted-422.json
    // (OcrCascadeResult with pdf_triage == "encrypted"). Synthesizing an
    // encrypted PDF via PdfPig 0.1.9's writer is not part of the public API,
    // so we validate the path via fixture consumption rather than in-test
    // construction.
}
