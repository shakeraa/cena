// =============================================================================
// Cena Platform — Retired document: AtRiskStudentDocument
//
// Retired 2026-04-20 per prr-013 + ADR-0012 RDY-080. The "at-risk" concept is
// now session-scoped (ADR-0003) and lives on SessionRiskAssessment inside the
// LearningSessionActor. It is never persisted to a profile, a rollup, or a
// projection.
//
// WHY THIS FILE STILL EXISTS:
//   Marten tables keyed on this type may hold rows that were written before
//   the retirement (dev-tier seed + any pre-2026-04-20 rollup runs). Deleting
//   the CLR type outright would break Marten deserialisation on any future
//   replay that still encounters those rows (even though the schema
//   registration was removed on the same date). Keeping the class declared —
//   but marked [Obsolete] and moved out of the active Documents/ namespace
//   root — gives us safe read-side tolerance without inviting new writes.
//
// NEW CODE MUST NOT:
//   * Write instances of this document (no session.Store, no projection).
//   * Query this document via Marten (the schema registration is gone; the
//     table is unmanaged and will be dropped on a future schema migration
//     when we know no more orphans exist).
//   * Take a dependency on this type from any new aggregate, DTO, or service.
//
// TEACHER-FACING "students needing intervention" UX: see
// src/actors/Cena.Actors/Sessions/SessionRiskAssessment.cs — the session-
// scoped, CI-bounded, in-surface-only replacement seam.
// =============================================================================

namespace Cena.Infrastructure.Documents.Legacy;

/// <summary>
/// DEPRECATED — do NOT reference from new code. Retained only so historical
/// Marten rows (written before prr-013 retirement on 2026-04-20) remain
/// deserialisable during cleanup. The session-scoped replacement is
/// <c>Cena.Actors.Sessions.SessionRiskAssessment</c>.
/// </summary>
[Obsolete(
    "Retired 2026-04-20 per prr-013 + ADR-0012 RDY-080. Use session-scoped " +
    "SessionRiskAssessment via LearningSessionActor. This type is kept only " +
    "for read-side tolerance of pre-retirement Marten rows.",
    error: false)]
public class AtRiskStudentDocument
{
    public string Id { get; set; } = "";              // "{schoolId}:{studentId}:{yyyy-MM-dd}"
    public string StudentId { get; set; } = "";
    public string StudentName { get; set; } = "";
    public string ClassId { get; set; } = "";
    public string SchoolId { get; set; } = "";
    public DateTimeOffset Date { get; set; }

    public string RiskLevel { get; set; } = "medium"; // high | medium | low
    public float CurrentAvgMastery { get; set; }
    public float MasteryDeclineLast14d { get; set; }
    public string RecommendedIntervention { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
