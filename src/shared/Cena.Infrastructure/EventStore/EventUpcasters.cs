// =============================================================================
// Cena Platform -- Event Upcaster Base Infrastructure
// Layer: Infrastructure | Runtime: .NET 9
//
// Provides base class and documentation for Marten event upcasters.
// Upcasters transform old event schemas to new versions during stream reads,
// enabling non-breaking schema evolution in an append-only event store.
//
// DATA-009: Event Schema Evolution via Upcasting
// =============================================================================

using Marten;

namespace Cena.Infrastructure.EventStore;

/// <summary>
/// Base class for strongly-typed event upcasters that transform V(N) events to V(N+1).
///
/// <para>
/// <b>When to create an upcaster:</b>
/// <list type="bullet">
///   <item>Adding a required field that needs a sensible default for old events</item>
///   <item>Renaming a field (old JSON key -> new record property)</item>
///   <item>Splitting or merging event types</item>
///   <item>Changing field types (e.g., string -> enum)</item>
/// </list>
/// </para>
///
/// <para>
/// <b>When you do NOT need an upcaster:</b>
/// <list type="bullet">
///   <item>Adding an optional/nullable field (JSON deserializer returns null/default)</item>
///   <item>Adding a field with a C# default parameter value</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Pattern:</b>
/// <code>
/// // 1. Define the V2 event record in the Events directory
/// public record ConceptAttempted_V2( ... ) : IDelegatedEvent;
///
/// // 2. Create the upcaster
/// public sealed class ConceptAttemptedUpcaster
///     : EventUpcaster&lt;ConceptAttempted_V1, ConceptAttempted_V2&gt;
/// {
///     protected override ConceptAttempted_V2 Upcast(ConceptAttempted_V1 old)
///         =&gt; new( ... map fields, provide defaults for new fields ... );
/// }
///
/// // 3. Register in MartenConfiguration.RegisterUpcasters()
/// opts.Events.Upcast&lt;ConceptAttempted_V1, ConceptAttempted_V2&gt;(
///     v1 =&gt; ConceptAttemptedUpcaster.Instance.Transform(v1));
/// </code>
/// </para>
///
/// <para>
/// <b>Rules:</b>
/// <list type="number">
///   <item>Never mutate old events in the database; upcasters run in-memory on read.</item>
///   <item>Upcasters must be pure functions (no I/O, no side effects).</item>
///   <item>Chain upcasters for multi-version jumps: V1->V2->V3 (each step is a separate upcaster).</item>
///   <item>After the V2 event is stable in production, the V1 type can be marked [Obsolete].</item>
///   <item>The old event type name (V1) stays registered so Marten can deserialize existing rows.</item>
/// </list>
/// </para>
/// </summary>
/// <typeparam name="TOld">The previous version of the event (e.g., ConceptAttempted_V1).</typeparam>
/// <typeparam name="TNew">The new version of the event (e.g., ConceptAttempted_V2).</typeparam>
public abstract class EventUpcaster<TOld, TNew>
    where TOld : class
    where TNew : class
{
    /// <summary>
    /// Transforms an old event instance to the new version.
    /// Must be a pure function -- no I/O, no side effects.
    /// </summary>
    protected abstract TNew Upcast(TOld old);

    /// <summary>
    /// Public entry point used by the Marten registration lambda.
    /// </summary>
    public TNew Transform(TOld old) => Upcast(old);
}

/// <summary>
/// Extension methods for registering event upcasters with Marten's StoreOptions.
/// </summary>
public static class EventUpcasterExtensions
{
    /// <summary>
    /// Registers a typed event upcaster instance with Marten's event store.
    /// This uses Marten's built-in <c>Events.Upcast&lt;TOld, TNew&gt;</c> API
    /// which transforms events in-memory during stream reads.
    /// </summary>
    /// <typeparam name="TOld">The previous version of the event.</typeparam>
    /// <typeparam name="TNew">The new version of the event.</typeparam>
    /// <param name="opts">The Marten store options.</param>
    /// <param name="upcaster">The upcaster instance containing the transformation logic.</param>
    public static void RegisterUpcaster<TOld, TNew>(
        this StoreOptions opts,
        EventUpcaster<TOld, TNew> upcaster)
        where TOld : class
        where TNew : class
    {
        opts.Events.Upcast<TOld, TNew>(old => upcaster.Transform(old));
    }
}
