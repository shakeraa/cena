// =============================================================================
// Cena Platform — InMemorySoftCapEmissionLedger (PRR-401, EPIC-PRR-I, EPIC-PRR-J)
//
// Why this exists
// ---------------
// Process-local implementation used by tests and the dev composition-root.
// NOT a stub — it is thread-safe, passes the full ledger contract suite,
// and matches the concurrency behaviour of the Marten impl closely enough
// that SoftCapEventEmitter's tests can use it without fidelity loss. The
// only correctness property it loses is durability across pod restarts,
// which is why AddPhotoDiagnosticMarten replaces the binding with
// MartenSoftCapEmissionLedger at composition time.
//
// Concurrency
// -----------
// ConcurrentDictionary.TryAdd gives us the atomic "first writer wins"
// semantics the ledger needs. Two threads racing with the same compound
// id will see exactly one TryAdd return true; the other sees false. No
// locks, no torn reads.
// =============================================================================

using System.Collections.Concurrent;

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

public sealed class InMemorySoftCapEmissionLedger : ISoftCapEmissionLedger
{
    private readonly ConcurrentDictionary<string, SoftCapEmissionLedgerDocument> _byId = new();

    public Task<bool> TryClaimAsync(
        string studentSubjectIdHash,
        string capType,
        string monthWindow,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        var id = SoftCapEmissionLedgerDocument.KeyOf(studentSubjectIdHash, capType, monthWindow);
        var doc = new SoftCapEmissionLedgerDocument
        {
            Id = id,
            StudentSubjectIdHash = studentSubjectIdHash,
            CapType = capType,
            MonthWindow = monthWindow,
            EmittedAtUtc = nowUtc,
        };
        var claimed = _byId.TryAdd(id, doc);
        return Task.FromResult(claimed);
    }

    public Task<bool> HasEmittedAsync(
        string studentSubjectIdHash,
        string capType,
        string monthWindow,
        CancellationToken ct)
    {
        var id = SoftCapEmissionLedgerDocument.KeyOf(studentSubjectIdHash, capType, monthWindow);
        return Task.FromResult(_byId.ContainsKey(id));
    }
}
