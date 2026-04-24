// =============================================================================
// Cena Platform — MasterySignalServiceRegistration (EPIC-PRR-J PRR-381,
// EPIC-PRR-A mastery-engine link)
//
// Why this file exists
// --------------------
// DI entry points for the post-reflection mastery-signal pipeline. Hosts
// call:
//
//   · services.AddMasterySignalServices()         — shared services,
//       registers InMemoryMasterySignalEmitter as the default emitter
//       (dev / test / emulator-safe).
//
//   · services.AddMasterySignalServicesMarten()   — production binding,
//       replaces the in-memory emitter with MartenMasterySignalEmitter
//       and registers the MasterySignalEmitted_V1 event type on Marten
//       so the per-student stream is queryable.
//
// Pattern mirrors ExamTargetRetentionServiceRegistration (AddX / AddXMarten
// split). Idempotent via TryAdd / Replace.
// =============================================================================

using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Actors.Mastery;

/// <summary>
/// DI extensions for the <see cref="IPostReflectionMasteryService"/> and
/// <see cref="IMasterySignalEmitter"/> bindings.
/// </summary>
public static class MasterySignalServiceRegistration
{
    /// <summary>
    /// Register the shared mastery-signal services with an in-memory emitter
    /// default. Safe to call from every host; production hosts then opt into
    /// <see cref="AddMasterySignalServicesMarten"/> to swap the emitter.
    /// </summary>
    public static IServiceCollection AddMasterySignalServices(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<MasterySignalOptions>();

        // Register the concrete in-memory emitter as a singleton so tests
        // and the in-proc emulator can resolve it directly to assert emitted
        // events, then bind the interface to the concrete. Replace (not Add)
        // is used by the Marten variant so resolution is deterministic.
        services.TryAddSingleton<InMemoryMasterySignalEmitter>();
        services.TryAddSingleton<IMasterySignalEmitter>(sp =>
            sp.GetRequiredService<InMemoryMasterySignalEmitter>());

        services.TryAddSingleton<IPostReflectionMasteryService, PostReflectionMasteryService>();

        return services;
    }

    /// <summary>
    /// Replace the in-memory emitter with the Marten binding and register
    /// the <see cref="MasterySignalEmitted_V1"/> event type. Requires
    /// <see cref="AddMasterySignalServices"/> to have been called first.
    /// </summary>
    public static IServiceCollection AddMasterySignalServicesMarten(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<MartenMasterySignalEmitter>();
        services.Replace(ServiceDescriptor.Singleton<IMasterySignalEmitter>(sp
            => sp.GetRequiredService<MartenMasterySignalEmitter>()));

        services.ConfigureMarten(opts =>
            opts.Events.AddEventType<MasterySignalEmitted_V1>());

        return services;
    }
}
