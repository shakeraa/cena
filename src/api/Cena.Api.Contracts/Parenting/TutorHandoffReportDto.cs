// =============================================================================
// Cena Platform — Tutor-handoff report DTOs (EPIC-PRR-I PRR-325)
//
// Why this exists:
//   Premium parents need a shareable artefact to hand to their student's
//   external tutor ("this month my kid practiced X, struggled with Y,
//   mastered Z"). Persona #7 (human tutor) flips from competitor to channel
//   the moment Cena emits a handoff they can plug into a lesson plan.
//   Persona #2 (high-SES parent) reads the same artefact as a trust
//   statement — "Cena is doing real work, here is the receipt".
//
// Scope boundary — BACKEND HTML (this task, PRR-325-backend):
//   This task ships the DATA SHAPE (these DTOs), the pure assembler that
//   folds practice data into one, the HTML renderer that turns the DTO
//   into a self-contained printable document, and a single POST endpoint
//   that glues the three together behind the Premium feature fence.
//
//   Actual PDF rendering (QuestPDF / iText / PdfSharp) is a deliberate
//   FOLLOW-UP and not this task's deliverable. Rationale:
//     1. Adding a PDF NuGet dep is a license-review decision (QuestPDF is
//        dual-license, iText is AGPL above certain usage, PdfSharp's MIT
//        fork ships with quirks) — it shouldn't block the data-shape +
//        endpoint landing.
//     2. The HTML artefact IS already the value for the tutor: they can
//        print-to-PDF from any browser, forward the .html by email, or the
//        SPA can call window.print() for client-side PDF. None of that
//        needs a server-side PDF library.
//     3. When the PDF library decision is made, it renders the SAME
//        HTML via an HTML→PDF pipeline or the SAME DTO via its own layout
//        — nothing in this file's shape changes.
//
//   The scope reduction is explicit so that a reviewer reading this banner
//   knows the missing PDF library is a DELIBERATE deferral, not an omission.
//
// Privacy boundary (ADR-0003):
//   Every field on every DTO below is a summary scalar. There is no
//   session id, no photo reference, no raw LaTeX, no misconception tag
//   keyed to a specific session. "Misconceptions" appears only as an
//   aggregate single-string summary — per ADR-0003 §session-scope,
//   session-specific misconception names never cross this boundary.
//
// Opt-in granularity (DoD):
//   The parent chooses which sections to include via the three Include*
//   booleans on the request. When a flag is false, the corresponding field
//   on the response is null/empty. The assembler enforces this — the
//   renderer does not need to re-check. See TutorHandoffReportAssembler
//   for the policy implementation.
//
// Why here (Cena.Api.Contracts) and not Cena.Actors:
//   Same architectural reason as HouseholdDashboardAggregator — the
//   Contracts assembly already depends on Cena.Actors for enum / state
//   types, so folding wire-format DTOs + the pure assembler that produces
//   them into Contracts dodges the cyclic Actors↔Contracts dependency.
//   The assembler is still a pure function — no I/O, no clock, no DI —
//   which preserves every property that made the "put it next to the
//   domain" location tempting. Locked by unit tests in
//   Cena.Actors.Tests/Parenting/.
//
// Ship-gate memory discipline:
//   Banned-term scan (streak / countdown / scarcity / loss) — none of the
//   field names or banner comments below reference gamification loops.
//   The report is informational only; it does not motivate via urgency.
// =============================================================================

namespace Cena.Api.Contracts.Parenting;

/// <summary>
/// Per-skill mastery change over the report window. Non-PII by design —
/// skill code is a curriculum identifier ("ALG.LINEAR.EQN"), not a
/// student-specific tag. Values are mastery probabilities in [0, 1].
/// </summary>
/// <param name="SkillCode">Curriculum skill identifier (stable across students).</param>
/// <param name="PriorProbability">Mastery probability at <see cref="TutorHandoffReportDto.WindowStart"/>.</param>
/// <param name="PosteriorProbability">Mastery probability at <see cref="TutorHandoffReportDto.WindowEnd"/>.</param>
/// <param name="DisplayLabel">
/// Human-readable skill label in the report locale (e.g. "Linear equations"
/// / "משוואות לינאריות"). Never null — the assembler falls back to
/// <see cref="SkillCode"/> when no localized label is available.
/// </param>
public sealed record MasteryDelta(
    string SkillCode,
    double PriorProbability,
    double PosteriorProbability,
    string DisplayLabel);

