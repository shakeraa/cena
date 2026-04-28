# TASK-PRR-245: Reference library + student-side variant generation

**Priority**: P1 — depends on ADR-0059 acceptance + legal-delta memo (see Blocking)
**Effort**: L (3-4 weeks across backend + frontend + ADR + persona reviews)
**Lens consensus**: pending persona reviews (privacy, ministry, cogsci, finops, redteam, a11y) — see ADR-0059 §Q2
**Source docs**: [ADR-0059 (proposed)](../../docs/adr/0059-bagrut-reference-browse-and-variant-generation.md), [ADR-0043](../../docs/adr/0043-bagrut-reference-only-enforcement.md), [ADR-0050](../../docs/adr/0050-multi-target-student-exam-plan.md), [PRR-242 (done)](done/TASK-PRR-242-past-bagrut-corpus-ingestion.md), [PRR-243 (done)](done/TASK-PRR-243-bagrut-question-paper-multi-pick.md)
**Assignee hint**: claude-code (coordinator) for ADR sign-off; backend implementation eligible for kimi-coder; frontend by claude sub-agent under main session
**Tags**: source=user-directive-2026-04-28, epic=epic-prr-n, priority=p1, ui, backend, content, adr-extension
**Status**: **Blocked** on ADR-0059 Q1 (PRR-249 legal memo) + Q2 (PRR-248 6-persona review) + PRR-250 verification sweep
**Tier**: launch-adjacent (decision: ship default-off behind feature flag at Launch per ADR-0059 §Q3)
**Epic**: [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md) (Reference library + variants umbrella, created 2026-04-28)

---

## Goal

Activate the past-Bagrut corpus (PRR-242) on the student surface as a reference-browse library. Students can browse the שאלונים aligned to their `ExamTarget.QuestionPaperCodes`, see the Ministry-published source as context, and one-tap into a CAS-verified variant for actual practice. This closes the "students go to the Ministry website anyway" leak with a structured handoff and amortizes PRR-242's $3-5k corpus investment across the student surface.

Per the user 2026-04-28: *"there should be a separate browse syllabus / past papers reference page that deep-links to base materials that we ingest. recreate with different parameters."*

## Scope

### Backend

1. **`Reference<T>` wrapper** at `src/actors/Cena.Actors/Content/Reference.cs` per ADR-0059 §1. Sibling to `Deliverable<T>`. Factory enforces `MinistryBagrut` provenance + valid consent token; emits `EventId(8009, "BagrutReferenceBrowsed")` audit log.
2. **Reference catalog endpoints** at `src/api/Cena.Student.Api.Host/Endpoints/ReferenceLibraryEndpoints.cs` (new):
   - `GET /api/v1/reference/papers` — list שאלונים filtered by active `ExamTarget.QuestionPaperCodes`. Returns paper metadata only (year, moed, topic summary), no question bodies.
   - `GET /api/v1/reference/papers/{paperCode}/{year}/{moed}` — full paper with all questions wrapped in `Reference<QuestionDto>`.
   - `GET /api/v1/reference/papers/{paperCode}/{year}/{moed}/q/{questionNumber}` — single-question detail.
   - All endpoints require valid `BagrutReferenceConsentGranted_V1` consent token (90-day TTL).
3. **Consent endpoints**:
   - `POST /api/v1/reference/consent` — student grants reference-browse consent; emits `BagrutReferenceConsentGranted_V1` event; returns `ConsentTokenId`.
   - `DELETE /api/v1/reference/consent` — student revokes; archives token, RTBF-cascade compatible.
4. **Variant generation endpoint** at `src/api/Cena.Student.Api.Host/Endpoints/ReferenceVariantEndpoints.cs` (new):
   - `POST /api/v1/reference/papers/{paperCode}/q/{questionNumber}/variant` with `body: { variationKind: "parametric" | "structural" }`.
   - Parametric path: deterministic parameter substitution + SymPy verify (no LLM); persists variant via `CasGatedQuestionPersister` keyed on `{sourceShailonCode, questionIndex, parametricSeed}` for de-dup.
   - Structural path: wraps `GenerateSimilarHandler.HandleAsync` (currently admin-only) with student auth + rate-limit; persists similarly with `variationKind: "structural"`.
   - Returns `{variantQuestionId, sessionRouteHint}`. Client navigates to single-question session.
