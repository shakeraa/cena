---
persona: ministry
subject: MULTI-TARGET-EXAM-PLAN-001
date: 2026-04-21
verdict: red
---

## Summary

The multi-target exam plan is pedagogically sound, but the Ministry-facing data model in section 3 is wrong in two load-bearing places and incomplete in a third. Shipping it as drafted would produce a catalog that cannot be reconciled against the Israeli Ministry of Education's (משרד החינוך) canonical identifiers — which means every downstream artefact that claims Ministry alignment (rubric DSL PRR-033, coverage matrix PRR-072, parent/teacher reports, any future הערכה בית-ספרית integration) will be building on sand. The fixes are cheap if done now, expensive after ADR-0049 locks. Red until fixed; then easily green.

Two independent issues drive the red verdict: (1) `BAGRUT_MATH`/`5U` is a display label, not the Ministry's canonical code — the Ministry uses a 6-digit numeric subject-unit code (`035806` / `035807` / `035581` / `035582` for Math 5U across Hebrew-stream question papers and שאלונים). Without the numeric code we cannot round-trip anything back to the Ministry. (2) Bagrut sittings are not "dates" to the Ministry — they are **named mo'adim** with tishrei-calendar labels (`קיץ תשפ"ו מועד א'`, `חורף תשפ"ז מועד ב'`, `מועד ג'` for IDF-absentee makeup). The student picking a date collapses that taxonomy and breaks reporting.

The magen (מגן) question is a separate shipgate concern and the reporting-flows question is a v2 problem — both are flagged below.

## Section 9.6 answers

**Q1 — `BAGRUT_MATH` + `5U` vs Ministry canonical code.** No, it does not match. The Ministry's canonical identifier is a **6-digit numeric שאלון (question-paper) code** assigned per subject × unit-count × year-cohort × track-variant. Mathematics 5 yehidot is split across three שאלונים (`581`, `582`, `583` — the Ministry prepends `035` for the subject-family prefix, giving `035581` etc.), not a single "5U" code. 4 yehidot = `804`/`805`/`806`. 3 yehidot = `801`/`802`. Physics 5U = `036004`/`036005`/`036006`. English = `016xxx` family. What you need in the catalog: `ministryQuestionPaperCodes: string[]` per `(ExamCode, Track)` tuple, plus `ministrySubjectCode` (4-digit, e.g. `0358` = math, `0360` = physics) as a stable parent. `BAGRUT_MATH`/`5U` stays as the internal/display identifier; it is **not** the Ministry-facing identifier. Without this, PRR-033's rubric-DSL version pin has nothing to pin against.

