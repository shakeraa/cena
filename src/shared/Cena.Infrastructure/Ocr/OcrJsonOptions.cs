// =============================================================================
// Cena Platform — OCR Cascade JSON Options
//
// Centralises the JsonSerializerOptions used across the Ocr namespace so
// field names round-trip correctly with the Python reference impl and the
// committed dev-fixtures. Every caller that (de)serialises OcrCascadeResult
// MUST use Default — do not instantiate JsonSerializerOptions ad-hoc.
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cena.Infrastructure.Ocr;

public static class OcrJsonOptions
{
    /// <summary>
    /// Matches the shape produced by scripts/ocr-spike/pipeline_prototype.py.
    /// snake_case at the top level; explicit [JsonPropertyName] on fields that
    /// need stronger contracts (most of them).
    /// </summary>
    public static readonly JsonSerializerOptions Default = Build();

    private static JsonSerializerOptions Build()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = false,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
        return opts;
    }
}
