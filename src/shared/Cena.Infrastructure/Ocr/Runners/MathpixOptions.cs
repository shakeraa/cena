// =============================================================================
// Cena Platform — Mathpix runner configuration (ADR-0033 Layer 4a)
//
// Bind from "Ocr:Mathpix":
//   {
//     "BaseUrl": "https://api.mathpix.com/v3/",
//     "AppId":   "<from vault>",
//     "AppKey":  "<from vault>",
//     "RequestTimeout": "00:00:08",
//     "MaxImageBytes": 8000000
//   }
//
// Credentials MUST come from the secure config source (AWS Secrets Manager,
// K8s secret, etc.) — never committed, never in appsettings.json. The
// runner refuses to start without both AppId and AppKey set.
// =============================================================================

namespace Cena.Infrastructure.Ocr.Runners;

public sealed class MathpixOptions
{
    public string BaseUrl { get; init; } = "https://api.mathpix.com/v3/";
    public string? AppId { get; init; }
    public string? AppKey { get; init; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(8);
    /// <summary>
    /// Mathpix rejects images larger than ~10 MB. Crop larger inputs
    /// before sending. Default 8 MB leaves headroom.
    /// </summary>
    public int MaxImageBytes { get; init; } = 8_000_000;
}
