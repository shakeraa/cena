// =============================================================================
// Cena Platform — OCR Cascade Exceptions
//
// Thin hierarchy — one base exception + two specific subtypes. Callers
// discriminate on type to decide the HTTP response (422, 503, 500).
// =============================================================================

namespace Cena.Infrastructure.Ocr;

public abstract class OcrCascadeException : Exception
{
    protected OcrCascadeException(string message) : base(message) { }
    protected OcrCascadeException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Both Layer 4 cloud fallback providers (Mathpix + Gemini Vision) have their
/// RDY-012 circuit breakers open. Surface A should return 503; Surface B
/// should flag for human review and retry later.
/// </summary>
public sealed class OcrCircuitOpenException : OcrCascadeException
{
    public OcrCircuitOpenException() : base(
        "OCR cloud fallback providers are unavailable (circuit breakers open).") { }

    public OcrCircuitOpenException(string message) : base(message) { }
    public OcrCircuitOpenException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Input could not be processed — empty bytes, unsupported MIME type, or the
/// file could not be opened. Encrypted PDFs are NOT this exception — they
/// return a normal result with triage=encrypted + human_review_required.
/// </summary>
public sealed class OcrInputException : OcrCascadeException
{
    public OcrInputException(string message) : base(message) { }
    public OcrInputException(string message, Exception inner) : base(message, inner) { }
}
