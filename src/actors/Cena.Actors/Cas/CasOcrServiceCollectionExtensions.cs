// =============================================================================
// Cena Platform — CAS-OCR DI bridge (Actors side)
//
// Bridges Cena.Infrastructure.Ocr to Cena.Actors.Cas without putting the CAS
// dependency directly in Infrastructure (maintains layer direction
// Actors → Infrastructure, not the reverse).
//
//     services.AddOcrCascadeCore(cfg);                   // Infrastructure side
//     services.AddOcrCascadeWithCasValidation();         // this extension
//
// Registers the real CasRouterLatexValidator so Layer 5 of the OCR cascade
// rounds-trips every math block through the 3-tier CAS router (ADR-0002).
// =============================================================================

using Cena.Infrastructure.Ocr.Cas;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Actors.Cas;

public static class CasOcrServiceCollectionExtensions
{
    /// <summary>
    /// Wires ILatexValidator → CasRouterLatexValidator. Requires
    /// ICasRouterService to already be registered (standard CAS wiring —
    /// see Cena.Admin.Api composition root).
    /// </summary>
    public static IServiceCollection AddOcrCascadeWithCasValidation(
        this IServiceCollection services)
    {
        services.TryAddSingleton<ILatexValidator, CasRouterLatexValidator>();
        return services;
    }
}
