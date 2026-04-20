// =============================================================================
// Cena Platform — ReadinessBucket (prr-007 theta isolation seam)
//
// Coarse-grained ordinal bucket presented to students + parents + teachers in
// place of a raw IRT theta scalar. The seam enforces ADR-0012's
// SessionRiskAssessment pattern at the DTO boundary:
//
//   * The LLM explains. The CAS verifies. The mapper BUCKETS.
//   * No outbound payload may carry a raw theta/ability/readiness double.
//   * See NoThetaInOutboundDtoTest for the architecture-level guard.
//
// Boundaries (chosen from BKT progression threshold 0.85 mastery + IRT 2PL
// ability-scale convention):
//
//   Emerging     theta < -1.0              (or CI-uncertain; see mapper)
//   Developing   -1.0 <= theta < 0.0
//   Proficient    0.0 <= theta < 1.0
//   ExamReady     1.0 <= theta
//
// Ordinal on purpose: downstream code may compare (bucket >= Proficient) to
// gate content without needing the theta number back.
// =============================================================================

namespace Cena.Actors.Mastery;

/// <summary>
/// Ordinal readiness bucket exposed to students/parents/teachers in place of
/// a raw IRT theta scalar (prr-007). Ministry-defensibly conservative: when
/// the 80% CI straddles a bucket boundary, the lower bucket wins — we would
/// rather understate readiness than overstate it to a student or to a
/// Ministry auditor.
/// </summary>
public enum ReadinessBucket
{
    /// <summary>theta &lt; -1.0, or CI straddles a boundary with lower bound below Emerging cutoff.</summary>
    Emerging = 0,

    /// <summary>-1.0 &lt;= theta &lt; 0.0 (confident bucket).</summary>
    Developing = 1,

    /// <summary>0.0 &lt;= theta &lt; 1.0 (confident bucket).</summary>
    Proficient = 2,

    /// <summary>theta &gt;= 1.0 (confident bucket, Ministry-exam-ready range).</summary>
    ExamReady = 3
}
