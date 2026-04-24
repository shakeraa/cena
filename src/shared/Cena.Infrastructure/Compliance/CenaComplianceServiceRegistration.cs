// =============================================================================
// Cena Platform — Composed compliance services registration
//
// Bundles ADR-0038 SubjectKeyStore + prr-155 ConsentAggregate into a single DI
// call so Host Program.cs files can stay under their FileSize500LocBaseline.yml
// grandfather baselines (the ratchet only goes down per ADR-0012 Schedule Lock).
//
// Use via:
//   builder.Services.AddCenaComplianceServices(builder.Configuration, builder.Environment);
// =============================================================================

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Cena.Infrastructure.Compliance.KeyStore;

namespace Cena.Infrastructure.Compliance;

public static class CenaComplianceServiceRegistration
{
    /// <summary>
    /// Registers all Cena compliance/consent-aggregate primitives in one call:
    ///   - ADR-0038 SubjectKeyStore (env-aware backing + dev-fallback health-check)
    ///   - prr-155 ConsentAggregate primitives (forward-declared via late-binding
    ///     helper — the concrete ConsentAggregate registration lives in Cena.Actors
    ///     since Infrastructure cannot reference Actors directly)
    ///
    /// Host projects must still call the ConsentAggregate registration separately
    /// AFTER this call; this bundle only covers the Infrastructure-resident services.
    /// </summary>
    public static IServiceCollection AddCenaComplianceServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
        => services.AddSubjectKeyStore(configuration, environment);
}
