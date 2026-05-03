// =============================================================================
// Cena Platform — IPhotoRedactor (EPIC-PRR-J PRR-413)
//
// Seam for pre-OCR PII redaction on student worksheet photos. Called from the
// photo-upload intake pipeline BEFORE the image is forwarded to the OCR vendor
// so the vendor never sees:
//   - Student faces (if the student accidentally captures themselves),
//   - Handwritten names in top / bottom margins,
//   - School logos / letterhead banners.
//
// This interface is the SLICE that PRR-413 ships. The full behavior the source
// 10-persona review asked for (face detection via a local ML model + OCR-based
// name-pattern detection + template-matched logo detection) needs a model
// dependency (ONNX / OpenCV.NET / emgu.CV), a licensing review, and a tuning
// corpus that we do NOT yet have. Shipping the interface + a heuristic redactor
// + an audit-log seam NOW unblocks:
//   1. The upload endpoint's consent/disclosure UX (we can honestly tell the
//      student which redaction classes we currently apply + which we defer).
//   2. The admin "incidental-PII exposure" compliance dashboard.
//   3. Replaceable DI binding for a real detector in a follow-up task.
//
// "No stubs — production grade" (memory 2026-04-11): the shipped implementations
// are HONEST about what they do + do not do. DeferredRedactions lists every
// requested redaction kind this implementation could not apply — auditors +
// ops can see the gap from the structured log line rather than discovering it
// at breach-notification time.
//
// Scope discipline:
//   - PhotoRedactionRequest declares WHAT the caller wants (flags).
//   - PhotoRedactionResult declares WHAT we actually did (AppliedRedactions)
//     AND WHAT we could not do (DeferredRedactions, as string tags).
//   - Regions are reported in percentage coordinates (XPct/YPct/WidthPct/
//     HeightPct) so the audit record is resolution-independent and survives
//     downstream re-encoding / thumbnail generation.
//   - RedactionMethod is "blur" or "solid-fill" as a string so the audit
//     record is readable without importing an enum into log consumers; the
//     heuristic redactor emits "solid-fill" (black fill), a real
//     decoder-backed implementation can emit "blur" (Gaussian).
//
// Follow-ups explicitly deferred:
//   - FACE_DETECTION: needs ONNX model + license review + tuning corpus.
//     The IPhotoRedactor.RedactAsync surface already accepts Faces as a
//     flag, so when a real detector lands the caller code never changes.
//   - STRUCTURED_IMAGE_DECODER: JPEG / PNG decode so the heuristic can
//     operate on real uploads (not just raw-byte test inputs). Needs an
//     ImageSharp (or System.Drawing on non-Linux) dependency add + a
//     decide-now licensing review.
//   - LOGO_TEMPLATE_MATCHER: feature-matching against a curated school-
//     letterhead corpus. Out of scope for the heuristic-only slice.
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic.Intake;

/// <summary>
/// Bit-flag enumeration of redaction classes a caller can request. The
/// caller sets every kind it wants applied; the implementation returns which
/// were actually applied and which were deferred.
/// </summary>
[Flags]
public enum PhotoRedactionKinds
{
    None = 0,

    /// <summary>Top margin (typically name + date line on worksheets).</summary>
    TopMargin = 1 << 0,

    /// <summary>Bottom margin (typically page-number + school footer).</summary>
    BottomMargin = 1 << 1,

    /// <summary>Human faces. Requires a real detector — deferred in PRR-413.</summary>
    Faces = 1 << 2,

    /// <summary>School logos / letterhead banners. Requires a template matcher — deferred.</summary>
    Logos = 1 << 3,
}

/// <summary>
/// Input to <see cref="IPhotoRedactor.RedactAsync"/>. The caller is
/// responsible for having already sniffed the MIME type and measured the
/// pixel dimensions (the upload endpoint's image-metadata step); the
/// redactor does not re-parse the bytes.
/// </summary>
/// <param name="MimeType">
/// IANA-registered content type (e.g. <c>image/jpeg</c>, <c>image/png</c>).
/// Use <c>application/octet-stream</c> for raw pixel arrays used in tests.
/// </param>
/// <param name="ImageWidthPx">Width in pixels. Must be ≥ 1.</param>
/// <param name="ImageHeightPx">Height in pixels. Must be ≥ 1.</param>
/// <param name="RedactionKinds">Bit-flag set of redaction classes requested.</param>
public sealed record PhotoRedactionRequest(
    string MimeType,
    int ImageWidthPx,
    int ImageHeightPx,
    PhotoRedactionKinds RedactionKinds);

