// =============================================================================
// Cena Platform — Layer 0 preprocess configuration (ADR-0033)
//
// Bind from "Ocr:Layer0":
//   {
//     "PdftoppmBinaryPath": "pdftoppm",
//     "DpiForRasterization": 300,
//     "MaxLongEdgePixels":   2200,
//     "ConvertToGrayscale":  true,
//     "PerPageTimeout":      "00:00:30",
//     "PdfRenderTimeout":    "00:00:60"
//   }
//
// The defaults match the Python spike — do not tune without updating the
// regression fixture set.
// =============================================================================

namespace Cena.Infrastructure.Ocr.Layers;

public sealed class Layer0PreprocessOptions
{
    /// <summary>Poppler's `pdftoppm` — cross-platform PDF rasteriser.</summary>
    public string PdftoppmBinaryPath { get; init; } = "pdftoppm";

    /// <summary>DPI passed to pdftoppm. 300 matches the spike.</summary>
    public int DpiForRasterization { get; init; } = 300;

    /// <summary>
    /// Downsample any page whose long edge exceeds this threshold. Calibrated
    /// from the spike: pages >2200 px add 50 s to Layer 0 without improving
    /// downstream OCR accuracy.
    /// </summary>
    public int MaxLongEdgePixels { get; init; } = 2200;

    /// <summary>
    /// Grayscale-convert preprocessed pages. Tesseract + pix2tex perform
    /// slightly better on grayscale than RGB.
    /// </summary>
    public bool ConvertToGrayscale { get; init; } = true;

    /// <summary>Per-page preprocessing timeout (ImageSharp operations).</summary>
    public TimeSpan PerPageTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Whole-PDF rasterization timeout (pdftoppm).</summary>
    public TimeSpan PdfRenderTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Refuse PDFs larger than this many bytes (defensive — avoid memory
    /// blow-up from adversarial inputs).
    /// </summary>
    public int MaxPdfBytes { get; init; } = 50_000_000;
}
