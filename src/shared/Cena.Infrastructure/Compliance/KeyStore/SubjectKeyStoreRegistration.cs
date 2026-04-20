// =============================================================================
// Cena Platform -- Subject Key Store DI Registration (ADR-0038, prr-003b)
//
// Wires:
//   - SubjectKeyDerivation   (from CENA_PII_ROOT_KEY_BASE64 or dev fallback)
//   - ISubjectKeyStore       (InMemorySubjectKeyStore until Postgres backing)
//   - EncryptedFieldAccessor (facade used by write + read paths)
// =============================================================================

using Cena.Infrastructure.Compliance.KeyStore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Extension methods for registering the ADR-0038 crypto-shredding primitives.
/// </summary>
public static class SubjectKeyStoreRegistration
{
    /// <summary>
    /// Register the subject key store + encrypted field accessor. Safe to
    /// call multiple times; uses TryAddSingleton.
    /// </summary>
    public static IServiceCollection AddSubjectKeyStore(this IServiceCollection services)
    {
        services.TryAddSingleton(sp =>
        {
            var logger = sp.GetService<ILoggerFactory>()?.CreateLogger(typeof(SubjectKeyDerivation));
            return SubjectKeyDerivation.FromEnvironment(logger);
        });

        services.TryAddSingleton<ISubjectKeyStore>(sp =>
        {
            var derivation = sp.GetRequiredService<SubjectKeyDerivation>();
            var logger = sp.GetService<ILogger<InMemorySubjectKeyStore>>();
            return new InMemorySubjectKeyStore(derivation, logger);
        });

        services.TryAddSingleton<EncryptedFieldAccessor>();

        return services;
    }
}
