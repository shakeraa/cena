// =============================================================================
// Cena Platform — CAS-gated persister DI registration (PRR-252)
//
// Mirrors the canonical admin-side chain in CenaAdminServiceRegistration.cs:78-88.
// Extracted from Program.cs so the 500-LOC ratchet (ADR-0012) stays satisfied
// and the registration is reusable from any future host that needs the
// single-writer ADR-0002 enforcement path.
//
// Usage in Program.cs:
//   builder.Services.AddCenaCasPersistence();
//
// Prerequisites in DI (registered elsewhere by the host):
//   - ICasRouterService (NATS / SymPy sidecar wiring)
//   - IDocumentStore (Marten)
//   - IConfiguration (gate-mode lookups)
// =============================================================================

using Cena.Actors.Cas;
using Microsoft.Extensions.DependencyInjection;

namespace Cena.Student.Api.Host.Cas;

internal static class CasPersistenceServiceRegistration
{
    /// <summary>
    /// PRR-252 — student-side registration of the CAS-gated persister chain.
    /// Variant-generation endpoints (PRR-245) route every author-owned variant
    /// through this single-writer ADR-0002 enforcement path.
    /// </summary>
    /// <remarks>
    /// Endpoint-layer auth (ResourceOwnershipGuard.VerifyStudentAccess) is the
    /// caller's responsibility — the persister itself is identity-agnostic
    /// (same pattern as the admin-side flow).
    /// </remarks>
    public static IServiceCollection AddCenaCasPersistence(this IServiceCollection services)
    {
        services.AddSingleton<IMathContentDetector, MathContentDetector>();
        services.AddSingleton<IStemSolutionExtractor, StemSolutionExtractor>();
        services.AddSingleton<ICasGateModeProvider, CasGateModeProvider>();
        services.AddScoped<ICasVerificationGate, CasVerificationGate>();
        services.AddScoped<ICasGatedQuestionPersister, CasGatedQuestionPersister>();
        return services;
    }
}
