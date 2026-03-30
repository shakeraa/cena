// =============================================================================
// Cena Platform -- Event Store Configuration Extensions
// Layer: Infrastructure | Runtime: .NET 9
//
// DATA-009: Centralised extension method for Marten event store setup.
// Called from both Cena.Actors.Host/Program.cs and Cena.Api.Host/Program.cs
// via the existing MartenConfiguration.ConfigureCenaEventStore() method.
//
// NOTE: Event type registration and upcaster registration live in
// Cena.Actors/Configuration/MartenConfiguration.cs because that assembly
// owns the event types. This file provides shared infrastructure helpers
// that do NOT depend on concrete event types.
// =============================================================================

using Marten;

namespace Cena.Infrastructure.EventStore;

/// <summary>
/// Shared Marten event store configuration that does not depend on
/// concrete event types. Event registration and upcaster wiring are
/// handled in <c>Cena.Actors.Configuration.MartenConfiguration</c>.
///
/// <para>
/// <b>Event schema evolution checklist:</b>
/// <list type="number">
///   <item>Define the new event record (V2) next to the existing V1 in the Events directory.</item>
///   <item>Create an upcaster class extending <see cref="EventUpcaster{TOld,TNew}"/>.</item>
///   <item>Register the V2 event type via <c>opts.Events.AddEventType&lt;V2&gt;()</c>.</item>
///   <item>Register the upcaster via <c>opts.RegisterUpcaster(new MyUpcaster())</c>.</item>
///   <item>Update any snapshot Apply() methods and projections to handle V2.</item>
///   <item>Write a test that round-trips V1 -> upcaster -> V2 with correct defaults.</item>
/// </list>
/// </para>
/// </summary>
public static class EventStoreConfiguration
{
    /// <summary>
    /// Applies shared event store settings that are common across all hosts.
    /// Call this from <c>MartenConfiguration.ConfigureCommon</c> to layer in
    /// infrastructure-level defaults before domain-specific registration.
    /// </summary>
    public static void ApplyInfrastructureDefaults(this StoreOptions opts)
    {
        // Currently a no-op: all Marten config is in MartenConfiguration.
        // This extension point exists so that future infrastructure-level
        // settings (retry policies, event archival, compression) can be
        // added here without touching the Actors assembly.
    }
}
