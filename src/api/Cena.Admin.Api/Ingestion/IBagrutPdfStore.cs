// =============================================================================
// Cena Platform — IBagrutPdfStore
//
// Persistent storage for Bagrut source PDFs by content-hash id. Closes the
// gap surfaced 2026-05-01: BagrutPdfIngestionService.IngestAsync hashed
// pdfBytes to derive sourcePdfId then handed bytes to the OCR cascade and
// dropped them. Curators reviewing a draft on the InReview board could not
// look at the original document — only the OCR-derived text. Persistent
// storage + a retrieval endpoint let the SPA render the PDF side-by-side
// with the extracted form (browser-native <embed type=application/pdf>).
//
// Identity: pdfId is sha256(pdfBytes) lowercase hex. Same id the rest of
// the pipeline uses (BagrutPdfIngestionService.GeneratePdfId). Content-
// addressable — the same PDF uploaded twice resolves to the same blob.
//
// Backends: today there is one implementation, LocalFileSystemBagrutPdfStore,
// writing under a configured root directory (bind-mount /var/cena/source-pdfs
// in docker-compose). Production hosts can swap in an S3-backed
// implementation per the ADR-0058 photo-pipeline pattern; the interface
// is intentionally narrow so that swap is a single-class change.
//
// Backfill: items already on the InReview board predate this store and
// have no PDF bytes anywhere. ExistsAsync returns false for those; the
// retrieval endpoint surfaces a 404 the SPA renders as
// "PDF not retained — uploaded before storage was added; re-upload to
// see the original here."
// =============================================================================

namespace Cena.Admin.Api.Ingestion;

public interface IBagrutPdfStore
{
    /// <summary>
    /// Persist <paramref name="pdfBytes"/> under <paramref name="pdfId"/>
    /// (content-hash). Idempotent: the same bytes written twice is a
    /// no-op (atomic write avoids torn files on concurrent uploads).
    /// Throws on I/O errors so the caller can decide whether to abort
    /// ingestion or proceed without persistence.
    /// </summary>
    Task PersistAsync(string pdfId, byte[] pdfBytes, CancellationToken ct = default);

    /// <summary>
    /// Returns true if a blob exists for <paramref name="pdfId"/>.
    /// Cheap — does not read bytes. Used by the retrieval endpoint to
    /// 404 cleanly for backfilled items without dragging the bytes.
    /// </summary>
    Task<bool> ExistsAsync(string pdfId, CancellationToken ct = default);

    /// <summary>
    /// Stream PDF bytes for <paramref name="pdfId"/>. Returns null when
    /// not found (caller maps to 404). The returned stream is owned by
    /// the caller — must be disposed.
    /// </summary>
    Task<Stream?> OpenReadAsync(string pdfId, CancellationToken ct = default);
}
