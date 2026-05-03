// =============================================================================
// Cena Platform — OCR Bounding Box
//
// Shape: { x, y, w, h, page }. Page is 1-indexed for humans (0 = unknown).
// Matches scripts/ocr-spike/runners/base.py BoundingBox.
// =============================================================================

using System.Text.Json.Serialization;

namespace Cena.Infrastructure.Ocr.Contracts;

public sealed record BoundingBox(
    [property: JsonPropertyName("x")]    double X,
    [property: JsonPropertyName("y")]    double Y,
    [property: JsonPropertyName("w")]    double W,
    [property: JsonPropertyName("h")]    double H,
    [property: JsonPropertyName("page")] int Page = 1
);