5. **Rate-limit decorator** integration with `RateLimitedEndpoint`. Defaults per ADR-0059 §5; per-student-day limits configurable via `IInstitutePricingResolver` (PRR-244).
6. **Provenance lineage on persisted variants**: extend `QuestionDocument` with `SourceProvenance: Provenance?` + `SourceShailonCode: string?` + `SourceQuestionIndex: int?` + `VariationKind: "parametric" | "structural"?`. Backfill: nullable, defaults to existing items being null.
7. **Architecture test extension** at `src/actors/Cena.Actors.Tests/Architecture/BagrutRecreationOnlyTest.cs`:
   - Add positive-list namespace `Cena.Api.Contracts.Reference.**` where Ministry-named fields are permitted iff the DTO declares `[ReferenceContext]` attribute.
   - Existing negative-list for `Sessions/Challenges/Tutor/Me/Hub` unchanged.
   - New test: every endpoint returning `Reference<T>` in `ReferenceLibraryEndpoints.cs` must call the `Reference<T>.From` factory (not direct construction) — enforced via Roslyn analyzer or `Reference<T>` having a non-public constructor.
8. **Audit counter projection**: `BagrutReferenceItemRendered_V1` event aggregates into a per-(student, paper) counter for finops + persona-cogsci telemetry (variant-cadence analysis).

### Frontend

9. **New route** `/reference/library` in student SPA (`src/student/full-version/src/pages/reference/library.vue`). Gated by:
   - Student has ≥1 Bagrut `ExamTarget` (per ADR-0059 §4), OR
   - Student opted-in via `/settings/reference-library` (freestyle path, lower rate-limit).
10. **Consent disclosure card** as Vuetify dialog on first visit (or after token expiry). Copy in en/he/ar. `<bdi dir="ltr">` around שאלון codes per math-always-LTR rule. Confirms via `POST /reference/consent` + caches token client-side.
11. **Reference paper browser**:
    - Sidebar: list of student's papers (from `GET /reference/papers`) grouped by שאלון code.
    - Main pane: paper-level navigation (year + moed pickers) → question-level pages.
    - Each question renders **without any input affordance** (no answer box, no submit, no MCQ radios). One CTA: "**Practice a variant of this question**" with two sub-options: *"Same problem, different numbers"* (parametric) vs *"Similar problem, different scenario"* (structural; gated for free tier).
12. **Variant-tier picker** with cost transparency: "Free" badge on parametric, "Premium" badge on structural for free-tier students. Free-tier users tapping structural see an upgrade nudge — copy must comply with ADR-0048 (no countdown / loss-aversion).
13. **Provenance citation chip** on the variant-practice answer screen: *"Variant of Bagrut Math 5U, שאלון 035582 q3 (קיץ תשפ״ד) — numbers changed."* Honors labels-match-data rule.
14. **Settings page** `/settings/reference-library` for freestyle opt-in + consent revoke.

### Tests

15. Unit tests: `Reference<T>.From` invariants (provenance kind, consent expiry, factory throws on misuse).
16. Endpoint integration tests:
    - GET filters correctly by student's active `QuestionPaperCodes`; non-Bagrut targets get empty list.
    - Consent token enforcement (401 without, 401 on expired).
    - Rate-limit enforcement at boundary (last allowed call returns 200, next returns 429).
17. Variant generation tests:
    - Parametric path: deterministic seed produces same variant on repeat call (de-dup hits).
    - Structural path: cached after first generation; second student requesting same source gets cached variant (idempotent).
    - Both paths persist with full provenance lineage.
18. Architecture tests:
    - `BagrutRecreationOnlyTest` extended for the positive-list carve-out + `[ReferenceContext]` attribute requirement.
    - New test: any DTO in `Reference.**` namespace MUST be wrapped in `Reference<T>` at the endpoint, never returned bare.
19. E2E test: student onboarding → grants consent → browses reference paper → generates parametric variant → practices → answer screen shows provenance citation. RTL + LTR flows.
20. Negative E2E: student without Bagrut target → reference page redirects to settings opt-in. Student with revoked consent → re-prompted on next access.

## Files

