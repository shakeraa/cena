---
persona: privacy
subject: MULTI-TARGET-EXAM-PLAN-001
date: 2026-04-21
verdict: yellow
---

## Summary

Verdict **yellow**, leaning red on two axes: the free-text per-target note and the "retained indefinitely" archive claim. The core data model (ExamCode + Track + Deadline + WeeklyHours) is defensible under data-minimization — these are thin, structured, non-identifying, and operationally necessary. The three privacy hazards are (1) the ≤200-char free-text note, (2) the "retained indefinitely" archive carve-out that quietly bypasses our RTBF/retention discipline, and (3) sensitive-inference creep when `SAT` + locale + grade-band are co-held on the same aggregate. Each has a concrete mitigation below. None are full launch-blockers if fixed in EPIC-PRR-F; all become blockers if shipped as drafted in section 8.

## Section 9.9 answers

**9.9.a — "Planning SAT" + locale as sensitive inference.** Yes, exploitable, and yes, it needs consent framing — but not a modal. Combining `SAT` ExamCode with `tenant=Israeli-school` or `locale=he-IL` is a measurable signal of diaspora/immigration intent (kids prepping SAT from Tel Aviv are statistically a self-selected cohort). Under Israeli PPL Amendment 13 (in force Jan 2025) this is not "sensitive information" per the statute's narrow list (health, sexual orientation, political, religious, criminal), so it does not trigger the heightened lawful-basis requirement. However, PPL's purpose-limitation and data-minimization principles do apply, and the Privacy Protection Authority's 2025 guidance treats **inferred immigration intent** as a derived special category when the inference is deterministic enough to act on. Under GDPR Art. 9 same answer: not special-category data per se (Recital 51 is narrow), but Art. 5(1)(b) purpose-limitation binds us from using this inference for anything beyond "help the student pass SAT." Required controls: (i) no export of `{ExamCode, locale, tenant}` tuples to analytics without k-anonymity floor per PRR-026; (ii) no profile-scoring field derived from ExamCode distribution; (iii) no marketing/retargeting use — ever. A consent surface is not strictly required for collection, but the privacy policy (PRR-123) must name SAT targeting as a disclosed purpose limited to in-product personalization.

**9.9.b — "Retained indefinitely" archive.** Section 8's claim that archived targets are "not misconception data, so not ADR-0003" is technically correct and operationally wrong. ADR-0003 governs misconception data specifically; it does not give us a positive license to retain everything else forever. ExamTarget archive history is declared-plan data and is **not** operationally necessary after the exam sitting has passed — the scheduler doesn't need it, the coverage matrix doesn't need it, the student doesn't need "I studied for Bagrut Math in 2026" three years later. Default retention should be **24 months post-ArchivedAt**, with user-initiated extension if they want transcript-style history. "Indefinite" has to be justified against a purpose, and no purpose has been stated. A "forget my plan history" control is already subsumed by PRR-003a RTBF; the question is whether the default is retain-forever or purge-at-24mo. Recommend default purge.

**9.9.c — Per-target free-text note (≤200 chars).** Drop it. This is the single highest-risk field in the whole epic. 200 chars is exactly the length where real teens will write "retaking because my dad was sick last year," "I failed first time because of anxiety meds," "need to finish before my mom kicks me out," or identify siblings/schools/teachers by name. None of that belongs in our event store, none of it survives PII scrub cleanly, and none of it is operationally useful to the scheduler or the CAS oracle. If the product argument is "helps the student remember why they set this goal," offer a structured enum instead: `{retake, first-attempt, score-improvement, schedule-change}` — four tags, zero free text, zero PII surface. The ADR-0022 scrubber is a mitigation, not a license; the data-minimization default is "don't collect what you can't safely use."

**9.9.d — Parent dashboard visibility (EPIC-PRR-C).** Three age bands, three defaults:
- **Under-13**: visible to parent by default. COPPA/ADR Parent aggregate governs. Student cannot hide.
- **13–17**: **hidden by default, student-toggle to share**. ADR-0003/Israeli PPL student-agency principle + EPIC-PRR-C's 13+/16+ age-band split (PRR-052) already establishes student-controlled visibility at 13+. Exam plan is personal academic planning, not a grade — parent doesn't have an intrinsic right to it.
- **18+ (post-army/adult)**: hidden by default, student-toggle to share. "Post-army" is not a legal threshold we should encode — 18 is the line. If the adult opts in, the parent link is a student-grant, not a default.

Do not implement any "share by default at 13, hide at 16" middle tier — it will leak for the 13–15 cohort who didn't know they had to opt out. Hide-by-default for everyone 13+ is the honest default.

## Additional findings

