// =============================================================================
// Cena Platform — TutorHandoffServiceRegistration (EPIC-PRR-I PRR-325)
//
// Why this exists:
//   The student API host registers two DI bindings for the tutor-handoff
//   endpoint:
//     1. ITutorHandoffHtmlRenderer → TutorHandoffHtmlRenderer
//     2. ITutorHandoffCardSource → NoopTutorHandoffCardSource (default)
//
//   Wrapping both registrations in a single extension method keeps the
//   call site in Program.cs short (one line) and future-proofs the
//   wiring: when the real Marten-backed card source lands, only the
//   AddTutorHandoffMarten() companion method — NOT Program.cs —
//   changes.
//
//   TryAdd-guarded so a production host can register a Marten-backed
//   implementation BEFORE calling this method without being overwritten.
//   Mirrors how AddParentDashboardEndpoints / AddStripeServices are
//   composed throughout the student host.
// =============================================================================

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Api.Contracts.Parenting;

/// <summary>
/// DI extension methods for the tutor-handoff report feature.
/// </summary>
public static class TutorHandoffServiceRegistration
{
    /// <summary>
    /// Register the tutor-handoff renderer and the zero-data card-source
    /// default. TryAdd-guarded — a real card source (e.g. Marten-backed)
    /// can be registered by the host BEFORE calling this method and will
    /// not be overwritten.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <returns>The same container for chaining.</returns>
    public static IServiceCollection AddTutorHandoffServices(
        this IServiceCollection services)
    {
        services.TryAddSingleton<ITutorHandoffHtmlRenderer, TutorHandoffHtmlRenderer>();
        services.TryAddSingleton<ITutorHandoffCardSource, NoopTutorHandoffCardSource>();
        return services;
    }
}