### New
- `docs/adr/0059-bagrut-reference-browse-and-variant-generation.md` (drafted in this task)
- `tasks/pre-release-review/EPIC-PRR-H-reference-library.md`
- `src/actors/Cena.Actors/Content/Reference.cs`
- `src/actors/Cena.Actors/Content/ReferenceContextAttribute.cs`
- `src/actors/Cena.Actors/Events/ReferenceLibraryEvents.cs` (`BagrutReferenceConsentGranted_V1`, `BagrutReferenceConsentRevoked_V1`, `BagrutReferenceItemRendered_V1`, `ReferenceVariantGenerated_V1`)
- `src/api/Cena.Student.Api.Host/Endpoints/ReferenceLibraryEndpoints.cs`
- `src/api/Cena.Student.Api.Host/Endpoints/ReferenceVariantEndpoints.cs`
- `src/api/Cena.Api.Contracts/Reference/` (new namespace — DTOs + `[ReferenceContext]` markings)
- `src/student/full-version/src/pages/reference/library.vue`
- `src/student/full-version/src/pages/reference/paper.vue`
- `src/student/full-version/src/pages/settings/reference-library.vue`
- `src/student/full-version/src/components/reference/ConsentDisclosureDialog.vue`
- `src/student/full-version/src/components/reference/QuestionReferenceView.vue` (no-input renderer)
- `src/student/full-version/src/components/reference/VariantTierPicker.vue`
- `docs/legal/bagrut-corpus-display-delta.md` (delta to PRR-242's legal memo — see Q1)

### Modified
- `src/actors/Cena.Actors.Tests/Architecture/BagrutRecreationOnlyTest.cs` — positive-list carve-out
- `src/api/Cena.Admin.Api/Questions/GenerateSimilarHandler.cs` — extract a `GenerateSimilarCore` callable from both admin and student endpoints (no behavior change for admin)
- `src/shared/Cena.Infrastructure/Documents/QuestionDocument.cs` — add `SourceProvenance`, `SourceShailonCode`, `SourceQuestionIndex`, `VariationKind` fields (nullable)
- `src/student/full-version/src/plugins/i18n/locales/{en,he,ar}.json` — consent disclosure copy

## Definition of Done

- All 6 ADR-0059 invariants enforced by code, test, or arch-test layer.
- Persona reviews (privacy, ministry, cogsci, finops, redteam, a11y) filed under `pre-release-review/reviews/persona-*/reference-library-findings.md`. Any red verdicts addressed before merge.
- Legal-delta memo (`docs/legal/bagrut-corpus-display-delta.md`) signed off by Shaker before feature flag flips on for any tenant.
- Full `Cena.Actors.sln` builds cleanly; full E2E suite green (per memory "Full sln build gate").
- Shipgate scanner v2 passes — no countdown / streak / days-until copy in any of the new files.
- Variant cost ceiling validated against PRR-244 pricing resolver: a free-tier student cannot exceed configured monthly variant spend.
- Reference page ships behind feature flag `reference_library_enabled` per tenant; default-off at Launch per ADR-0059 §Q3.

## Blocking

- ADR-0059 acceptance (currently Proposed; awaits Shaker sign-off + persona reviews).
- ADR-0059 Q1: legal-delta memo confirming display-to-students is within PRR-242's licensing posture.
- ADR-0059 Q2: 6 persona findings filed and resolved.

Implementation MUST NOT START until both blockers clear. Coordinator (claude-code) tracks via this task; do not auto-claim.

## Non-negotiable references

- [ADR-0059](../../docs/adr/0059-bagrut-reference-browse-and-variant-generation.md) (this task implements it)
- [ADR-0043](../../docs/adr/0043-bagrut-reference-only-enforcement.md) (carve-out, not violation)
- [ADR-0050](../../docs/adr/0050-multi-target-student-exam-plan.md) (filter source = `ExamTarget.QuestionPaperCodes`)
- [ADR-0026](../../docs/adr/0026-llm-three-tier-routing.md) (variant cost tiers)
- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md) (banned UX patterns)
- [ADR-0042](../../docs/adr/0042-consent-aggregate-bounded-context.md) (consent event sourcing + RTBF)
- Memory "Bagrut reference-only" — load-bearing
- Memory "No stubs — production grade"
- Memory "Math always LTR"
- Memory "Real browser E2E with diagnostics" (E2E tests must be real Chrome with console capture)
- Memory "Full sln build gate"

## Reporting

complete via: `node .agentdb/kimi-queue.js complete <id> --worker <you> --result "<branch + ADR-0059 sha + persona-review folder>"`

If blocked on persona findings or legal memo: `fail` with reason and the blocking artifact path.

## Related

- ADR-0059 (this task's foundation; draft pending review)
- ADR-0043 (carve-out source)
- ADR-0050 + PRR-243 (filter source — `ExamTarget.QuestionPaperCodes`)
- PRR-242 (corpus source — done)
- PRR-244 (pricing resolver for rate-limit overrides — done)
- [EPIC-PRR-N](EPIC-PRR-N-reference-library-and-variants.md) (this task + PRR-248 / PRR-249 / PRR-250 are children)

---

## Coordinator notes (claude-code, 2026-04-28)

- Task drafted under senior-architect role per user directive. ADR-0059 + this task body are the artifacts to circulate for persona review BEFORE any code lands.
- The `Reference<T>` carve-out from ADR-0043 is the load-bearing design decision; if persona-ministry returns a red verdict on in-app display, fall back to metadata-only mode (citation + topic + structure description, no raw question text). This fallback preserves the variant-generation surface while neutralizing legal/ministry risk. Document the fallback in ADR-0059 §Open Questions Q1 resolution.
- The variant-generation endpoint is the ONE place we expose `GenerateSimilarHandler` to students. Any future student-facing AI generation should follow this rate-limit + tier-gate pattern, not bypass it.
