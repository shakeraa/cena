// =============================================================================
// Cena Platform — MartenSoftCapEmissionLedger (PRR-401, EPIC-PRR-I, EPIC-PRR-J)
//
// Why this exists
// ---------------
// Production Marten-backed implementation of <see cref="ISoftCapEmissionLedger"/>.
// Replaces InMemorySoftCapEmissionLedger as the production DI binding per
// memory "No stubs — production grade" (2026-04-11). In-memory dedup is a
// correctness risk: a pod restart empties the dictionary and the next
// post-cap upload re-emits <c>EntitlementSoftCapReached_V1</c> — which
// lands on the parent's subscription stream AGAIN and inflates
// AbuseDetectionWorker's 30-day count.
//
// Pattern mirror
// --------------
// This file deliberately mirrors <c>MartenProcessedWebhookLog</c> (Stripe
// webhook dedup) and <c>MartenDiagnosticCreditLedger</c> (per-student
// credit rows) — same shape, same pre-check + Store + catch-collision
// idiom. Atomicity of "have I claimed this tuple?" → "no, claim it,
// return true" is delegated to Marten's optimistic concurrency on Store:
// a second writer racing the same compound id sees
// <see cref="DocumentAlreadyExistsException"/> after SaveChangesAsync and
// returns false.
//
// Pre-check + Store
// -----------------
// LoadAsync lets us avoid the spurious Store + SaveChanges round trip
// when the tuple is obviously already seen (common case: every upload
// past 101 re-hits the hot path). The final answer still comes from
// Store itself, because two racers could both pass the pre-check.
// Catching the narrow exception (not a broader Marten error) preserves
// fail-loud semantics for genuine storage errors.
// =============================================================================

using Marten;
using Marten.Exceptions;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public sealed class MartenSoftCapEmissionLedger : ISoftCapEmissionLedger
{
    private readonly IDocumentStore _store;

    public MartenSoftCapEmissionLedger(IDocumentStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<bool> TryClaimAsync(
        string studentSubjectIdHash,
        string capType,
        string monthWindow,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var id = SoftCapEmissionLedgerDocument.KeyOf(studentSubjectIdHash, capType, monthWindow);

        // Pre-check: skip the write round-trip when the tuple is already
        // present. Two concurrent racers that both pass this check fall
        // through to Store, where optimistic concurrency produces
        // exactly one winner.
        await using (var querySession = _store.QuerySession())
        {
            var existing = await querySession
                .LoadAsync<SoftCapEmissionLedgerDocument>(id, ct)
                .ConfigureAwait(false);
            if (existing is not null)
            {
                return false;
            }
        }

        await using var session = _store.LightweightSession();
        session.Insert(new SoftCapEmissionLedgerDocument
        {
            Id = id,
            StudentSubjectIdHash = studentSubjectIdHash,
            CapType = capType,
            MonthWindow = monthWindow,
            EmittedAtUtc = nowUtc,
        });

        try
        {
            await session.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DocumentAlreadyExistsException)
        {
            // Loser of the two-racer case: the winner's row is canonical.
            // Return false so the emitter treats this call as a no-op.
            return false;
        }
    }

    public async Task<bool> HasEmittedAsync(
        string studentSubjectIdHash,
        string capType,
        string monthWindow,
        CancellationToken ct)
    {
        var id = SoftCapEmissionLedgerDocument.KeyOf(studentSubjectIdHash, capType, monthWindow);
        await using var session = _store.QuerySession();
        var hit = await session
            .LoadAsync<SoftCapEmissionLedgerDocument>(id, ct)
            .ConfigureAwait(false);
        return hit is not null;
    }
}
