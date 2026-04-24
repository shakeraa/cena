# TASK-PRR-325: Tutor-handoff PDF export (Premium differentiator)

**Priority**: P0 — launch-blocker (Premium differentiator per persona #7)
**Effort**: M (1 week)
**Lens consensus**: persona #7 tutor (flips competitor→channel), #2 high-SES (trust artifact)
**Source docs**: [BUSINESS-MODEL-001-pricing-10-persona-review.md](../../docs/design/BUSINESS-MODEL-001-pricing-10-persona-review.md)
**Assignee hint**: backend (PDF generation) + UX
**Tags**: epic=epic-prr-i, parent-ux, premium-differentiator, priority=p0
**Status**: Partial — HTML handoff report + endpoint + assembler + renderer + 37 tests shipped (per-session audit 2026-04-23 confirms existing surface already satisfies DoD modulo PDF library); server-side PDF wrap deferred on license review
**Source**: 10-persona pricing review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-I](EPIC-PRR-I-subscription-pricing-model.md)

---

## Goal

Premium parents can generate a shareable PDF for their student's external tutor: "This month your student practiced X, struggled with Y, mastered Z." Turns the human-tutor segment into a Cena distribution channel.

## Scope

- PDF template with Cena branding, parent-selectable date range (default 30d).
- Contents: topics practiced, accuracy distribution, notable misconceptions (aggregate, no session-specific), mastery deltas, recommended focus areas.
- Parent chooses what to share (opt-in granularity).
- Export format: PDF (downloadable + email-to-tutor option).
- HE/AR/EN.
- Tutor-facing only; not visible to student (avoids surveillance-feeling).

## Files

- `src/backend/Cena.StudentApi/Controllers/TutorReportController.cs`
- PDF template HTML → PDF.
- `src/parent/src/pages/tutor-report.vue`
- Tests.

## Definition of Done

- PDF generates with real data.
- Locale-correct.
- Parent-opt-in granularity respected.
- Full sln green.

## Non-negotiable references

- [ADR-0003](../../docs/adr/0003-misconception-session-scope.md) — aggregate only, no session-specific misconception names.
- Memory "Labels match data".

## Reporting

complete via: standard queue complete.

## Related

- [PRR-320](TASK-PRR-320-parent-dashboard-mvp.md)

## Audit (2026-04-23)

Per-session audit of the existing surface — PRR-325 backend is
already shipped as the HTML form, with server-side PDF wrapping as
a deliberate deferral documented in the code banners.

### Already shipped

- [TutorHandoffReportDto.cs](../../src/api/Cena.Api.Contracts/Parenting/TutorHandoffReportDto.cs)
  — Request + Response DTOs with `IncludeMisconceptions`,
  `IncludeTimeOnTask`, `IncludeMastery` booleans that realise the
  DoD's "parent-opt-in granularity". Window defaults to
  `WindowEnd − 30 days`. `Locale` drives direction (RTL for he/ar,
  LTR for en) in the renderer.
- [TutorHandoffReportAssembler.cs](../../src/api/Cena.Api.Contracts/Parenting/TutorHandoffReportAssembler.cs)
  — Pure assembler folds the card bundle into a single DTO and
  enforces the opt-in booleans at this layer; renderer does not
  re-check.
- [ITutorHandoffHtmlRenderer.cs](../../src/api/Cena.Api.Contracts/Parenting/ITutorHandoffHtmlRenderer.cs)
  — Self-contained HTML renderer (CSS inline, no external deps)
  producing a printable document suitable for browser print-to-PDF
  plus email forwarding.
- [ITutorHandoffCardSource.cs](../../src/api/Cena.Api.Contracts/Parenting/ITutorHandoffCardSource.cs)
  — Card-source port; provider supplies real practice data per the
  Premium parent-dashboard pipeline (PRR-320 upstream).
- [TutorHandoffEndpoints.cs](../../src/api/Cena.Student.Api.Host/Endpoints/TutorHandoffEndpoints.cs)
  — `POST /api/me/tutor-handoff-report`:
  - Auth-guarded; sub claim required.
  - Feature fence via `SkuFeatureAuthorizer.CheckParent(state,
    TierFeature.TutorHandoffPdf)` — Premium-only; 403 with
    `tier_required` + `requiredTier=Premium` on deny so UI upsells.
  - IDOR guard — 403 `student_not_linked` rather than 404 so the
    endpoint is not an id-enumeration oracle.
  - UTF-8 text/html response for Hebrew + Arabic rendering.
- Tests: `src/actors/Cena.Actors.Tests/Parenting/` —
  `TutorHandoffReportAssemblerTests` plus `TutorHandoffHtmlRendererTests`
  = 37 tests covering opt-in granularity, locale direction,
  misconception aggregate-only (ADR-0003), feature fence, IDOR branch.
  All pass.
- ADR-0003 compliance locked: every wire field is a summary scalar;
  `MisconceptionSummary` is aggregate-only; no session ids, no photo
  references, no session-specific misconception names cross the
  boundary.
- Ship-gate discipline: informational framing, no streak / countdown /
  scarcity / loss-aversion in banner or field names.

### What is deferred (license-review gate)

- **Server-side PDF output.** The literal DoD asks for PDF; today the
  endpoint returns `text/html`. The DTO file banner documents this
  deferral explicitly: adding a PDF NuGet dep (QuestPDF dual-license,
  iText AGPL above usage thresholds, PdfSharp MIT-fork quirks) is a
  license-review decision that should not block the data-shape +
  endpoint landing. The HTML artefact IS already the value — a tutor
  can print-to-PDF from any browser, forward the `.html` by email, or
  the SPA can call `window.print()` for client-side PDF. When the
  library decision is made, it renders the same HTML via an HTML→PDF
  pipeline or the same DTO via its own layout API without touching
  this endpoint.
- **Parent Vue `tutor-report.vue` page** — calls the endpoint with
  opt-in checkboxes, renders returned HTML in a preview iframe plus
  download link. Frontend work; endpoint contract and DTOs frozen.
- **Email-to-tutor button** — posts the HTML to the transactional-
  email pipeline; independent follow-up (touches PRR-345 email suite).

Closing as **Partial** per memory "Honest not complimentary": every
backend DoD item is green modulo the specific "PDF file-type" output
format; the HTML form satisfies the tutor-handoff business purpose
today and the PDF wrap is one NuGet-package decision away when ops
greenlights the license terms.
