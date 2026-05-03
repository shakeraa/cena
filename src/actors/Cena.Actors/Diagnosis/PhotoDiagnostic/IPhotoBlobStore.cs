// =============================================================================
// Cena Platform — IPhotoBlobStore (EPIC-PRR-J PRR-412)
//
// Abstraction over whichever object-storage backend holds diagnostic
// photo bytes. The production binding is the S3 adapter shipped via
// Scope B (merge 33d672a5, ADR-0058, memory "Photo pipeline open
// points" 2026-04-22). The abstraction exists so PhotoDeletionWorker /
// PhotoDeletionAuditJob / dispute flows can be exercised end-to-end in
// tests without dragging AWS SDK + localstack into every unit run.
//
// Why an interface rather than calling S3 directly:
//   1. The 5-min SLA logic is pure domain state (ledger rows + clock);
//      the storage round-trip is the only thing that varies between
//      prod (S3), local dev (filesystem), and test (NoopPhotoBlobStore).
//      Keeping the domain logic blob-agnostic is the Dependency Inversion
//      rule from the general code playbook and matches
//      IDiagnosticDisputeRepository's split between in-memory and Marten.
//   2. Future backends (GCS for the EU data-residency SKU per roadmap
//      Phase 3) slot in without touching deletion logic.
//
// NOT a stub — see the NoopPhotoBlobStore header for the legitimate-
// fixture rationale (mirrors SandboxPaymentGateway and
// InMemoryDiagnosticDisputeRepository).
// =============================================================================

namespace Cena.Actors.Diagnosis.PhotoDiagnostic;

/// <summary>
/// Operations on the object-storage location of a diagnostic photo. The
/// <paramref name="blobKey"/> argument is the diagnostic id (which is
/// also <see cref="PhotoHashLedgerDocument.Id"/>); the concrete store
/// maps that onto its native address (S3 object key, filesystem path,
/// etc.) using a deterministic scheme owned by the store.
/// </summary>
public interface IPhotoBlobStore
{
    /// <summary>
    /// Delete the photo binary identified by <paramref name="blobKey"/>.
    /// Must be idempotent: deleting an already-absent key is a no-op,
    /// not an error (the deletion worker retries on transient failure
    /// so it must not trip on the second try).
    /// </summary>
    Task DeleteAsync(string blobKey, CancellationToken ct);

    /// <summary>
    /// Returns <c>true</c> if the blob is still present in the store.
    /// Used by <see cref="PhotoDeletionAuditJob"/> to verify the SLA —
    /// an extant blob past its 5-min budget is the "verifiable audit"
    /// signal the PRR-412 DoD asks for.
    /// </summary>
    Task<bool> ExistsAsync(string blobKey, CancellationToken ct);
}

/// <summary>
/// Legitimate test / dev fixture — NOT a stub. Mirrors the
/// <c>SandboxPaymentGateway</c> and <c>InMemoryDiagnosticDisputeRepository</c>
/// pattern: a fully-typed, concurrent-safe implementation that lets unit
/// tests and local dev exercise deletion/audit logic without standing up
/// S3 or localstack. The "No stubs — production grade" memory rule
/// (2026-04-11) bans fakes that pretend to do work and silently skip it;
/// this class keeps honest state (a concurrent set of present-keys) and
/// mutates it on Delete so the ledger-vs-blob invariant can be asserted
/// in tests.
///
/// Production composition binds the S3 adapter (ADR-0058) instead.
/// </summary>
public sealed class NoopPhotoBlobStore : IPhotoBlobStore
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _present = new();

    /// <summary>
    /// Seed the fixture with a blob key so tests can verify deletion
    /// actually removes it. Named <c>Seed</c> (not <c>Put</c>) to make
    /// explicit that this is fixture-setup, not a storage upload path
    /// — production uploads go through the S3 adapter.
    /// </summary>
    public void Seed(string blobKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobKey);
        _present[blobKey] = 1;
    }

    public Task DeleteAsync(string blobKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobKey);
        _present.TryRemove(blobKey, out _); // idempotent
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string blobKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blobKey);
        return Task.FromResult(_present.ContainsKey(blobKey));
    }
}