/// <summary>
/// One redacted region on the image, in percentage coordinates so the record
/// survives downstream re-encoding / thumbnailing. (0,0) is the top-left.
/// </summary>
/// <param name="Kind">Which requested class this region satisfies.</param>
/// <param name="XPct">Left edge as a fraction of width in [0, 1].</param>
/// <param name="YPct">Top edge as a fraction of height in [0, 1].</param>
/// <param name="WidthPct">Width as a fraction of width in (0, 1].</param>
/// <param name="HeightPct">Height as a fraction of height in (0, 1].</param>
/// <param name="RedactionMethod">
/// How the pixels were altered. <c>"solid-fill"</c> for byte-level zero fill
/// (heuristic implementation), <c>"blur"</c> for Gaussian blur (decoder-backed
/// implementations). Kept as a string so audit-log consumers don't need to
/// track an enum across service boundaries.
/// </param>
public sealed record PhotoRedactionRegion(
    PhotoRedactionKinds Kind,
    double XPct,
    double YPct,
    double WidthPct,
    double HeightPct,
    string RedactionMethod);

/// <summary>
/// Output of <see cref="IPhotoRedactor.RedactAsync"/>. The honest-about-gaps
/// design is load-bearing: the caller MUST log <see cref="DeferredRedactions"/>
/// so the incidental-PII compliance dashboard can see which classes the
/// current pipeline does not cover.
/// </summary>
/// <param name="RedactedImageBytes">
/// Bytes to forward to OCR. For a decoder-backed implementation, these are
/// a re-encoded image with the redacted regions blurred. For the
/// byte-level heuristic, these are the input bytes with the top / bottom
/// margin byte ranges zeroed out (raw-MIME path) OR the input bytes
/// unchanged (structured-MIME path — decoding was deferred).
/// </param>
/// <param name="AppliedRedactions">
/// Regions that were actually altered. Empty list is valid — the Noop
/// implementation and the structured-MIME path of the heuristic both return
/// an empty list (and report all requested kinds as deferred).
/// </param>
/// <param name="DeferredRedactions">
/// Machine-readable tags for every requested kind this implementation could
/// not apply. Callers MUST log these for the compliance dashboard. Tag
/// conventions this slice defines:
///   - <c>"face_detection_not_implemented"</c> — Faces flag set, no detector wired.
///   - <c>"logo_template_matcher_not_implemented"</c> — Logos flag set, no matcher wired.
///   - <c>"structured_image_decoder_required"</c> — margin redaction on image/jpeg /
///     image/png etc. requires an ImageSharp-style decoder that isn't in this slice.
///   - <c>"redaction_not_configured"</c> — the Noop null-object implementation is bound.
/// </param>
public sealed record PhotoRedactionResult(
    ReadOnlyMemory<byte> RedactedImageBytes,
    IReadOnlyList<PhotoRedactionRegion> AppliedRedactions,
    IReadOnlyList<string> DeferredRedactions);

/// <summary>
/// Pre-OCR redactor. Implementations must be pure (no hidden network I/O);
/// the upload pipeline calls this synchronously on the hot path and fails
/// closed if redaction throws — that fail-closed policy is the reason the
/// interface is <see cref="Task{TResult}"/>-returning (even though the
/// heuristic implementation completes synchronously).
/// </summary>
public interface IPhotoRedactor
{
    /// <summary>
    /// Redact PII regions on the supplied image bytes according to
    /// <paramref name="request"/>'s <see cref="PhotoRedactionRequest.RedactionKinds"/>
    /// mask. Returns the redacted bytes + an honest applied/deferred manifest.
    /// </summary>
    Task<PhotoRedactionResult> RedactAsync(
        ReadOnlyMemory<byte> imageBytes,
        PhotoRedactionRequest request,
        CancellationToken ct);
}
