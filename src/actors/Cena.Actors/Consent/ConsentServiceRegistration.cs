// =============================================================================
// Cena Platform — ConsentAggregate DI registration (prr-155)
//
// Single entry point for wiring the ConsentAggregate primitives into a DI
// container. Idempotent via TryAdd — safe to call from multiple Host
// compositions (Actors.Host, Student.Api.Host, Admin.Api.Host).
//
// Registered:
//   - IConsentAggregateStore -> InMemoryConsentAggregateStore (singleton).
//     A future Marten-backed implementation will override via a second
//     AddConsentAggregate(...) overload taking a factory.
//   - ConsentCommandHandler (singleton — stateless).
//
// This method depends on EncryptedFieldAccessor which is registered by
// AddSubjectKeyStore() in Cena.Infrastructure. Callers MUST call
// AddSubjectKeyStore() first; this helper does so idempotently to be safe.
// =============================================================================

using Cena.Infrastructure.Compliance;
using Cena.Infrastructure.Compliance.KeyStore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Actors.Consent;

/// <summary>
/// Extension methods to register ConsentAggregate services.
/// </summary>
public static class ConsentServiceRegistration
{
    /// <summary>
    /// Register the ConsentAggregate primitives: store, command handler, and
    /// the underlying subject-key-store / encrypted-field-accessor if not
    /// already registered. Idempotent.
    /// </summary>
    public static IServiceCollection AddConsentAggregate(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Crypto-shredding primitives — idempotent internally.
        services.AddSubjectKeyStore();

        // Aggregate store: defaults to in-memory; Marten overlay is a
        // follow-up (EPIC-PRR-A Sprint 2).
        services.TryAddSingleton<IConsentAggregateStore, InMemoryConsentAggregateStore>();

        // Command handler is stateless — singleton is correct.
        services.TryAddSingleton<ConsentCommandHandler>();

        // Shadow-write adapter for the legacy GdprConsentManager facade.
        services.TryAddSingleton<IConsentAggregateWriter, ConsentAggregateWriterAdapter>();

        // prr-052: age-band lookup. Default to Marten-backed; tests can
        // override before this line runs by registering their own
        // IStudentAgeBandLookup first.
        services.TryAddSingleton<IStudentAgeBandLookup, MartenStudentAgeBandLookup>();

        return services;
    }
}
