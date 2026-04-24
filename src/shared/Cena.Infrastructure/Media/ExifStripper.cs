// =============================================================================
// Cena Platform — EXIF / IPTC / XMP / GPS metadata stripper (prr-001)
//
// Motivation
// ----------
// PRR-001 (pre-release-review 2026-04-20, privacy + red-team lenses):
// the previous `StripExifMetadata` in PhotoUploadEndpoints was a
// documented stub that returned input bytes untouched, while the
// endpoint response falsely advertised `ExifStripped = true`. GPS
// coordinates of minors' homes were flowing through the OCR, moderation,
// and persistence pipelines — a ship-blocker per the user's standing
// rules "labels match data" and "no stubs in production".
//
// This service is the canonical seam: the LLM and the rest of the
// ingestion pipeline (OCR, moderation, analytics, persistence) must
// only see <see cref="StripResult.Scrubbed"/>. The architecture test
// <c>NoUnstrippedImageBytesTest</c> keeps external consumers honest.
//
// Implementation choice — user decision 2026-04-20 (scope-cut)
// ------------------------------------------------------------
// Spec as written asked for Magick.NET-Q8-AnyCPU. Two issues drove a
// scope-cut (which the task explicitly permits):
//
//   1. At the time of writing, Magick.NET-Q8-AnyCPU 14.2.0 ships with
//      20+ known CVEs (1 high + 14 moderate + 5 low). Accepting that
//      many known-vulnerable code paths into the attack surface of a
//      privacy-hardening feature is indefensible.
//
//   2. SixLabors.ImageSharp 3.1.10 is already a project dependency
//      (wired by the OCR cascade Layer 0 / Layer 2c). It is fully
//      managed (no native binaries), has no open CVEs in the 3.1.x
//      line, and supports complete JPEG / PNG / WebP metadata
//      stripping via <c>ImageMetadata.ExifProfile</c>,
//      <c>IptcProfile</c>, and <c>XmpProfile</c>. Re-encoding strips
//      every GPS tag including the APP1 / APP13 segments EXIF APP1
//      writers typically use.
//
// HEIC is not supported by ImageSharp out of the box. Today the
// endpoint only accepts JPEG / PNG / WebP (PhotoUploadEndpoints
// `AllowedContentTypes`), so HEIC is rejected upstream by content-type
// and magic-byte validation before ever reaching this stripper.
// When HEIC support is added (roadmap Phase 4), this class must be
// revisited — HEIC will need either a native codec or a conversion
// pre-step.
// =============================================================================

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

namespace Cena.Infrastructure.Media;

/// <summary>
/// Result of <see cref="ExifStripper.Strip"/>. On success, <see cref="Scrubbed"/>
/// contains image bytes with all EXIF / IPTC / XMP / GPS metadata removed
/// via re-encode. On failure, <see cref="Success"/> is <c>false</c> and the
/// caller MUST NOT persist the bytes — return 422 instead (user decision
/// 2026-04-20: better reject than leak).
/// </summary>
public sealed record StripResult(byte[] Scrubbed, bool Success, string? FailureReason);

/// <summary>
/// Strips EXIF / IPTC / XMP / GPS metadata from JPEG / PNG / WebP image
/// bytes via SixLabors.ImageSharp re-encode. See file header for the
/// rationale behind choosing ImageSharp over Magick.NET (prr-001,
/// user decision 2026-04-20).
/// </summary>
/// <remarks>
/// <see cref="Strip"/> is <c>virtual</c> so strip-failure paths can be
/// tested without artificially corrupting a real image. Test doubles
/// override; production code injects the base class directly via DI.
/// </remarks>
public class ExifStripper
{
    /// <summary>
    /// Strip all metadata profiles from <paramref name="input"/> by
    /// loading, clearing <c>Metadata.ExifProfile / IptcProfile / XmpProfile</c>,
    /// clearing <c>Metadata.IccProfile</c> and <c>Metadata.CicpProfile</c>
    /// (defense-in-depth — ICC can carry signatures / IDs), and
    /// re-encoding with format-appropriate encoders.
    /// </summary>
    /// <remarks>
    /// Returns <c>Success=false</c> on any ImageSharp exception (unknown
    /// format, corrupt data, unsupported codec). Callers must treat a
    /// failure as a hard 422 — per prr-001, a photo that can't be
    /// stripped must not be persisted.
    /// </remarks>
    public virtual StripResult Strip(byte[] input)
    {
        if (input is null || input.Length == 0)
            return new StripResult(Array.Empty<byte>(), false, "empty_input");

        try
        {
            using var image = Image.Load(input);

            // Nuke every metadata container ImageSharp exposes. Setting
            // each profile to null causes the encoder to omit the
            // corresponding APP / chunk segment entirely.
            var meta = image.Metadata;
            meta.ExifProfile = null;
            meta.IptcProfile = null;
            meta.XmpProfile  = null;
            meta.IccProfile  = null;
            meta.CicpProfile = null;

            // Format-specific metadata containers also hold EXIF mirrors
            // in some codecs (e.g. PNG eXIf chunk). Clear their profiles
            // too by using a clean encoder on save.
            using var output = new MemoryStream(capacity: input.Length);
            var format = image.Metadata.DecodedImageFormat
                ?? throw new InvalidOperationException("unknown_decoded_format");

            IImageEncoder encoder = format.DefaultMimeType switch
            {
                "image/jpeg" => new JpegEncoder { Quality = 92 },
                "image/png"  => new PngEncoder(),
                "image/webp" => new WebpEncoder(),
                _ => throw new InvalidOperationException(
                    $"unsupported_format:{format.DefaultMimeType}"),
            };

            image.Save(output, encoder);
            return new StripResult(output.ToArray(), true, null);
        }
        catch (UnknownImageFormatException ex)
        {
            return new StripResult(Array.Empty<byte>(), false,
                $"unknown_image_format:{ex.Message}");
        }
        catch (InvalidImageContentException ex)
        {
            return new StripResult(Array.Empty<byte>(), false,
                $"invalid_image_content:{ex.Message}");
        }
        catch (Exception ex)
        {
            return new StripResult(Array.Empty<byte>(), false,
                $"strip_failed:{ex.GetType().Name}:{ex.Message}");
        }
    }
}