/// <summary>
/// Request body for <c>POST /api/me/tutor-handoff-report</c>. The three
/// <c>Include*</c> flags realise the DoD's "parent-opt-in granularity"
/// — the parent chooses which sections appear on the handoff before
/// sharing it with an external tutor. When a flag is false, the
/// corresponding field on the response DTO is null / empty.
/// </summary>
/// <param name="StudentSubjectIdEncrypted">
/// Wire-format encrypted student id (ADR-0038). Endpoint verifies this
/// student is linked to the calling parent's subscription before the
/// assembler runs — IDOR-guarded at the boundary.
/// </param>
/// <param name="WindowStart">
/// Inclusive start of the reporting window (UTC). Defaults to
/// <see cref="WindowEnd"/> − 30 days when omitted by the caller
/// (endpoint normalises before invoking the assembler).
/// </param>
/// <param name="WindowEnd">Exclusive end of the reporting window (UTC).</param>
/// <param name="IncludeMisconceptions">
/// When true, the assembler includes the aggregate misconception summary
/// (ADR-0003 compliant — no session-specific names). When false, the
/// <see cref="TutorHandoffReportDto.MisconceptionSummary"/> field is null.
/// </param>
/// <param name="IncludeTimeOnTask">
/// When true, <see cref="TutorHandoffReportDto.TimeOnTaskMinutes"/> is
/// populated. When false, the field is null (tutor sees no minutes figure).
/// </param>
/// <param name="IncludeMastery">
/// When true, <see cref="TutorHandoffReportDto.MasteryDeltas"/> is populated.
/// When false, the field is an empty dictionary.
/// </param>
/// <param name="Locale">
/// Report locale — one of <c>"he"</c>, <c>"ar"</c>, <c>"en"</c>. Drives
/// direction (RTL for he/ar, LTR for en) in the renderer and the display
/// labels the assembler selects for each skill code.
/// </param>
public sealed record TutorHandoffReportRequestDto(
    string StudentSubjectIdEncrypted,
    DateTimeOffset? WindowStart,
    DateTimeOffset WindowEnd,
    bool IncludeMisconceptions,
    bool IncludeTimeOnTask,
    bool IncludeMastery,
    string Locale);

/// <summary>
/// Tutor-handoff report payload. Produced by
/// <see cref="TutorHandoffReportAssembler.Assemble"/> from a request plus
/// a pre-built per-student aggregate bundle; rendered to a self-contained
/// HTML document by <see cref="ITutorHandoffHtmlRenderer"/>. Every field
/// is a summary scalar — no session ids, no photo refs, no raw transcripts,
/// no untokenized PII.
/// </summary>
/// <param name="StudentSubjectIdEncrypted">
/// The student this report is about (encrypted per ADR-0038). Preserved on
/// the response so the frontend can deep-link back to the student card.
/// </param>
/// <param name="GeneratedAtUtc">Report generation timestamp (UTC).</param>
/// <param name="WindowStart">Inclusive window start (UTC); matches request after default-fill.</param>
/// <param name="WindowEnd">Exclusive window end (UTC); matches request.</param>
/// <param name="Locale">Final locale used for labels + direction.</param>
/// <param name="StudentDisplayName">
/// Student's display name, honouring visibility veto (ADR-0041). Null/empty
/// when the student opted out of name display on parent surfaces.
/// </param>
/// <param name="TopicsPracticed">
/// Human-readable topic list practiced in the window (locale-specific
/// labels). Order is PRESERVED from the input — the assembler never
/// reorders because display order carries meaning (recency / emphasis).
/// </param>
/// <param name="MasteryDeltas">
/// Per-skill prior→posterior mastery deltas. Keyed by SkillCode so the
/// renderer can render a stable table. Empty when
/// <see cref="TutorHandoffReportRequestDto.IncludeMastery"/> is false.
/// </param>
/// <param name="TimeOnTaskMinutes">
/// Total minutes on task in the window. Null when
/// <see cref="TutorHandoffReportRequestDto.IncludeTimeOnTask"/> is false.
/// Long (not int) because a 30-day window × 10 students × heavy usage
/// could exceed int.MaxValue minutes in a pathological institute rollup
/// (fine for tutor-single-student, but the type is the honest upper bound).
/// </param>
/// <param name="MisconceptionSummary">
/// Aggregate misconception summary — one short paragraph describing the
/// patterns the student hit most often in the window. Null when
/// <see cref="TutorHandoffReportRequestDto.IncludeMisconceptions"/> is
/// false. NEVER a list of session-specific misconception tags
/// (ADR-0003 §session-scope); always a single aggregate string.
/// </param>
/// <param name="RecommendedFocusAreas">
/// Locale-specific suggestions the tutor can act on
/// ("Strengthen factoring before next lesson"). Empty list when no
/// recommendations are available from the source data.
/// </param>
public sealed record TutorHandoffReportDto(
    string StudentSubjectIdEncrypted,
    DateTimeOffset GeneratedAtUtc,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    string Locale,
    string? StudentDisplayName,
    IReadOnlyList<string> TopicsPracticed,
    IReadOnlyDictionary<string, MasteryDelta> MasteryDeltas,
    long? TimeOnTaskMinutes,
    string? MisconceptionSummary,
    IReadOnlyList<string> RecommendedFocusAreas);

/// <summary>
/// Pre-built per-student aggregate bundle the endpoint supplies to the
/// assembler. Every field is a summary scalar, matching the output DTO's
/// privacy boundary. The endpoint builds this from its read-model seams
/// (mastery store, minutes projection, exam target retention etc.) and
/// the assembler folds it with the request into the final DTO.
/// </summary>
/// <param name="StudentDisplayName">Student name honouring visibility veto; null when opted out.</param>
/// <param name="TopicsPracticed">Topics practiced in the window (locale-specific labels).</param>
/// <param name="MasteryDeltas">Per-skill mastery deltas (SkillCode → MasteryDelta).</param>
/// <param name="TimeOnTaskMinutes">Minutes on task in the window.</param>
/// <param name="MisconceptionSummary">Aggregate misconception summary (single string, no tags).</param>
/// <param name="RecommendedFocusAreas">Locale-specific focus recommendations.</param>
public sealed record TutorHandoffCards(
    string? StudentDisplayName,
    IReadOnlyList<string> TopicsPracticed,
    IReadOnlyDictionary<string, MasteryDelta> MasteryDeltas,
    long TimeOnTaskMinutes,
    string? MisconceptionSummary,
    IReadOnlyList<string> RecommendedFocusAreas);
