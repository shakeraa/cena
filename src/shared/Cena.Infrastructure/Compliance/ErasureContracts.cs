// =============================================================================
// Cena Platform — GDPR Right-to-Erasure contracts (SEC-005, ADR-0038, prr-152)
//
// Extracted from RightToErasureService.cs to keep that file under the 500-LOC
// rule (the service class itself grew past 500 once the prr-152 cascade
// projections were threaded through — see IErasureProjectionCascade).
//
// This file holds ONLY types (enums, DTOs, interfaces). The runtime logic
// stays in RightToErasureService.cs.
// =============================================================================

namespace Cena.Infrastructure.Compliance;

/// <summary>
/// Status of a GDPR erasure request through its lifecycle.
/// </summary>
public enum ErasureStatus
{
    /// <summary>Initial request received, cooling period not yet started.</summary>
    Requested,

    /// <summary>30-day cooling period in effect.</summary>
    CoolingPeriod,

    /// <summary>Erasure actively being processed.</summary>
    Processing,

    /// <summary>Erasure completed successfully across all stores.</summary>
    Completed,

    /// <summary>Request cancelled by data subject before processing.</summary>
    Cancelled,
}

/// <summary>
/// Types of erasure actions that can be taken against a data store.
/// </summary>
public enum ErasureAction
{
    /// <summary>Data anonymized (PII removed/irreversibly hashed, record retained for integrity).</summary>
    Anonymized,

    /// <summary>Data permanently deleted.</summary>
    Deleted,

    /// <summary>Data preserved (legal hold, audit requirement, or FERPA).</summary>
    Preserved,
}

/// <summary>
/// Represents a single erasure action taken against a specific data store.
/// Part of the ErasureManifest for audit trail purposes.
/// </summary>
public sealed class ErasureManifestItem
{
    /// <summary>Name of the data store (e.g., "StudentProfile", "TutorMessages").</summary>
    public string Store { get; set; } = "";

    /// <summary>The type of action taken.</summary>
    public ErasureAction Action { get; set; }

    /// <summary>Number of records/rows affected in this store.</summary>
    public int Count { get; set; }

    /// <summary>Additional context for audit (e.g., "HMAC-SHA256 hashed").</summary>
    public string? Details { get; set; }

    public ErasureManifestItem(string store, ErasureAction action, int count, string? details = null)
    {
        Store = store;
        Action = action;
        Count = count;
        Details = details;
    }
}

/// <summary>
/// Complete manifest of all erasure actions taken across all data stores.
/// Provides full audit trail for compliance verification.
/// </summary>
public sealed class ErasureManifest
{
    /// <summary>The student ID that was erased.</summary>
    public string StudentId { get; set; } = "";

    /// <summary>Timestamp when erasure was completed.</summary>
    public DateTimeOffset CompletedAt { get; set; }

    /// <summary>All actions taken across data stores.</summary>
    public List<ErasureManifestItem> Actions { get; set; } = new();

    /// <summary>Total records affected across all stores.</summary>
    public int RowsAffected => Actions.Sum(a => a.Count);

    public ErasureManifest(string studentId, DateTimeOffset completedAt)
    {
        StudentId = studentId;
        CompletedAt = completedAt;
    }

    /// <summary>Adds an action to the manifest.</summary>
    public void AddAction(string store, ErasureAction action, int count, string? details = null)
    {
        Actions.Add(new ErasureManifestItem(store, action, count, details));
    }
}

/// <summary>
/// Interface for building an erasure manifest during processing.
/// Abstracts manifest construction for testability.
/// </summary>
public interface IErasureManifestBuilder
{
    /// <summary>Creates a new manifest for the specified student.</summary>
    ErasureManifest CreateManifest(string studentId, DateTimeOffset completedAt);
}

/// <summary>
/// Default implementation of the erasure manifest builder.
/// </summary>
public sealed class ErasureManifestBuilder : IErasureManifestBuilder
{
    /// <inheritdoc />
    public ErasureManifest CreateManifest(string studentId, DateTimeOffset completedAt)
    {
        return new ErasureManifest(studentId, completedAt);
    }
}

/// <summary>
/// Provides configuration for cryptographic operations during erasure.
/// </summary>
public interface IErasureCryptoConfig
{
    /// <summary>The pepper value used for HMAC hashing (should be 32+ bytes).</summary>
    string Pepper { get; }
}

/// <summary>
/// Default implementation reading pepper from configuration.
/// </summary>
public sealed class ErasureCryptoConfig : IErasureCryptoConfig
{
    /// <inheritdoc />
    public string Pepper { get; }

    public ErasureCryptoConfig(string pepper)
    {
        if (string.IsNullOrEmpty(pepper))
            throw new ArgumentException("Erasure pepper must be configured", nameof(pepper));
        Pepper = pepper;
    }
}

/// <summary>
/// A GDPR erasure request record stored in Marten.
/// </summary>
public sealed class ErasureRequest
{
    /// <summary>Unique identifier for this request.</summary>
    public Guid Id { get; set; }

    /// <summary>The student ID to erase.</summary>
    public string StudentId { get; set; } = "";

    /// <summary>Current status of the erasure request.</summary>
    public ErasureStatus Status { get; set; }

    /// <summary>When the request was initially received.</summary>
    public DateTimeOffset RequestedAt { get; set; }

    /// <summary>When the erasure was actually processed (null if not yet).</summary>
    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Who requested the erasure (e.g., "student:self", "parent:email@example.com").</summary>
    public string? RequestedBy { get; set; }

    /// <summary>The completed erasure manifest (null until completion).</summary>
    public ErasureManifest? Manifest { get; set; }
}

/// <summary>
/// Service interface for GDPR Right to Erasure operations.
/// </summary>
public interface IRightToErasureService
{
    /// <summary>Submit a new erasure request (enters cooling period).</summary>
    Task<ErasureRequest> RequestErasureAsync(string studentId, string requestedBy, CancellationToken ct = default);

    /// <summary>Process the erasure for a student (after cooling period).</summary>
    Task ProcessErasureAsync(string studentId, CancellationToken ct = default);

    /// <summary>Get the current status of an erasure request.</summary>
    Task<ErasureRequest?> GetErasureStatusAsync(string studentId, CancellationToken ct = default);
}

/// <summary>
/// prr-152 — cascade collaborator that erases per-student projections
/// added after the SEC-005 / ADR-0038 baseline. The <see cref="RightToErasureService"/>
/// delegates to every registered cascade during ProcessErasureAsync so the
/// core service does not grow past the 500-LOC rule each time a new
/// per-student Document lands.
///
/// Contract: one implementation per logical projection family (e.g. a
/// single implementation for ParentDigestPreferences; a separate one for
/// the TutorContext cache). Each returns its <see cref="ErasureManifestItem"/>
/// so the manifest audit trail stays complete.
///
/// Implementations MUST be idempotent — the erasure worker may re-invoke
/// on retry, and a second invocation for an already-erased student must
/// not fail the whole pipeline (return Count=0).
/// </summary>
public interface IErasureProjectionCascade
{
    /// <summary>
    /// Stable name of the projection family this cascade handles. Surfaces
    /// in the manifest so operators can tell which cascades fired.
    /// </summary>
    string ProjectionName { get; }

    /// <summary>
    /// Erase the projection for <paramref name="studentId"/>. Returns the
    /// audit entry to append to the manifest. Count=0 means either the
    /// student had no data in this projection or the cascade has already
    /// run (both are benign — idempotence).
    /// </summary>
    Task<ErasureManifestItem> EraseForStudentAsync(
        string studentId,
        CancellationToken ct);
}
