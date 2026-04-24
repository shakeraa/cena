// =============================================================================
// Cena Platform — IMasterySignalEmitter + InMemory + Marten implementations
// (EPIC-PRR-J PRR-381, EPIC-PRR-A mastery-engine link)
//
// Why this file exists
// --------------------
// PRR-381 post-reflection retry-success path needs a durable way to emit a
// MasterySignalEmitted_V1 event. Two bindings:
//
//   · InMemoryMasterySignalEmitter  — production-grade for dev, test, and
//     emulator hosts (mirrors the SandboxPaymentGateway / ChurnReason
//     in-memory idiom per memory "No stubs — production grade"). Appends to
//     a ConcurrentBag so tests can enumerate and assert emitted events.
//
//   · MartenMasterySignalEmitter    — production binding. Appends to a
//     per-student stream keyed `masterysignal-{studentAnonId}` (mirrors the
//     aggregate-stream naming convention used elsewhere in the codebase).
//     Uses FetchStreamStateAsync to discriminate first-event (StartStream)
//     vs. subsequent-event (Append) without a try/catch on a not-found
//     exception — the same pattern as MartenConsentAggregateStore.
//
// Stream-per-student was chosen deliberately over a global stream so that
// RTBF / retention workers can target a single student's signals by stream
// key without a table-scan, matching ADR-0050 §6 retention semantics for
// other per-student event streams.
//
// No streak, no variable-ratio reward — the emitter is a straight append;
// there is no counter, no sequence bonus, no grouping mechanic. See the
// MasterySignalEmitted_V1 banner for the ship-gate discipline.
// =============================================================================

using Marten;

namespace Cena.Actors.Mastery;

/// <summary>
/// Abstraction over the MasterySignalEmitted_V1 event sink. Callers
/// (<see cref="IPostReflectionMasteryService"/> and future post-reflection
/// success surfaces) depend on this interface so the concrete store is
/// swappable between in-memory (dev/test) and Marten (prod).
/// </summary>
public interface IMasterySignalEmitter
{
    /// <summary>
    /// Append a mastery-signal event to the downstream store. Implementations
    /// must be idempotent under retry of the SAME event instance — the
    /// caller may retry on transient failure without producing duplicate
    /// downstream effects beyond a single append to the stream.
    /// </summary>
    Task EmitAsync(MasterySignalEmitted_V1 @event, CancellationToken ct = default);
}

/// <summary>
/// In-memory <see cref="IMasterySignalEmitter"/>. Appends every event to a
/// thread-safe collection so tests can enumerate emissions. Not a stub —
/// a full implementation with production-grade semantics for the
/// non-persistence use cases (dev, test, emulator), consistent with the
/// repository's "No stubs — production grade" discipline.
/// </summary>
public sealed class InMemoryMasterySignalEmitter : IMasterySignalEmitter
{
    private readonly System.Collections.Concurrent.ConcurrentBag<MasterySignalEmitted_V1> _events = new();

    /// <summary>Snapshot of every event emitted so far, in insertion order is NOT guaranteed.</summary>
    public IReadOnlyCollection<MasterySignalEmitted_V1> Events => _events.ToArray();

    /// <inheritdoc />
    public Task EmitAsync(MasterySignalEmitted_V1 @event, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        _events.Add(@event);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Marten-backed <see cref="IMasterySignalEmitter"/>. Appends to a per-student
/// event stream keyed <c>masterysignal-{studentAnonId}</c>.
/// </summary>
public sealed class MartenMasterySignalEmitter : IMasterySignalEmitter
{
    private readonly IDocumentStore _store;

    /// <summary>Prefix for the per-student stream key.</summary>
    public const string StreamKeyPrefix = "masterysignal-";

    public MartenMasterySignalEmitter(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>Build the canonical stream key for a student.</summary>
    public static string StreamKey(string studentAnonId)
    {
        if (string.IsNullOrWhiteSpace(studentAnonId))
        {
            throw new ArgumentException(
                "studentAnonId must be non-empty.",
                nameof(studentAnonId));
        }
        return StreamKeyPrefix + studentAnonId;
    }

    /// <inheritdoc />
    public async Task EmitAsync(MasterySignalEmitted_V1 @event, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var streamKey = StreamKey(@event.StudentAnonId);
        await using var session = _store.LightweightSession();

        // FetchStreamStateAsync pattern (cf. MartenConsentAggregateStore)
        // discriminates first-event vs. subsequent-event without throwing.
        var existing = await session.Events
            .FetchStreamStateAsync(streamKey, token: ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            session.Events.StartStream(streamKey, @event);
        }
        else
        {
            session.Events.Append(streamKey, @event);
        }

        await session.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
