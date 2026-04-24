// =============================================================================
// Cena Platform — MartenProcessedWebhookLog (EPIC-PRR-I PRR-301 prod binding)
//
// Production Marten-backed implementation of IProcessedWebhookLog.
// Replaces InMemoryProcessedWebhookLog as the production DI binding per
// memory "No stubs — production grade" (2026-04-11). In-memory dedup is
// a correctness risk: Stripe retries a webhook up to 3 days after the
// original send, and an in-memory dedup set is emptied on every pod
// restart. First webhook post-restart that Stripe is still retrying
// would be double-processed — duplicate subscription activation events
// on the student's stream, duplicate billing-cycle advances, and worse:
// a double-refund on a retry of the `charge.refunded` event.
//
// Pattern: tiny Marten document keyed by the Stripe event id. Atomicity
// of "have I seen this id?" -> "no, claim it, return true" is delegated
// to Marten's optimistic-concurrency behaviour on Store: a second writer
// racing to register the same id sees the unique-id collision and
// returns false after a SaveChangesAsync failure.
// =============================================================================

using Marten;
using Marten.Exceptions;

namespace Cena.Actors.Subscriptions.Stripe;

/// <summary>
/// Marten-persisted dedup row for a Stripe event id. Id is the Stripe
/// <c>evt_...</c> identifier — one row per event, ever.
/// </summary>
public sealed record ProcessedWebhookDocument
{
    /// <summary>Stripe event id (<c>evt_...</c>). Primary key.</summary>
    public string Id { get; init; } = "";

    /// <summary>When the event was first seen + claimed.</summary>
    public DateTimeOffset ProcessedAtUtc { get; init; }
}

/// <summary>
/// Marten-backed implementation of <see cref="IProcessedWebhookLog"/>.
/// </summary>
public sealed class MartenProcessedWebhookLog : IProcessedWebhookLog
{
    private readonly IDocumentStore _store;
    private readonly TimeProvider _clock;

    public MartenProcessedWebhookLog(IDocumentStore store, TimeProvider clock)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <inheritdoc />
    public async Task<bool> TryRegisterAsync(string eventId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new ArgumentException("Event id required.", nameof(eventId));
        }

        // Pre-check lets us avoid a spurious Store + SaveChangesAsync round
        // trip when the event is obviously already seen. The final answer
        // still comes from Store itself, because two concurrent writers
        // could both pass the pre-check.
        await using var querySession = _store.QuerySession();
        var existing = await querySession
            .LoadAsync<ProcessedWebhookDocument>(eventId, ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return false;
        }

        await using var session = _store.LightweightSession();
        session.Insert(new ProcessedWebhookDocument
        {
            Id = eventId,
            ProcessedAtUtc = _clock.GetUtcNow(),
        });

        try
        {
            await session.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DocumentAlreadyExistsException)
        {
            // Two webhooks racing for the same event id: loser sees the
            // collision and returns false. Stripe will retry; the winner's
            // row is canonical. Catching the narrow exception (not a
            // broader Marten error) preserves fail-loud semantics for
            // genuine errors.
            return false;
        }
    }
}
