// =============================================================================
// Cena Platform — NotConfiguredOcrCascadeService
//
// Fail-loud sentinel registered as IOcrCascadeService on hosts that have NOT
// wired any real OCR runner (no Gemini key, no Mathpix key, no
// cena-ocr-sidecar). Constructor-injecting consumers (e.g.
// BagrutPdfIngestionService) get a working object at DI time, so they don't
// crash the process at startup. The first call surfaces a curator-readable
// 503 via the existing OcrCircuitOpenException → 503 mapping in
// BagrutIngestEndpoints.
//
// This is NOT a stub: it does not fabricate OCR output. It throws a
// structured exception with explicit remediation guidance.
//
// Replace by uncommenting AddOcrCascadeCore + AddOcrCascadeWithCasValidation
// in Program.cs and wiring at least one runner (Gemini key, Mathpix
// credentials, or the SuryaSidecarClient + Pix2TexSidecarClient pair backed
// by docker-compose.ocr-sidecar.yml).
// =============================================================================

using Cena.Infrastructure.Ocr;
using Cena.Infrastructure.Ocr.Contracts;

namespace Cena.Admin.Api.Ingestion;

public sealed class NotConfiguredOcrCascadeService : IOcrCascadeService
{
    private const string Message =
        "OCR runners are not configured for this host. " +
        "To enable: set Ocr:Gemini:ApiKey, set Ocr:Mathpix:AppId+AppKey, " +
        "or bring up docker-compose.ocr-sidecar.yml and uncomment " +
        "AddOcrCascadeCore in Cena.Admin.Api.Host/Program.cs.";

    public Task<OcrCascadeResult> RecognizeAsync(
        ReadOnlyMemory<byte> bytes,
        string contentType,
        OcrContextHints? hints,
        CascadeSurface surface,
        CancellationToken ct)
    {
        throw new OcrCircuitOpenException(Message);
    }
}
