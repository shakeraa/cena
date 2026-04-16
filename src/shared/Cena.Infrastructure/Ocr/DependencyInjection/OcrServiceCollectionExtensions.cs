// =============================================================================
// Cena Platform — OCR Cascade DI registration (Infrastructure side)
//
//     services.AddOcrCascadeCore(configuration);
//
// Registers only pure-C# pieces that live in Cena.Infrastructure:
//   - IPdfTriage → PdfTriage
//   - ILayer3Reassemble / ILayer4ConfidenceGate / ILayer5CasValidation (brain)
//   - ConfidenceGateOptions (bound from "Ocr:ConfidenceGate")
//   - IOcrCascadeService (orchestrator, scoped)
//   - TimeProvider.System
//
// Does NOT register any stub defaults. Callers must wire every remaining
// dependency with a REAL implementation — per the no-stubs/production-grade
// rule. The composition root throws at first resolution if anything is
// missing, which is the correct failure mode.
//
// What the caller MUST register on top of this:
//   - ILatexValidator         → CasRouterLatexValidator (Cena.Actors, see
//                                AddOcrCascadeWithCasValidation)
//   - ILayer0Preprocess       → Layer0Preprocess (Cena.Infrastructure)
//   - ILayer1Layout           → SuryaSidecarClient (Cena.Infrastructure)
//   - ILayer2aTextOcr         → TesseractLocalRunner (Cena.Infrastructure)
//   - ILayer2bMathOcr         → Pix2TexSidecarClient (Cena.Infrastructure)
//   - ILayer2cFigureExtraction → Layer2cFigureExtraction (Cena.Infrastructure)
//   - IMathpixRunner           → MathpixRunner (optional; only if API keys set)
//   - IGeminiVisionRunner      → GeminiVisionRunner (optional; only if key set)
// =============================================================================

using Cena.Infrastructure.Ocr.Layers;
using Cena.Infrastructure.Ocr.PdfTriage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Infrastructure.Ocr.DependencyInjection;

public static class OcrServiceCollectionExtensions
{
    public static IServiceCollection AddOcrCascadeCore(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        services.TryAddSingleton<IPdfTriage, Cena.Infrastructure.Ocr.PdfTriage.PdfTriage>();

        services.TryAddSingleton<ILayer3Reassemble, Layer3Reassemble>();
        services.TryAddSingleton<ILayer4ConfidenceGate, Layer4ConfidenceGate>();
        services.TryAddSingleton<ILayer5CasValidation, Layer5CasValidation>();

        var options = new ConfidenceGateOptions();
        configuration?.GetSection("Ocr:ConfidenceGate").Bind(options);
        services.TryAddSingleton(options);

        services.TryAddScoped<IOcrCascadeService, OcrCascadeService>();
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
