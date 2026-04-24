// =============================================================================
// Cena Platform — OCR Figure Reference
//
// Cropped bounding box of a figure/table/plot extracted by Layer 2c. The
// cropped image is written to a storage path; this record carries the
// reference, not the bytes.
// =============================================================================

using System.Text.Json.Serialization;

namespace Cena.Infrastructure.Ocr.Contracts;

public sealed record OcrFigureRef(
    [property: JsonPropertyName("bbox")]         BoundingBox Bbox,
    [property: JsonPropertyName("kind")]         string Kind,            // "figure" | "diagram" | "table" | "plot"
    [property: JsonPropertyName("cropped_path")] string? CroppedPath,
    [property: JsonPropertyName("caption")]      string? Caption
);
