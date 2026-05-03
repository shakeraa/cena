// =============================================================================
// Cena Platform — OCR sidecar (gRPC) configuration
//
// Both SuryaSidecarClient (Layer 1) and Pix2TexSidecarClient (Layer 2b) talk
// to the same Python sidecar (docker/ocr-sidecar). Shared options keep
// one source of truth.
//
// Bind from "Ocr:Sidecar":
//   {
//     "Address":        "http://ocr-sidecar:50051",
//     "RequestTimeout": "00:00:20",
//     "IncludeFigures": true,
//     "MaxRegionBytes": 8000000
//   }
// =============================================================================

namespace Cena.Infrastructure.Ocr.Runners;

public sealed class OcrSidecarOptions
{
    /// <summary>
    /// gRPC endpoint for the sidecar. Default matches the K8s service name
    /// convention from docker/ocr-sidecar/README.md.
    /// </summary>
    public string Address { get; init; } = "http://ocr-sidecar:50051";

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(20);

    public bool IncludeFigures { get; init; } = true;

    /// <summary>Defensive cap — refuse to send a region larger than this.</summary>
    public int MaxRegionBytes { get; init; } = 8_000_000;
}