**Q2 — Sitting windows complete and correctly labelled?** No. Missing:
- מועד א' (primary sitting — summer for most subjects, winter for some retakes) vs מועד ב' (retake, typically 6–8 weeks after מועד א') vs מועד ג'/מועד מיוחד (IDF, reserves, illness-justified makeup — permitted by a school committee per Ministry regulation).
- Winter/summer is a **subject-dependent pair**, not a universal one. Math has both summer + winter אpassings per year in recent cohorts; Physics is effectively summer-only with a מועד ב'. The catalog table in section 4 that shows "Jan, Jun, Aug" for math conflates מועד ב' with a separate sitting — it's the same academic cycle, not three independent dates.
- The student must pick a **named mo'ed**, not a date. The date is derived from the mo'ed × academic year. Shape: `Sitting { year: "תשפ"ו" | "תשפ"ז", season: "קיץ" | "חורף", moed: "א" | "ב" | "ג", derivedDateUtc: DateTimeOffset }`. The UI can show the date; the stored identifier is the tuple. A free-date picker is a compliance bug.

**Q3 — Syllabus reference version pinning.** Resolves partially. The Ministry publishes subject-level תוכנית לימודים (curriculum) and per-שאלון מיקוד (focused content for the current year) annually, typically in Elul (end-Aug/early-Sep) ahead of the academic year. Occasionally the מיקוד updates mid-year (e.g. COVID-era relief lists). The catalog's `syllabusReferenceId` must key on `(ministrySubjectCode, academicYear, miqudRevision)` — **three dimensions, not one**. Owner: this is a content-ops call, not engineering; absent a content lead, Shaker + the bagrut-fidelity sub-agent own it by default. PRR-033's "version pinned on every item" DoD must be tightened to include the מיקוד revision, not just the curriculum year.

**Q4 — מגן / magen grade.** Out of scope for v1, in scope to **defend against scope creep**. Magen is the school-assigned portion (typically 30%, occasionally 50% for specific configurations) that the school computes and submits to the Ministry; Cena is not the school. Capturing it in `StudentPlan` would imply Cena is the source of truth, which is wrong and legally fraught. If anything, we may want a read-only `selfReportedMagen?: number` field later so the student sees an accurate composite score estimate — but that is v2 and must be clearly labelled "your school's number, not ours". For v1: do not capture.

**Q5 — School-to-Ministry reporting flows (what our data shape needs).** If a school ever exports Cena-student-progress to Ministry (unlikely short-term, probable medium-term once the educator admin panel lands), the minimum viable shape is: `{ studentMinistryId, ministryQuestionPaperCode, sittingTuple, schoolInstitutionCode }`. We store **none** of `studentMinistryId` or `schoolInstitutionCode` today and should not start — they are high-sensitivity PII. The compliant path is: Cena holds an opaque `studentId`, the school's SIS holds the Ministry ID, the school maps on export. Our data shape must therefore (a) key Bagrut progress on `ministryQuestionPaperCode` (Q1), (b) key sittings on the mo'ed tuple (Q2), and (c) expose a clean export endpoint where the school joins the two IDs. If we key on internal-only labels, schools cannot produce a compliant export — and that is the enterprise-tenant deal-breaker.

## Additional findings

1. **ExamCode enum design smell.** `PSYCHOMETRY` is called PET by the test's owner (המרכז הארצי לבחינות ולהערכה / NITE). Section 4's comment "PSYCHOMETRY | Psychometric Entrance Test (PET)" is fine as a label, but the enum value should be `PET` to match the owner's canonical name — it is not a Ministry exam. Mixing MoE-scoped and NITE-scoped exam codes in one flat enum conflates two different regulators.
2. **SAT has zero Ministry relevance** — this is correct and should stay that way. No magen, no syllabus pin, no reporting. Mentioning SAT under "Ministry-lens" review would be overreach.
3. **Mizug/integration with ministry-issued calendar.** The Ministry publishes the Bagrut sittings calendar in PDF annually (`לוח בחינות בגרות תשפ"ז`). Hand-transcribing into the catalog creates drift. Either scrape + validate, or pin a human-verified snapshot per academic year as a JSON artefact under `config/ministry/bagrut-calendar-<year>.json`. The catalog endpoint (PRR-211 proposed) reads that artefact.
4. **Bagrut-reference-only memory interaction.** The memory rule "Ministry exams are reference material; student-facing items are AI-authored CAS-gated recreations" means our **item bank never shows a real Bagrut question**, but it still has to teach toward the same שאלון. The catalog's `ministryQuestionPaperCode` is therefore a **rubric target**, not a content source. PRR-072's coverage matrix must be `(ministryQuestionPaperCode × syllabusCell) → (ourItem[])`. Keep this boundary bright in ADR-0049.

## Section 10 positions

1. SAT + PET v1 — no Ministry opinion; handled by content/finops/enterprise lenses.
2. Per-target free-text note — Ministry-neutral, but **reject** from the Ministry angle if any reporting flow surfaces it to the school: free-text is a DPIA (Data Protection Impact Assessment) landmine.
3. Target cap — Ministry-neutral. 5 is fine (3 Bagrut + PET + retake).
4. "Not sure yet, skip" — Ministry-neutral; teacher/enterprise lens owns.
5. Classroom-assigned targets — v2. When it arrives, `source: student|classroom|tenant` must also carry `ministryInstitutionCode` on classroom/tenant-sourced targets so reports are school-traceable.
6. Parent visibility — Ministry-neutral (parent-lens owns); but adult-age (18+) post-army flag must exist before any school-reporting flow, because Ministry stops treating them as minors.

## Recommended new PRR tasks

- **PRR-217 (P0, pre-ADR-0049)**: Canonical Ministry code mapping. Add `ministrySubjectCode`, `ministryQuestionPaperCodes[]`, `sittingTuple` to the catalog schema. Includes a human-verified JSON artefact for תשפ"ו + תשפ"ז sittings. Depends on nothing. Blocks PRR-033, PRR-072, ADR-0049.
- **PRR-218 (P1, pre-launch)**: Bagrut calendar artefact review cadence. Annual process (Elul) + mid-year מיקוד-update hook. Owner: content lead. Lightweight — a runbook entry plus a CI check that the current artefact is not stale (> 14 months).
- **PRR-219 (P2, post-launch)**: School-export endpoint shape. Read-only endpoint that emits `(studentOpaqueId, ministryQuestionPaperCode, progressMetrics, sittingTuple)`; school SIS joins with Ministry ID on their side. Explicit DPA note: we never store the Ministry ID.
- **PRR-220 (P2, post-launch, defensive)**: Magen-field non-capture lint. A test or doc assertion that `StudentPlan`/`ExamTarget` contains no `magen`, `bagrutGrade`, `finalGrade` fields. Stops scope creep from later sub-agents who "helpfully" add them.
- **Annotate existing**: PRR-033's DoD "version pinned on every item" tightens to include `miqudRevision`. PRR-016's freeze window tightens to cover the full מועד א' + מועד ב' sitting arc (not just exam day — the Ministry's grade-submission window runs ~6 weeks post-sitting and we must not break integrations mid-submission).

## Blockers / non-negotiables

- **BLOCKER (red)**: ADR-0049 cannot lock with `ExamCode` as the only Bagrut identifier. Must carry canonical Ministry codes. Fix before the ADR is drafted, not after.
- **BLOCKER (red)**: Sitting deadline stored as bare `DateTimeOffset` is non-compliant with Ministry taxonomy. Must be a `Sitting` tuple (year × season × mo'ed) with the date derived. Fix in section 3 before ADR.
- **Non-negotiable**: No student-facing Bagrut item is a real Ministry question (Bagrut reference-only memory). This must be explicitly restated in ADR-0049 so content-engineering for SAT/PET does not accidentally set a different policy for Bagrut.
- **Non-negotiable**: No `magen`, `schoolGrade`, `ministryStudentId`, or `institutionCode` fields on `StudentPlan` in v1. Ship-gate lint (PRR-220).

## Questions back to decision-holder

1. Who owns the annual Bagrut calendar + מיקוד refresh (PRR-218)? Shaker-default is fine, but we need the name in the ADR so it does not silently become "the sub-agent's problem".
2. Do we want to carry `ministryQuestionPaperCodes` as an array per (exam, track), or model each שאלון as a separate target? The array is simpler and matches how students think ("I'm doing 5 yehidot math"). The per-שאלון model is more faithful to Ministry reporting. Recommend array + derived list; confirm.
3. Is the target population "Israeli Ministry of Education only" for Bagrut v1, or should the catalog also model the Arab-stream (המגזר הערבי) שאלון variants (different numeric codes, same subject)? If yes, PRR-217 must cover both streams from day one.
4. Timing: can PRR-217 land before ADR-0049 drafting starts, or do we draft the ADR with a placeholder and patch? (Strong recommendation: land PRR-217 first; ADRs with placeholder identifiers tend to ossify the placeholder.)
