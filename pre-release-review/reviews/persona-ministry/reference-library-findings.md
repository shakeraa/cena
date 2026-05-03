---
persona: ministry
subject: ADR-0059
date: 2026-04-28
verdict: yellow
reviewer: claude-subagent-persona-ministry
---

## Summary

ADR-0059 carves the right surface in roughly the right place:

- `Reference<T>` is a distinct epistemic wrapper from `Deliverable<T>`, isolating reference-browse from delivery-for-assessment by construction.
- Answer affordances are stripped on the reference render path (no textbox, no submit, no "did I get this right?").
- Every reference render is consent-gated (§3) and SIEM-audited under a pinned `EventId(8009)`.
- Practice always routes through a CAS-verified variant — there is no path where a raw Ministry item is graded as if it were an authored item.

From a Ministry-of-Education compliance angle that is materially better than what every Israeli prep platform on the market currently does. Bagrut Plus, Bagrut Tikshoret, מוסדות-issued חוברות בגרות, and school-portal PDFs all surface raw past papers with no consent flow, no provenance citation, and no architectural separation between "view past paper" and "answer past paper for grading". The carve-out is defensible on those grounds.

It is **yellow, not green**, for four reasons the ADR itself flags but does not close:

1. **Legal-delta memo missing**. PRR-242's compliance memo addressed backend ingestion, not in-app display. The two surfaces are legally distinct under both the 2007 Israeli Copyright Law (חוק זכות יוצרים תשס״ח-2007) and the Ministry's published terms-of-use on edu.gov.il. ADR-0059 itself acknowledges this in §Risks but does not block on it.

2. **Corpus not ingested in dev**. Per PRR-250 §2, `mt_doc_bagrutcorpusitemdocument` does not exist in the dev Postgres. Every claim about §3 consent flow, §4 filter scope, §5 variant lineage, and §6 citation rendering is moot until ingestion runs. The arch-test invariants in §1 (`BagrutRecreationOnlyTest` positive-list extension) cannot be exercised against a real fixture.

3. **Citation form is not Ministry-canonical**. The ADR's example citation copy in §6 reads "Bagrut Math 5U, שאלון 035582 q3 (קיץ תשפ״ד)". This drops the מועד. A canonical Ministry sitting reference is `קיץ תשפ״ד מועד א׳` (or מועד ב׳ / מועד ג׳ / מועד מיוחד) — without the מועד the citation is ambiguous (a single academic-year-season carries up to four mo'adim). My prior multi-target-exam-plan review (2026-04-21) flagged this exact ambiguity at the aggregate level; the citation must inherit the same canonical form.

4. **"Ministry posture" risk is hand-waved**. §Consequences calls this "reputational risk". It is more concretely a takedown / cease-and-desist surface, and the ADR does not describe what happens operationally if a Ministry letter arrives. No runbook, no kill-switch procedure, no audit-preservation flow specified.

The required fixes (§Required mitigations below) are cheap if done now and expensive after PRR-245 ships. Sign-off conditional on the four required mitigations landing before ADR acceptance.

---

## Section Q2 prompt answers

### Q2.a — In-app display vs PRR-242 backend-ingestion legal posture: are these legally distinct surfaces, and what additional posture is required?

**Yes, they are legally distinct.** PRR-242's compliance memo (the file referenced in the task DoD as `docs/legal/bagrut-corpus-usage.md`) addressed reference-as-input-to-an-AI-pipeline — an **internal use** of Ministry-published material to anchor recreations. That posture rests on:

- The Ministry publishes past Bagrut papers on edu.gov.il under terms permitting **reference and study use** by students and educators.
- Israeli state copyright duration is **50 years from publication** for state-authored works (§42 of the 2007 Israeli Copyright Law / חוק זכות יוצרים תשס״ח-2007). Bagrut papers from 2015→present are still well within that window — they are **not** in the public domain.
- Ministry-published material under the standard terms-of-use on edu.gov.il permits non-commercial, attributed reference. Cena's commercial nature (paid subscriptions) puts pressure on the "non-commercial" half of that posture, which the PRR-242 memo did not stress-test.

**In-app display to authenticated paying students** changes three things that the PRR-242 memo did not address:

