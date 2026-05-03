// =============================================================================
// Cena Platform — IOcrCascadeService (ADR-0033)
//
// The single entry point for both ingestion surfaces:
//   Surface A — PhotoCaptureEndpoints / PhotoUploadEndpoints (student)
//   Surface B — BagrutPdfIngestionService, IngestionPipelineService,
//               IngestionPipelineCloudDir (admin)
//
// Both surfaces call RecognizeAsync() with raw bytes and contextual hints.
// The cascade handles PDF triage, layout detection, per-region OCR, RTL
// reassembly, confidence-gated cloud fallback, and SymPy CAS validation
// (via the existing ADR-0002 CasRouter).
// =============================================================================

using Cena.Infrastructure.Ocr.Contracts;

namespace Cena.Infrastructure.Ocr;

public interface IOcrCascadeService
{
    /// <summary>
    /// Runs the full cascade (Layers 0–5) on a single file and returns the
    /// normalised structured result. Safe to call concurrently.
    /// </summary>
    /// <param name="bytes">Raw file bytes. Image (PNG/JPG) or PDF.</param>
    /// <param name="contentType">MIME type, e.g. "image/jpeg" or "application/pdf".</param>
    /// <param name="hints">Optional curator/client hints. Null = cascade infers.</param>
    /// <param name="surface">"A" for student-facing (latency-first), "B" for admin batch (throughput-first).</param>
    /// <param name="ct">Cancellation token — honoured at layer boundaries.</param>
    /// <returns>
    /// <see cref="OcrCascadeResult"/> with per-region extractions, confidence, CAS verdict, and timings.
    /// </returns>
    /// <exception cref="OcrCircuitOpenException">
    /// Thrown when both Mathpix and Gemini Vision circuit breakers (RDY-012)
    /// are open and Layer 4 cannot fallback. Caller decides whether to 503.
    /// </exception>
    /// <exception cref="OcrInputException">
    /// Thrown for malformed input (empty bytes, unsupported content type).
    /// Encrypted PDFs do NOT throw — they return a result with
    /// <c>PdfTriage = Encrypted</c> and <c>HumanReviewRequired = true</c>.
    /// </exception>
    Task<OcrCascadeResult> RecognizeAsync(
        ReadOnlyMemory<byte> bytes,
        string contentType,
        OcrContextHints? hints,
        CascadeSurface surface,
        CancellationToken ct);
}

/// <summary>
/// Distinguishes Surface A (student, latency-critical) from Surface B (admin,
/// throughput-critical). Affects τ enforcement, timeout budgets, and the
/// catastrophic-review rejection path (A returns 422, B flags for human review).
/// </summary>
public enum CascadeSurface
{
    StudentInteractive = 0,
    AdminBatch = 1,
}
