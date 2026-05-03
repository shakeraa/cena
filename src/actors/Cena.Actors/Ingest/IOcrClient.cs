// =============================================================================
// Cena Platform — OCR Client Abstraction
// Primary: Gemini 2.5 Flash | Fallback: Mathpix | Local dev: Surya
// =============================================================================

namespace Cena.Actors.Ingest;

/// <summary>
/// OCR service contract. Processes a PDF/image and returns structured text + LaTeX math.
/// </summary>
public interface IOcrClient
{
    /// <summary>Process a single page image and return structured OCR output.</summary>
    Task<OcrPageOutput> ProcessPageAsync(Stream imageStream, string contentType, CancellationToken ct = default);

    /// <summary>Process a multi-page PDF and return per-page OCR output.</summary>
    Task<OcrDocumentOutput> ProcessDocumentAsync(Stream pdfStream, CancellationToken ct = default);

    /// <summary>Name of this OCR provider for audit logging.</summary>
    string ProviderName { get; }
}

/// <summary>Specialized math OCR fallback for complex equations.</summary>
public interface IMathOcrClient
{
    /// <summary>Extract LaTeX from a cropped equation image.</summary>
    Task<string> ExtractLatexAsync(Stream imageStream, CancellationToken ct = default);

    string ProviderName { get; }
}

// ── Output Records ──

public sealed record OcrPageOutput(
    int PageNumber,
    string RawText,
    string DetectedLanguage,
    Dictionary<string, string> MathExpressions,
    float Confidence,
    List<OcrTextBlock> TextBlocks);

public sealed record OcrTextBlock(
    string Text,
    OcrBoundingBox? BoundingBox,
    float Confidence,
    bool IsMath);

public sealed record OcrBoundingBox(int X, int Y, int Width, int Height);

public sealed record OcrDocumentOutput(
    List<OcrPageOutput> Pages,
    string DetectedLanguage,
    float OverallConfidence,
    int PageCount,
    decimal EstimatedCostUsd);
