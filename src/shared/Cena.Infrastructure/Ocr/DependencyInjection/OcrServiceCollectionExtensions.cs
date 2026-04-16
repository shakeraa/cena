// =============================================================================
// Cena Platform — OCR Cascade DI registration
//
// Single entry point for wiring the cascade into a DI container:
//
//     services.AddOcrCascadeCore(configuration);
//
// Registers everything that's pure C# (no native/model deps):
//   - PDF triage (Layer 0 prerequisite)
//   - Layers 3, 4, 5 concrete implementations
//   - ConfidenceGateOptions bound from "Ocr:ConfidenceGate"
//   - NullLatexValidator as the fail-closed default for ILatexValidator
//   - OcrCascadeService orchestrator
//
// What's NOT registered — callers must supply these themselves once the
// concrete wrappers land:
//   - ILayer0Preprocess (OpenCVSharp concrete, RDY-OCR-PORT follow-up)
//   - ILayer1Layout (SuryaSidecarClient, RDY-OCR-PORT follow-up)
//   - ILayer2aTextOcr (TesseractLocalRunner, RDY-OCR-PORT follow-up)
//   - ILayer2bMathOcr (Pix2TexSidecarClient, RDY-OCR-PORT follow-up)
//   - ILayer2cFigureExtraction (OpenCV-based, RDY-OCR-PORT follow-up)
//   - IMathpixRunner / IGeminiVisionRunner (RDY-012 follow-up — optional)
//   - ILatexValidator real impl (wraps CasRouterService, ADR-0002)
// =============================================================================

using Cena.Infrastructure.Ocr.Cas;
using Cena.Infrastructure.Ocr.Layers;
using Cena.Infrastructure.Ocr.PdfTriage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Infrastructure.Ocr.DependencyInjection;

public static class OcrServiceCollectionExtensions
{
    /// <summary>
    /// Registers the pure-C# parts of the OCR cascade. Callers must separately
    /// register concrete implementations of ILayer0 through ILayer2c (and
    /// optionally IMathpixRunner / IGeminiVisionRunner / a real
    /// ILatexValidator). See class comment for the full wiring checklist.
    /// </summary>
    public static IServiceCollection AddOcrCascadeCore(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        // PDF triage — pure C#, no deps
        services.TryAddSingleton<IPdfTriage, Cena.Infrastructure.Ocr.PdfTriage.PdfTriage>();

        // The brain — Layers 3, 4, 5
        services.TryAddSingleton<ILayer3Reassemble, Layer3Reassemble>();
        services.TryAddSingleton<ILayer4ConfidenceGate, Layer4ConfidenceGate>();
        services.TryAddSingleton<ILayer5CasValidation, Layer5CasValidation>();

        // Options — bind if configuration supplied, otherwise defaults
        var options = new ConfidenceGateOptions();
        configuration?.GetSection("Ocr:ConfidenceGate").Bind(options);
        services.TryAddSingleton(options);

        // Fail-closed default for ILatexValidator. Production hosts MUST
        // replace this with a CAS-backed impl (CasRouterLatexValidator).
        services.TryAddSingleton<ILatexValidator, NullLatexValidator>();

        // The orchestrator — scoped because real Layer0/2 impls may carry
        // per-request state (cancellation tokens, tenant tags, etc.).
        services.TryAddScoped<IOcrCascadeService, OcrCascadeService>();

        // TimeProvider — shared singleton, injected into the orchestrator for
        // deterministic `captured_at` timestamps in tests.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
