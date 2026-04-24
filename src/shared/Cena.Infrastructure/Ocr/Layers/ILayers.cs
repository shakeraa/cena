// =============================================================================
// Cena Platform — OCR Cascade Layer Interfaces (ADR-0033)
//
// One interface per layer as documented in
// scripts/ocr-spike/pipeline_prototype.py. Keeping layers as separate
// abstractions gives us three things:
//
//   1. Each layer is independently mockable → OcrCascadeService tests don't
//      need real OpenCV / gRPC / Tesseract.
//   2. Runner swaps (e.g. Tesseract → Surya) don't cascade into the
//      orchestrator — only the DI registration changes.
//   3. Kill-switches at every layer (ADR-0033 consequence): any layer can
//      return a degraded-mode result without breaking the next layer.
//
// Layers 2a/2b/2c run in parallel; the orchestrator awaits Task.WhenAll.
// =============================================================================

using Cena.Infrastructure.Ocr.Contracts;

namespace Cena.Infrastructure.Ocr.Layers;

// ── Layer 0 ────────────────────────────────────────────────────────────────
public interface ILayer0Preprocess
{
    /// <summary>Rasterise a PDF (if applicable) and apply deskew/binarize/denoise.</summary>
    Task<Layer0Output> RunAsync(
        ReadOnlyMemory<byte> bytes,
        string contentType,
        CancellationToken ct);
}

public sealed record Layer0Output(
    IReadOnlyList<byte[]> PreprocessedPageBytes,   // one PNG per page
    PdfTriageVerdict? Triage,                      // null for image inputs
    double LatencySeconds);

// ── Layer 1 ────────────────────────────────────────────────────────────────
public interface ILayer1Layout
{
    Task<Layer1Output> RunAsync(
        IReadOnlyList<byte[]> pageBytes,
        OcrContextHints? hints,
        CancellationToken ct);
}

public sealed record LayoutRegion(
    string Kind,                                    // "text" | "math" | "figure" | "table"
    BoundingBox Bbox);

public sealed record Layer1Output(
    IReadOnlyList<LayoutRegion> Regions,
    bool IsDegradedMode,                            // true if Surya unavailable
    double LatencySeconds);

// ── Layer 2a / 2b / 2c ─────────────────────────────────────────────────────
public interface ILayer2aTextOcr
{
    Task<Layer2aOutput> RunAsync(
        IReadOnlyList<byte[]> pageBytes,
        IReadOnlyList<LayoutRegion> textRegions,
        OcrContextHints? hints,
        CancellationToken ct);
}

public sealed record Layer2aOutput(
    IReadOnlyList<OcrTextBlock> TextBlocks,
    double LatencySeconds);

public interface ILayer2bMathOcr
{
    Task<Layer2bOutput> RunAsync(
        IReadOnlyList<byte[]> pageBytes,
        IReadOnlyList<LayoutRegion> mathRegions,
        CancellationToken ct);
}

public sealed record Layer2bOutput(
    IReadOnlyList<OcrMathBlock> MathBlocks,
    double LatencySeconds);

public interface ILayer2cFigureExtraction
{
    Task<Layer2cOutput> RunAsync(
        IReadOnlyList<byte[]> pageBytes,
        IReadOnlyList<LayoutRegion> figureRegions,
        CancellationToken ct);
}

public sealed record Layer2cOutput(
    IReadOnlyList<OcrFigureRef> Figures,
    double LatencySeconds);

// ── Layer 3 ────────────────────────────────────────────────────────────────
public interface ILayer3Reassemble
{
    /// <summary>RTL-aware reading-order stitch across pages.</summary>
    Layer3Output Run(
        IReadOnlyList<OcrTextBlock> textBlocks,
        IReadOnlyList<OcrMathBlock> mathBlocks,
        IReadOnlyList<OcrFigureRef> figures);
}

public sealed record Layer3Output(
    IReadOnlyList<OcrTextBlock> OrderedTextBlocks,
    IReadOnlyList<OcrMathBlock> OrderedMathBlocks,
    IReadOnlyList<OcrFigureRef> Figures,
    double LatencySeconds);

// ── Layer 4 ────────────────────────────────────────────────────────────────
public interface ILayer4ConfidenceGate
{
    /// <summary>
    /// Inspect per-region confidence. Blocks below τ are cropped from the
    /// corresponding page (<paramref name="pageBytes"/>) using the block's
    /// BoundingBox.Page + coordinates, then routed through the cloud fallback
    /// runners (Mathpix for math, Gemini for text). Catastrophic failure
    /// surfaces as human-review required + catastrophic reason.
    /// </summary>
    /// <param name="pageBytes">
    /// Preprocessed page images from Layer 0, 1-indexed by BoundingBox.Page.
    /// Empty list is valid (no rescue possible — runners are skipped).
    /// </param>
    Task<Layer4Output> RunAsync(
        IReadOnlyList<byte[]> pageBytes,
        IReadOnlyList<OcrTextBlock> textBlocks,
        IReadOnlyList<OcrMathBlock> mathBlocks,
        CascadeSurface surface,
        CancellationToken ct);
}

public sealed record Layer4Output(
    IReadOnlyList<OcrTextBlock> TextBlocks,        // may have rescued blocks
    IReadOnlyList<OcrMathBlock> MathBlocks,        // may have rescued blocks
    IReadOnlyList<string> FallbacksFired,
    double AverageConfidence,
    bool CatastrophicFailure,
    double LatencySeconds);

// ── Layer 5 ────────────────────────────────────────────────────────────────
public interface ILayer5CasValidation
{
    /// <summary>
    /// SymPy round-trip every math block (ADR-0002 oracle). Blocks that fail
    /// the CAS gate stay in the result but are flagged SympyParsed=false so
    /// downstream consumers can reject them.
    /// </summary>
    Task<Layer5Output> RunAsync(
        IReadOnlyList<OcrMathBlock> mathBlocks,
        CancellationToken ct);
}

public sealed record Layer5Output(
    IReadOnlyList<OcrMathBlock> MathBlocks,
    int Validated,
    int Failed,
    double LatencySeconds);