1. **Public reproduction vs internal reproduction**. PRR-242 stored corpus text on Cena infrastructure and fed it to an LLM — arguably an internal-use reproduction. Rendering it on a student's screen inside Cena's UI is a **public reproduction** to a defined audience (Cena's user base). Under Israeli copyright law that requires either an explicit license or a defensible fair-use claim under §19 of the same statute (שימוש הוגן). Fair use under Israeli law is a balancing test, not a categorical exemption — and the Ministry has not litigated this against Israeli prep platforms publicly, which means precedent is thin.

2. **Commercial advantage**. Cena charges. Surfacing Ministry papers as a "feature" — a thing the student gets in exchange for a paid subscription — is materially harder to defend as fair use than a free study site. The §19 balancing test weighs commercial purpose against educational purpose; "we are an educational product that charges money" lands somewhere in the middle and depends on framing.

3. **Derivative works / variants**. Variants generated from Ministry-corpus source items are arguably derivative works under §16 of the Israeli Copyright Law. A "parametric variant" with the numbers swapped but the structure preserved is closer to a derivative work; a "structural variant" with the scenario rewritten is closer to fair-use educational re-creation. The ADR does not distinguish the legal posture between the two tiers in §5, and it should — they are not equivalent under Israeli copyright analysis.

**What's required**: a delta memo from a qualified Israeli copyright/IP lawyer (not the same memo as PRR-242, and not written by an engineer or PM) that specifically addresses (a) public-display-to-paying-subscribers, (b) parametric vs structural variant derivative-work analysis, (c) Ministry terms-of-use commercial-use clause review, (d) takedown response posture. Until that memo lands the reference page must remain feature-flagged off and the variant endpoint must remain admin-only.

### Q2.b — Ministry attitude toward third-party prep tools surfacing past papers: known case-law or precedent?

**No reported litigation that I can cite with confidence.** What I can say with confidence:

- **Bagrut Plus** (bagrutplus.co.il) — surfaces past Bagrut papers as core product feature. Long-running. Has not been publicly reported as the subject of a Ministry takedown or copyright suit. **Prior art, not legal cover.**
- **Bagrut Tikshoret** — private tutoring brand with online materials including past papers. Same posture. Same caveat.
- **School-portal PDFs** (בית-ספר portals, Mashov-attached resources, classroom-issued חוברות) — surface past papers routinely under what amounts to an implicit institutional fair-use posture. Cena is not a school and cannot rely on that posture by analogy.
- **Geva, מי-יודע, etc.** — tutoring centers with paper banks. Same prior-art status.

The **absence of public litigation** against any of these is not a license — it reflects the Ministry's historical enforcement style:

- **Light** for surface-level reference use, attribution-respectful presentation, and absence of grading-authority claims.
- **Heavier** for anything that looks like a parallel grading authority or a Bagrut "certification" — platforms that issued their own scores keyed against Bagrut rubrics and presented those scores as predictive have drawn scrutiny.
- **Heaviest** for direct re-publication of Ministry materials without attribution or in a way that competes with Ministry-issued materials directly (e.g. a paid mock-exam product that uses verbatim Ministry text and brands it as the platform's own work).

ADR-0059's strict §2 (no answer affordances, no grading on reference items) lands on the right side of the "parallel grading authority" line by construction. ADR-0059 §6 (provenance citation on the answer screen) addresses the "laundering Ministry material as our own" line — but the citation must be **non-defeatable** in the UI: not a tooltip the student can dismiss, not a footer that scrolls off-screen, not a settings-toggleable preference. The architectural rule should be: a variant cannot render without its provenance chip in the same DOM frame, enforced at the contract layer (`Reference<T>` factory, citation-required attribute on variant DTO).

**What's required**: a documented **takedown response runbook** that names the legal contact, the on-call SRE, and the steps to:

(a) feature-flag-off the reference library globally within 30 minutes of receiving a Ministry letter,
(b) preserve audit logs (`EventId(8009, "BagrutReferenceBrowsed")` events + variant lineage records) for legal review without continuing to serve,
(c) communicate to affected students with a non-alarming UX message,
(d) execute a per-source-paper-code variant purge if the takedown is scoped to specific שאלונים rather than the whole library.

The runbook is cheap to write, expensive to improvise under pressure.

### Q2.c — Does the mitigation set materially reduce Ministry-posture risk, or is it cosmetic?

**Material, not cosmetic — but with two caveats.**

The four-part mitigation set (consent + provenance citation + no-grading-on-reference + variant-routed practice) is a stronger posture than any Israeli prep platform I am aware of. Element-by-element:

- **Consent disclosure (§3)** — this is the closest thing to a "we know this is Ministry material, we are not your grading authority" affidavit on the student side. The consent record is event-sourced (`BagrutReferenceConsentGranted_V1`) and crypto-shreddable per ADR-0042's RTBF cascade. **No competing platform does this.** It will be load-bearing in any Ministry conversation: "the user explicitly acknowledged the source and the non-grading nature before any reference render".

- **Provenance citation (§6)** — cited inline on the answer screen with the שאלון code is materially stronger than the "courtesy of Ministry of Education" footer most competitors use, when they bother to include one. The labels-match-data rule from project memory aligns here: the citation must describe what the variant *is* (a derivative of a specific Ministry item) not what we wish it were.

- **No grading on reference items (§2)** — the architectural separation between `Reference<T>` and `Deliverable<T>` is the most legally-defensible part of the design. The compile-time, runtime, and architecture-test layers from ADR-0043 are reused here as a positive-list extension. It says by construction: we are not pretending to be a grading authority on Ministry items. The Ministry's strongest historical objection has been to platforms that imply parity with Ministry assessment; this rules it out by construction, not by policy.

- **Variant-routed practice (§5)** — parametric variants are closer to derivative works than structural variants under §16 analysis. The parametric/structural split is *useful* for cost and pedagogy but **does not insulate the parametric tier from copyright pressure** — it may actually concentrate it. A swapped-numbers parametric variant is more obviously derived from the source than a rewritten-scenario structural variant. From a copyright-defense standpoint the structural tier is the safer harbor; the parametric tier is closer to the line. The ADR should not treat the two as legally equivalent in §Risks.

**Caveat 1 — consent cache invalidation under legal-posture change**:
The consent-token is a 90-day cache. If a student grants consent and then the legal posture changes mid-cache (Ministry letter arrives, lawyer revises memo), there is no global revocation pathway specified in §3. The token is bound to the student, not to the active legal posture or the active feature-flag state. The runbook in Q2.b must include a **feature-flag kill-switch that bypasses the consent cache** — a flag-off state must hard-stop reference rendering even for consent-token-holding students, not just refuse new consent grants.

**Caveat 2 — consent copy elides the variant-grading distinction**:
§3's consent copy says "Cena does not grade your answers on these — they're for context". That is true for the *reference* item but *not* true for the *variant* — the variant goes through the session-answer pipeline (§6) and updates BKT mastery on `(studentId, skillId)`. The consent copy must not blur the line. Required revision (see mitigation 5):

> "These are Ministry-published past Bagrut questions, included as reference material — Cena does not grade your answers on these. To practice the underlying skill, tap **Practice a variant** to get a CAS-verified recreation. Variants ARE graded and update your mastery."

Without this revision the consent is misleading and arguably defective — a privacy/regulatory authority asked "what did the user agree to?" would receive an answer that does not match what the system actually does.

### Q2.d — Is the catalog mapping sufficient for student-facing reference-paper navigation?

**Sufficient for navigation, insufficient for citation form, and one downstream gap.**

The mapping established by ADR-0050 + PRR-243 — `BAGRUT_MATH + Track=5U + QuestionPaperCodes=[035581, 035582, 035583]` — is the right shape for filtering the reference library to the student's active plan. A Grade-12 student doing 5U Math sees only the three relevant שאלונים; a student who unchecked שאלון 035583 in PRR-243 onboarding sees only the other two. PRR-250 §3 confirms `ExamTarget.QuestionPaperCodes` is shipped, retention pipeline exists, RTBF cascade is wired. The filter source-of-truth is single and stable. **This part is fine.**

The ADR-0059 §6 example citation copy is **not** Ministry-canonical:

> "Variant of Bagrut Math 5U, שאלון 035582 q3 (קיץ תשפ״ד) — numbers changed."

The Ministry-canonical sitting form is `קיץ תשפ״ד מועד א׳` (or מועד ב׳ / מועד ג׳ / מועד מיוחד). The ADR's example drops the מועד. PRR-250 §2 confirms the corpus document carries `Moed` as a separate string field — the data is there, the citation just needs to render it. This is a one-line fix in the citation template, but it must land before ship: a citation that says "קיץ תשפ״ד" without the מועד is technically ambiguous (a single academic-year-season has up to four mo'adim) and from a Ministry-reporting-alignment standpoint that ambiguity is exactly the bug ADR-0050 §3 was written to prevent at the aggregate level. The citation must match the persistence canonical form.

**The downstream gap**: ADR-0059 does not specify whether the reference-library filter respects ADR-0050 §1's `PerPaperSittingOverride` map. A student who is taking שאלון 035581 in קיץ תשפ״ה but שאלונים 035582+035583 in קיץ תשפ״ו will see reference items from both years — fine, the corpus is multi-year — but the *recommended ordering* of reference items should plausibly weight items from sittings adjacent to the student's target sitting. The ADR is silent on this. For Ministry-posture this is not a blocker (it is pedagogy), but for student trust in the reference library as "aligned to my plan" it matters.

**On `BAGRUT_MATH + 5U` vs `סמל שנה שאלון`**: my prior multi-target-exam-plan review (2026-04-21) flagged that the canonical Ministry identifier is the 6-digit שאלון code, not the display label. ADR-0050 §2 resolved that correctly — שאלון codes are the catalog primary key, display labels are localized metadata. ADR-0059 inherits that correctly via `ExamTarget.QuestionPaperCodes`. There is no need for a `סמל שנה שאלון` (institution-code × year × paper) abstraction at the *reference-library* layer — that abstraction is for school-to-Ministry export reporting (which is post-Launch per my prior PRR-219 recommendation), and the reference library is a student-facing surface, not a reporting surface. **This is fine; do not over-engineer it.**

---

## Additional findings

1. **`Provenance.Source` is free-text in ADR-0043**. The runtime gate logs `Provenance.Source` as part of the SIEM event under `EventId(8008)`. ADR-0059's `Reference<T>` reuses the same `Provenance` record and emits `EventId(8009)` with the same `Source` field. If `Source` is an arbitrary string at the call site, the audit log becomes unstructured and queries by שאלון become impossible without log-line regex. Ministry-corpus citations should populate `Source` with a structured slash-delimited form, e.g. `"ministry-bagrut/035582/2024/summer/A/q3"`. A free-text "math 5u summer 2024" will break audit-by-paper-code, which is exactly the query the takedown response runbook needs to execute under time pressure.

2. **Variant lineage retention**. ADR-0059 §5 says persisted variants carry source provenance. ADR-0050 §6 sets retention bounds for declared-plan data. ADR-0003 sets retention for misconception data (30 days, session-scoped). **Variant lineage retention is unspecified.** If a variant persists for years and its source Ministry item was later determined to be inadmissible (Ministry letter, lawyer revision), the variant's lineage tag is now a permanent record of derivation from a contested source. Recommendation: variant lineage records should fall under the ADR-0038 RTBF cascade and have an explicit retention policy (suggest 24 months, matching ADR-0050 §6, with a "purge by source-paper-code" admin tool for takedown response).

3. **De-dup keying enables enumeration**. ADR-0059 §5 says variants are de-duped on `{sourceShailonCode, sourceQuestionIndex, variationKind, parametricSeed?}` and cached across students. This is good for cost. It is also a **redteam surface**: a free-tier user with 3 structural calls/day can enumerate cached variants by spamming `(paperCode, questionNumber)` combinations, effectively browsing the cache. Persona-redteam owns this in §Q2 of the ADR; from a Ministry lens, the concern is that an enumerated cache could be scraped and republished, putting Cena in the position of being the source of a Ministry-derived dataset on someone else's site. The rate-limit floor for *cache reads* should be no looser than the rate-limit for *cache writes*.

4. **Arab-stream parity (PRR-239)**. ADR-0050 Q1 resolved both Hebrew-stream and Arab-stream שאלון variants at Launch. ADR-0059 §4 references `ExamTarget.QuestionPaperCodes` without specifying Arab-stream coverage. The corpus document at `BagrutCorpusItemDocument` carries a `Stream` enum per PRR-250 §2 (Hebrew, Arab, etc). The reference library must filter on `Stream` matching the student's active target. Today the ADR does not specify this — it implicitly assumes the שאלון code is unique across streams, which it is not (Arab-stream שאלונים have parallel numeric codes per the catalog). One-line fix: §4 must add "filter `Stream` matches the student's `ExamTarget.Stream` (or all streams for freestyle opt-in)".

5. **Hebrew-only at Launch?** The corpus is heavily Hebrew. The consent copy in §3 is specced in en/he/ar. Reference items themselves will render in their original Hebrew (or Arabic for Arab-stream). A student onboarded in English UI but with `ExamTarget.Stream=Hebrew` will see the reference page UI chrome in English and the items in Hebrew. This is correct behavior (the items are what they are) but the consent copy should explicitly say "questions are shown in their original Ministry-published language" so the student is not surprised. Minor copy fix.

6. **"Reputational risk" framing in §Consequences is too soft**. The actual risk profile is:

   - Ministry letter arrives (formal letter from משרד החינוך legal/IP counsel, not a tweet).
   - Cease-and-desist with a typical 14–30 day response window.
   - Either we have a takedown runbook and feature-flag-off cleanly within 24 hours and respond with our legal-delta memo on file, or we improvise.
   - If we improvise: a Ynet / Calcalist / TheMarker / Globes story runs about a startup serving paid Bagrut content without authorization. The story does not have to be accurate to be damaging.
   - Secondary effects: school enterprise contracts (PRR-244 institutional plans) become harder to close while the story is fresh; the brand association with "Ministry dispute" persists in search results.

   The ADR's "reputational risk" framing under-describes this. Per the "Honest not complimentary" memory: name it. **It is a regulatory/legal risk with reputational secondary effects, not the other way around.** The legal-delta memo + takedown runbook (mitigations 1+2) are the entire defense surface.

7. **Bagrut-reference-only memory interaction is mostly preserved, with one carve-out worth naming**. The 2026-04-15 decision in project memory is "Ministry exams are reference material; student-facing items are AI-authored CAS-gated recreations, never raw Ministry text". ADR-0059 §1's `Reference<T>` wrapper is, on its face, a *narrow* exception: raw Ministry text can reach a student-facing surface, but only inside `Reference<T>`, only with consent, only without answer affordances, and only as a setup for variant practice. This is the correct scope of the exception. **ADR-0059 should restate this explicitly** — the memory entry currently reads as an absolute ban, and a future contributor reading the memory without reading ADR-0059 could reasonably believe the carve-out is forbidden. Suggest the memory entry get a one-line update referencing ADR-0059 as the authoritative carve-out, and the ADR §Decision preamble explicitly say "ADR-0043's ban applies to all surfaces *except* the `Reference<T>`-wrapped browse path defined here".

8. **PRR-242 task body's legal posture clause is thin**. PRR-242 §Scope item 7 reads: "Copyright/licensing review: Ministry papers are publicly published under usage terms permitting reference use; document compliance path. No direct reproduction to students." This is two sentences. The DoD line-item "Legal memo reviewed" does not specify by whom, against what jurisdiction, or what standard. From a Ministry-posture audit perspective, the PRR-242 legal memo (whatever it ended up being) is the *only* artifact between Cena and a Ministry letter today. ADR-0059 inherits PRR-242's legal posture by reference but does not validate it. **Recommend PRR-242's legal memo gets a re-read by qualified IP counsel as part of mitigation 1**, not as a separate task — and that the re-read explicitly addresses display-to-students, not just ingestion.

---

## Required mitigations (blockers for ADR acceptance)

1. **Legal-delta memo for in-app display, written by a qualified Israeli copyright/IP lawyer**, specifically addressing: (a) public-display-to-paying-subscribers under §19 fair-use balancing, (b) parametric vs structural variant derivative-work analysis under §16, (c) Ministry edu.gov.il terms-of-use commercial-use clause review, (d) recommended attribution form. Memo lives at `docs/legal/bagrut-reference-display-delta.md` and is referenced from ADR-0059 §Risks. Until this memo lands the feature flag stays default-off and the variant endpoint stays admin-only. **Owner**: Shaker. **Blocks**: ADR acceptance + PRR-245 implementation start.

2. **Takedown response runbook** at `docs/ops/runbooks/ministry-takedown.md` covering: (a) named legal contact + on-call rotation, (b) 30-minute global feature-flag kill-switch procedure (must bypass the §3 90-day consent cache), (c) audit-log preservation for legal review, (d) student communication template, (e) variant-purge procedure keyed on source paper code (links to mitigation 4). **Owner**: SRE + legal. **Blocks**: feature-flag default-on rollout (Q3).

3. **Citation form must be Ministry-canonical**: `שאלון <code>, <מועד tuple>` not free-text. ADR-0059 §6's example citation copy must be revised to render the מועד explicitly (e.g. "Variant of Bagrut Math 5U, שאלון 035582, q3 (קיץ תשפ״ד מועד א׳) — numbers changed."). The data is in `BagrutCorpusItemDocument.Moed` per PRR-250 §2; the rendering template must use it. Also `Provenance.Source` must populate with a structured slash-delimited form for SIEM tractability, not a free-text label. **Owner**: PRR-245 implementer. **Blocks**: variant-creation merge.

4. **Variant lineage retention policy + RTBF cascade**: explicit retention bound on the variant document carrying source-Ministry-corpus lineage (recommend 24 months, matching ADR-0050 §6), and inclusion in the ADR-0038 RTBF cascade with a purge-by-source-paper-code admin tool. Without this, takedown response (mitigation 2) cannot complete in bounded time. **Owner**: PRR-245 implementer + retention pipeline owner. **Blocks**: variant-creation merge.

---

## Recommended mitigations (non-blocking, address before default-on rollout)

Numbered R1–R6 to avoid collision with the required-mitigations list above.

1. **R1 — Consent copy revision** (§3): the disclosure must distinguish reference items (no grading) from variants (graded, affects mastery). Current copy implies the no-grading rule applies broadly; that is misleading for the variant flow. Revise to: "These are Ministry-published past Bagrut questions, included as reference material — Cena does not grade your answers on these. To practice the underlying skill, tap **Practice a variant** to get a CAS-verified recreation. Variants ARE graded and update your mastery."

2. **R2 — Stream-matching filter** (§4): explicit invariant that `Reference<T>` items rendered to a student have `corpus.Stream == student.ExamTarget.Stream` (or unfiltered for freestyle opt-in per §4). One-line addition; prevents cross-stream item leakage when Arab-stream Launch (PRR-239) lands.

3. **R3 — De-dup cache rate-limit floor** (§5): cache reads (which return a previously-generated variant for free) should be subject to the same per-(student, day) rate limit as cache writes. Otherwise free-tier users can enumerate the cache and turn Cena into a scrape target. Persona-redteam owns the deeper analysis; from a Ministry lens, cache enumeration is a pathway to third-party republication of Ministry-derived material, which is exactly the takedown surface required-mitigation 2 protects against.

4. **R4 — `Reference<T>` audit log retention bound**: ADR-0043 §2's `EventId(8008)` SIEM events for delivery-gate violations have an implicit retention policy from the SIEM pipeline. ADR-0059's new `EventId(8009, "BagrutReferenceBrowsed")` events for **non-violation** reference renders need an explicit retention bound — they are higher-volume than 8008 (every reference render emits one) and their privacy posture is different (they record student identity browsing Ministry items). Suggest 90 days, matching the consent token, then aggregate-only.

5. **R5 — Arab-stream + Russian-PET coverage in copy/UI**: confirm the consent copy and reference-page UI render correctly in Arabic and that PET (out of scope today, but architecture-not-Bagrut-coupled per Q4) does not accidentally inherit Bagrut-shaped consent copy if/when surfaced.

6. **R6 — Default-on rollout decision (Q3)**: the conservative two-week feature-flag-off-then-on schedule is correct from a Ministry lens, with one addition — the on-by-default rollout should be **per-tenant gradual** (5% → 25% → 100% over 2 weeks), not flag-flipped globally. A bug in the citation rendering or a Ministry letter arriving mid-rollout is much cheaper to halt at 5% than at 100%.

---

## Interaction with school-export reporting (PRR-219)

My multi-target-exam-plan review (2026-04-21) recommended PRR-219 as a post-Launch read-only school-export endpoint emitting `(studentOpaqueId, ministryQuestionPaperCode, progressMetrics, sittingTuple)`. ADR-0059 introduces a new question that must be answered before PRR-219 lands:

- **Should variant attempts that derive from Ministry-corpus reference items appear in the school export?**

Two readings, both have load-bearing consequences:

1. **Yes, include them.** The school report shows mastery progress against שאלון codes; variants tagged with source provenance feed naturally into that aggregation. Argument: the school cares about the student's progress against the שאלון, not how Cena got there.

2. **No, exclude them.** Including variant-attempt mastery in a school-export labeled "progress against שאלון 035582" implies the student practiced *that שאלון* — they did not, they practiced a CAS-verified variant of it. Argument: labels-match-data; the export must not overstate.

The compromise that aligns with both ADR-0050 and ADR-0059's epistemic separation: school exports must distinguish **direct skill mastery** (computed skill-globally per ADR-0050 §4) from **variant-attempt counts tagged with source-paper-code lineage**. The school sees both, the labels match what each number actually is. PRR-219's design must reflect this when it lands.

This is **out of scope for ADR-0059 acceptance** but in-scope for PRR-219 specification work. Flag for the post-Launch backlog.

## What ADR-0059 gets right (worth preserving on revision)

To be honest-not-complimentary on the positive side too: most of the design is correct and should not be changed during mitigation work.

- **`Reference<T>` as a sibling to `Deliverable<T>`, not a subtype**. Subtyping `Reference<T> : Deliverable<T>` would have leaked Ministry items through the existing delivery gate's polymorphism. Keeping them sibling types with separate factories is the right call.
- **Architecture-test positive-list approach**. Adding a positive-list namespace (`Cena.Api.Contracts.Reference.**`) for permitted Ministry-named fields, with `[ReferenceContext]` attribute proof, is more defensible than a deny-list. Every other namespace stays banned exactly as ADR-0043 specified.
- **Filter scope from `ExamTarget.QuestionPaperCodes`**. Single source of truth for "what does this student see?" — same source as PRR-243 onboarding, same source as scheduler. No second source of truth, no drift surface. This is the result of the multi-target-exam-plan persona reviews resolving Q1 cleanly; ADR-0059 inherits it correctly.
- **Tier-gating for variant generation**. Splitting parametric (cheap, deterministic, $0) from structural (expensive, LLM-authored, ~$0.005-0.015) at the **UI affordance** level forces the student to make an explicit cost-aware choice. Conflating the two would let free-tier users mash the Sonnet button. The cost ceiling defense relies on this discipline holding through implementation.
- **Variant cache de-dup keyed on source provenance**. Cost-amortizing across students who request the same source is the right primitive. The redteam concern (Additional finding #3) is about the *rate-limit posture* on cache reads, not the de-dup itself.
- **Audit event pinned to a stable EventId**. `EventId(8009, "BagrutReferenceBrowsed")` matches the ADR-0043 pattern (`EventId(8008)`) so SIEM pipelines key on the integer, not log-line text. Operationally clean.
- **Post-Launch corpora extension is open**. Q4 explicitly says the architecture must accept any `ProvenanceKind` with a corpus, not Bagrut-coupled. SAT released items / PET sample sections can plug in if licensed.

These are all worth preserving through mitigation work. The required mitigations are deltas, not redesigns.

## Sign-off

**Verdict**: yellow — the ADR is the right shape and a stronger Ministry posture than any Israeli prep platform on the market today, but four required mitigations must land before acceptance: (1) lawyer-reviewed legal-delta memo for in-app display, (2) takedown response runbook with feature-flag kill-switch, (3) Ministry-canonical citation form including מועד, (4) variant lineage retention policy + RTBF cascade keyed on source paper code.

The blocker from PRR-250 §2 (corpus not ingested in dev) is independent of this review's verdict — without corpus the architecture-test invariants in §1 cannot be exercised against a real fixture. That is an implementation prerequisite for PRR-245, not a design defect in ADR-0059. Coordinator should bundle the corpus-ingestion seeding step into PRR-245 acceptance criteria.

If mitigations 1–4 land and corpus seeding is wired, this goes green on a re-review. The cheap path to green: do all four now, before any code lands. The expensive path: ship behind a feature flag, discover the citation form is non-canonical or the legal posture is shaky after the first thousand students have rendered consent, then retrofit. Per "no Phase 1 stub → Phase 1b real" memory, the cheap path is the only path.

— claude-subagent-persona-ministry, 2026-04-28