**LLM-prompt leakage (section 8 + PRR-022).** Section 8 says the free-text note "must not echo without PII scrub" when surfaced in LLM prompts. This is backwards. PRR-022 establishes: **no PII in prompts, period.** The scrubber is a last-mile safety net, not a primary control. Hint-generation should reference the ExamTarget by *structured fields only* — `{ExamCode, Track, weeks-to-deadline-bucketed}`. Never `{Deadline.ExactDate}` (absolute dates narrow identification), never the free-text note (see 9.9.c), never `WeeklyHours` (another fingerprinting vector). Bucket weeks-to-deadline into `{<2wk, 2-6wk, 6-12wk, >12wk}` for prompt context. If the note field survives the review (it shouldn't), it must be explicitly denylisted in the prompt-assembly lint rule from PRR-022.

**RTBF + downstream derivations (PRR-003a/b).** ExamTarget events *can* be crypto-shredded via the same pathway. The problem is the derivation chain: coverage matrix (PRR-072) is per-target, scheduler state includes `ActiveExamTargetId`, mastery state is per-target from the moment section 6 lands. After shredding `ExamTarget*` events, those derived stores still hold `ExamTargetId` as a foreign key. Two options: (i) cascade the shred into coverage/mastery derived tables (expensive, touches the scheduler's hot path), or (ii) register every downstream derivation with the RetentionWorker (PRR-015) so shred propagates via the same worker pattern already established for misconception data. Option (ii) is consistent with the existing architecture. **Mandatory before launch**: PRR-015 must be extended to cover coverage-matrix + mastery-state + scheduler-state as ExamTarget-linked stores.

**k-anonymity floor (PRR-026) on exam-plan aggregates.** Any teacher/tenant-facing aggregate that reports exam-plan distribution ("34% of grade-11s are targeting Bagrut 5U") must enforce k≥10. A tenant of 12 students reporting "2 are targeting SAT" is de-anonymizing those two by diaspora intent. This is a direct extension of PRR-026, not a new control — but the scope note needs to explicitly name ExamCode distribution as a covered aggregate.

**Catalog endpoint unauthenticated.** Section 9.7 asks. Red-team 9.8 asks. If the catalog is unauthenticated, fine — it's a public list of exams. If it carries tenant-specific overrides (section 4 hints at future tenant variance), those MUST be authenticated. Don't leak tenant-specific exam lists to unauthenticated requests.

## Section 10 positions

**Q2 (free-text note):** **Drop.** Replace with a 4-value structured enum. No PII surface, no scrubber dependency, still carries the product intent.

**Q6 (parent visibility):** **Hide-by-default for 13+, visible for <13, student-grants for 18+.** Do not ship a share-by-default mode at any age ≥13.

## Recommended new PRR tasks

1. **PRR-217 — ExamTarget archive retention policy: 24-month default purge + user-extendable.** Extends DataRetentionPolicy.cs to cover ExamTarget archive. Blocks section-8 "retained indefinitely" claim. Owner: retention-worker lane.
2. **PRR-218 — ExamTarget RTBF cascade into coverage matrix + mastery state + scheduler state.** Extends PRR-015 + PRR-003a/b to cover ExamTarget-linked derivations. Launch blocker for RTBF completeness.
3. **PRR-219 — Ban free-text in ExamTarget; replace with structured `ReasonTag` enum.** If the user overrides this position, downgrades to "PII scrub + lint-rule enforcement on the note field, tested with adversarial teen-written inputs."
4. **PRR-220 — Parent visibility default for ExamTarget: age-banded per EPIC-PRR-C, hide-by-default 13+.** Coordinates with PRR-052.
5. **PRR-221 — LLM-prompt field allowlist for ExamTarget context.** Extends PRR-022 lint rule with the explicit allowlist `{ExamCode, Track, weeksToDeadlineBucket}` and deny everything else. Blocks hint-generation from leaking Deadline/WeeklyHours/Note.
6. **PRR-222 — k-anonymity floor extension: ExamCode distribution in teacher/tenant aggregates must satisfy PRR-026 k≥10.** Scope annotation on PRR-026, not a fresh axis.
7. **PRR-223 — Privacy-policy update (coord PRR-123): disclose SAT/PET targeting as in-product-only purpose.** Israeli PPL purpose-limitation compliance.

## Blockers / non-negotiables

- **Blocker-1:** Section 8's "retained indefinitely" language for archived targets must be replaced with a concrete retention period before ADR-0049 lands. Indefinite retention without a stated purpose violates both Israeli PPL purpose-limitation and GDPR Art. 5(1)(e). *Fix via PRR-217.*
- **Blocker-2:** RTBF derivation cascade must be designed before ExamTarget events ship. If we ship events then retrofit the cascade, we have data that cannot be shredded for weeks/months post-launch — a GDPR Art. 17 violation window. *Fix via PRR-218.*
- **Blocker-3 (soft):** The free-text note ships either dropped or with a structured enum replacement. Shipping it with scrub-only mitigation is a privacy regression we will pay for when the first real teen writes trauma into it. *Fix via PRR-219.*
- **Non-negotiable:** No ExamCode + tenant + locale tuple in any analytics export below k=10. *Covered by PRR-026 + PRR-222.*
- **Non-negotiable:** No ExamTarget field beyond `{ExamCode, Track, weeksToDeadlineBucket}` in LLM prompts. *Covered by PRR-022 + PRR-221.*

## Questions back to decision-holder

1. **Is there any product scenario where a student needs to see their own pre-2024 archived targets?** If no: 24-month purge is correct. If yes: what's the real retention horizon and its purpose?
2. **Is the free-text note load-bearing for any feature beyond the "I remember why I set this" UX affordance?** If it feeds the LLM, the scheduler, or any analytics: tell me now, because that changes the mitigation calculus from "drop it" to "structure it tightly."
3. **Parent-dashboard visibility at 18+ — do we treat post-18 students as adult-consent from signup, or is there a transition window where a minor-era parent link persists?** EPIC-PRR-C's 16+ tier needs alignment. My position is clean-break at 18, no legacy parent visibility.
4. **Does the tenant-admin-forced-plan capability (persona-enterprise 9.5) change the consent basis for collection?** A school-forced ExamTarget is no longer freely-given student data; it shifts the lawful basis under GDPR from consent/legitimate-interest to contract-with-the-school. The privacy-policy disclosure needs to cover both paths if this ships v1.
