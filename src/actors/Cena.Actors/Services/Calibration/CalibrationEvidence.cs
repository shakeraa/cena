// =============================================================================
// Cena Platform — Calibration Evidence (RDY-080)
//
// Audit-trail document that backs a single ConcordanceMapping version.
// Persisted as a Marten document so the full justification for why a
// given mapping was approved remains queryable years later.
//
// Writers: the calibration pipeline (offline fitter + review board).
// Readers: admin dashboard ("why is mapping v3 the current shipping
// version?"), regulatory audit, Dr. Yael's review UI.
// =============================================================================

namespace Cena.Actors.Services.Calibration;

/// <summary>
/// Evidence bundle for a single <see cref="ConcordanceMapping"/>
/// version. One of these exists per mapping version; never mutated
/// after approval.
/// </summary>
public sealed class CalibrationEvidence
{
    /// <summary>Marten document id. Same as the mapping version for 1:1.</summary>
    public int Id { get; set; }

    /// <summary>The mapping version this evidence backs.</summary>
    public required int MappingVersion { get; init; }

    /// <summary>
    /// Pre-registration hash — SHA-256 hex of the pre-registered study
    /// design doc at the time of fitting. Guards against post-hoc
    /// model-shopping: any change to the pre-reg would produce a
    /// different hash and the evidence would no longer match.
    /// </summary>
    public required string PreRegistrationDocHash { get; init; }

    /// <summary>
    /// Which calibration path produced this evidence.
    /// </summary>
    public required CalibrationPath Path { get; init; }

    /// <summary>
    /// The raw fit report output by the offline fitter (R / Python /
    /// whatever). Stored as a JSON blob so re-visits don't lose the
    /// full set of diagnostics.
    /// </summary>
    public required string FitReportJson { get; init; }

    /// <summary>
    /// SHA-256 hex of the ordered (student-anon-id, θ snapshot UTC,
    /// Bagrut score) tuples in the calibration sample. Enables a
    /// future "was my data in the fit?" query without retaining the
    /// cohort list.
    /// </summary>
    public required string CalibrationSampleHash { get; init; }

    /// <summary>SHA-256 hex of the validation sample, same shape.</summary>
    public required string ValidationSampleHash { get; init; }

    public required CalibrationAdequacy Adequacy { get; init; }

    /// <summary>
    /// Review board sign-offs. The mapping is not production-ready
    /// until all required signers have approved. Missing required
    /// signers blocks F8 point-estimate UI.
    /// </summary>
    public required IReadOnlyList<ReviewSignoff> Signoffs { get; init; }

    public required DateTimeOffset FittedAtUtc { get; init; }
    public DateTimeOffset? ApprovedAtUtc { get; set; }
    public DateTimeOffset? SupersededAtUtc { get; set; }

    /// <summary>
    /// Free-form notes from the review board. Kept because
    /// "why did we reject the mapping with good fit stats?" is a
    /// question that comes up and the answer is usually qualitative.
    /// </summary>
    public string ReviewNotes { get; set; } = string.Empty;
}

public enum CalibrationPath
{
    /// <summary>Longitudinal cohort: Cena users who sat Bagrut.</summary>
    LongitudinalCohortPathA,

    /// <summary>Common-item equating via Ministry-licensed anchors.</summary>
    CommonItemEquatingPathB
}

/// <summary>
/// A single review-board sign-off. Typically one from psychometrics
/// (Dr. Yael), one from pedagogy (Dr. Nadia), one from legal/DPO.
/// All three are required for production approval.
/// </summary>
public sealed record ReviewSignoff(
    string Role,
    string Approver,
    DateTimeOffset SignedAtUtc,
    string Comment)
{
    public static readonly IReadOnlyList<string> RequiredRolesForProduction =
        new[] { "psychometrics-lead", "pedagogy-lead", "legal-dpo" };
}
