// =============================================================================
// Cena Platform — Gemini Vision runner configuration (ADR-0033 Layer 4b)
//
// Bind from "Ocr:Gemini":
//   {
//     "BaseUrl": "https://generativelanguage.googleapis.com/v1beta/",
//     "Model":   "gemini-1.5-flash",
//     "ApiKey":  "<from vault>",
//     "RequestTimeout": "00:00:10",
//     "MaxImageBytes": 8000000
//   }
//
// API key never committed — bind from AWS Secrets Manager / K8s secret.
// =============================================================================

namespace Cena.Infrastructure.Ocr.Runners;

public sealed class GeminiVisionOptions
{
    public string BaseUrl { get; init; } = "https://generativelanguage.googleapis.com/v1beta/";
    public string Model { get; init; } = "gemini-1.5-flash";
    public string? ApiKey { get; init; }
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public int MaxImageBytes { get; init; } = 8_000_000;

    /// <summary>
    /// System-level instruction handed to Gemini for every rescue call.
    /// Keeps the prompt deterministic + concise so fallback results are
    /// auditable.
    /// </summary>
    public string SystemPrompt { get; init; } =
        "You are an OCR engine. Extract the exact text visible in the image. " +
        "Preserve language (Hebrew / English / Arabic). Preserve mathematical " +
        "notation as LaTeX inline when it appears. Output only the extracted " +
        "text — no commentary, no markdown, no leading labels.";
}
