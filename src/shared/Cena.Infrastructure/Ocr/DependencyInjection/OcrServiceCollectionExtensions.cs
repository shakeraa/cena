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
using Cena.Infrastructure.Ocr.Observability;
using Cena.Infrastructure.Ocr.PdfTriage;
using Cena.Infrastructure.Ocr.Runners;
using Cena.Infrastructure.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cena.Infrastructure.Ocr.DependencyInjection;

public static class OcrServiceCollectionExtensions
{
    public static IServiceCollection AddOcrCascadeCore(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        services.TryAddSingleton<IPdfTriage, Cena.Infrastructure.Ocr.PdfTriage.PdfTriage>();

        // Real concrete layers — no stubs.
        services.TryAddSingleton<ILayer0Preprocess, Layer0Preprocess>();
        services.TryAddSingleton<ILayer2cFigureExtraction, Layer2cFigureExtraction>();
        services.TryAddSingleton<ILayer3Reassemble, Layer3Reassemble>();
        services.TryAddSingleton<ILayer4ConfidenceGate, Layer4ConfidenceGate>();
        services.TryAddSingleton<ILayer5CasValidation, Layer5CasValidation>();

        var gateOptions = new ConfidenceGateOptions();
        configuration?.GetSection("Ocr:ConfidenceGate").Bind(gateOptions);
        services.TryAddSingleton(gateOptions);

        var layer0Options = new Layer0PreprocessOptions();
        configuration?.GetSection("Ocr:Layer0").Bind(layer0Options);
        services.TryAddSingleton(layer0Options);

        var figureStorage = new FigureStorageOptions();
        configuration?.GetSection("Ocr:FigureStorage").Bind(figureStorage);
        services.TryAddSingleton(figureStorage);

        services.TryAddScoped<IOcrCascadeService, OcrCascadeService>();
        services.TryAddSingleton(TimeProvider.System);

        // RDY-OCR-OBSERVABILITY (Phase 4): OcrMetrics Meter registration.
        // Hosts must ALSO call .AddMeter("Cena.Infrastructure.Ocr") on their
        // OpenTelemetry MeterProvider so the counters+histograms are
        // exported. The Meter name is OcrMetrics.MeterName.
        services.TryAddSingleton<OcrMetrics>();

        return services;
    }

    /// <summary>
    /// Adds <see cref="AddOcrCascadeCore"/> plus the standard runner bundle:
    /// Tesseract (Layer 2a), Surya sidecar (Layer 1), pix2tex sidecar (Layer
    /// 2b), and conditionally Mathpix + Gemini Vision (Layer 4 fallbacks)
    /// when their credentials are present. Used by every host that runs the
    /// ADR-0033 cascade — keeps admin-api and actor-host on identical
    /// runner wiring instead of inline-duplicating the block.
    ///
    /// Caller still needs <c>AddOcrCascadeWithCasValidation()</c> from
    /// Cena.Actors.Cas to bind the Layer-5 ILatexValidator → CasRouter
    /// bridge. (We can't call it here without inverting the layer
    /// direction Infrastructure → Actors.)
    /// </summary>
    public static IServiceCollection AddOcrCascadeWithRunners(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOcrCascadeCore(configuration);

        // Layer 2a — Tesseract local (no sidecar dependency).
        services.Configure<TesseractOptions>(configuration.GetSection("Ocr:Tesseract"));
        services.TryAddSingleton<ILayer2aTextOcr>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<TesseractOptions>>().Value;
            var log  = sp.GetService<ILogger<TesseractLocalRunner>>();
            return new TesseractLocalRunner(opts, log);
        });

        // Layer 1 + 2b — Surya layout + pix2tex math, both via the OCR
        // sidecar (docker-compose.ocr-sidecar.yml; default
        // http://localhost:50051 — overrideable per host via Ocr:Sidecar).
        services.Configure<OcrSidecarOptions>(configuration.GetSection("Ocr:Sidecar"));
        services.TryAddSingleton<ILayer1Layout, SuryaSidecarClient>();
        services.TryAddSingleton<ILayer2bMathOcr, Pix2TexSidecarClient>();

        // Layer 4 fallbacks — opt-in by credentials. Resilience pipelines
        // mirror actor-host's names so any policy config carries over.
        var mathpixAppId = configuration["Ocr:Mathpix:AppId"];
        if (!string.IsNullOrWhiteSpace(mathpixAppId))
        {
            services.Configure<MathpixOptions>(configuration.GetSection("Ocr:Mathpix"));
            services.AddHttpClient<IMathpixRunner, MathpixRunner>()
                .AddCenaResilience("OcrMathpix");
        }

        var geminiApiKey = configuration["Ocr:Gemini:ApiKey"];
        if (!string.IsNullOrWhiteSpace(geminiApiKey))
        {
            services.Configure<GeminiVisionOptions>(configuration.GetSection("Ocr:Gemini"));
            services.AddHttpClient<IGeminiVisionRunner, GeminiVisionRunner>()
                .AddCenaResilience("OcrGeminiVision");
        }

        return services;
    }
}
