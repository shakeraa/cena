// =============================================================================
// Cena Platform — Figure storage configuration (ADR-0033 Layer 2c)
//
// Bind from "Ocr:FigureStorage":
//   {
//     "OutputDirectory": "/var/cena/ocr-figures",
//     "MaxFigureBytes": 2000000
//   }
//
// OutputDirectory defaults to {TempPath}/cena-ocr-figures so local dev works
// without config. Hosts with blob storage (S3/GCS) should implement a
// replacement Layer2c that writes there directly.
// =============================================================================

namespace Cena.Infrastructure.Ocr.Layers;

public sealed class FigureStorageOptions
{
    public string OutputDirectory { get; init; } =
        Path.Combine(Path.GetTempPath(), "cena-ocr-figures");

    /// <summary>Drop figures larger than this many bytes (misclassified regions).</summary>
    public int MaxFigureBytes { get; init; } = 2_000_000;
}
