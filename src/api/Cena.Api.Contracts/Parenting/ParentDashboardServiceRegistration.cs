// =============================================================================
// Cena Platform — ParentDashboardServiceRegistration (EPIC-PRR-I PRR-320)
//
// Why this exists:
//   The /api/me/parent-dashboard endpoint needs an
//   IParentDashboardCardSource registered in DI to resolve. Two
//   implementations ship:
//     1. MartenParentDashboardCardSource (production) — reads Marten
//        event log for HintRequested_V1 events to compute per-student
//        engagement scalars.
//     2. NoopParentDashboardCardSource (fallback) — zero-data default
//        for tests and sandbox composers without a document store.
//
//   This extension registers the Noop default via TryAddSingleton so
//   a host that has already wired the Marten implementation (via
//   AddParentDashboardMarten, called ahead of this method in
//   Program.cs) is not overwritten. Mirrors the PRR-325 pattern in
//   TutorHandoffServiceRegistration.AddTutorHandoffServices.
// =============================================================================

using Marten;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Cena.Api.Contracts.Parenting;

/// <summary>DI extension methods for the parent-dashboard endpoint.</summary>
public static class ParentDashboardServiceRegistration
{
    /// <summary>
    /// Register the zero-data <see cref="IParentDashboardCardSource"/>
    /// default. TryAdd-guarded — if a host wires
    /// <see cref="MartenParentDashboardCardSource"/> first (via
    /// <see cref="AddParentDashboardMarten"/>) this call does not
    /// overwrite it.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <returns>The same container for chaining.</returns>
    public static IServiceCollection AddParentDashboardServices(
        this IServiceCollection services)
    {
        services.TryAddSingleton<IParentDashboardCardSource, NoopParentDashboardCardSource>();
        return services;
    }

    /// <summary>
    /// Register <see cref="MartenParentDashboardCardSource"/> as the
    /// production <see cref="IParentDashboardCardSource"/>. The caller
    /// must have already registered Marten's <see cref="IDocumentStore"/>
    /// (via <c>AddMarten(...)</c>). Call this BEFORE
    /// <see cref="AddParentDashboardServices"/> so the Noop TryAdd
    /// does not overwrite the real implementation.
    /// </summary>
    /// <param name="services">DI container.</param>
    /// <returns>The same container for chaining.</returns>
    public static IServiceCollection AddParentDashboardMarten(
        this IServiceCollection services)
    {
        services.AddSingleton<IParentDashboardCardSource, MartenParentDashboardCardSource>();
        return services;
    }
}
