// =============================================================================
// Cena Platform — ParentChildBindingDocument (prr-009 / EPIC-PRR-C prod)
//
// Marten-persisted shape for the parent-child binding. Separate from the
// domain VO (ParentChildBinding) so Marten has a single-property string
// Id composed from the (parent, student, institute) triple, enabling
// indexed lookups without a composite-key hack.
//
// Id format: "{parentActorId}|{studentSubjectId}|{instituteId}". Canonical;
// produced by <see cref="MakeId"/>. Pipe is a safe separator because the
// VO's three identifier fields are declared as opaque anon ids that never
// contain it (per ADR-0038 anon-id format rules).
// =============================================================================

namespace Cena.Actors.Parent;

/// <summary>
/// Marten document wrapper for <see cref="ParentChildBinding"/>. Id is the
/// pipe-joined triple so Marten can index per-student and per-parent
/// queries against flattened columns; the domain VO fields are preserved
/// verbatim for round-trip correctness.
/// </summary>
public sealed record ParentChildBindingDocument
{
    /// <summary>Canonical <c>parent|student|institute</c> Id for Marten.</summary>
    public string Id { get; init; } = "";

    /// <summary>Opaque anon parent id.</summary>
    public string ParentActorId { get; init; } = "";

    /// <summary>Opaque anon student id.</summary>
    public string StudentSubjectId { get; init; } = "";

    /// <summary>ADR-0001 tenant scope.</summary>
    public string InstituteId { get; init; } = "";

    /// <summary>When the grant was recorded.</summary>
    public DateTimeOffset GrantedAtUtc { get; init; }

    /// <summary>
    /// Non-null when the grant has been revoked. The row is preserved
    /// after revocation for audit rather than deleted per ADR-0042
    /// (consent-history is append-only).
    /// </summary>
    public DateTimeOffset? RevokedAtUtc { get; init; }

    /// <summary>
    /// Build the canonical Marten Id from the three-part binding key.
    /// </summary>
    public static string MakeId(
        string parentActorId,
        string studentSubjectId,
        string instituteId)
        => $"{parentActorId}|{studentSubjectId}|{instituteId}";
}
