---
persona: privacy
subject: STUDENT-INPUT-MODALITIES-001
date: 2026-04-21
verdict: red
---

## Summary

Verdict **red**. Q1 (photo-of-paper-work) and Q3 (freeform text) as drafted punch through three locked privacy invariants — PPL Amendment 13 (biometric), ADR-0003 (session-scope), and PRR-022 (no PII in LLM prompts) — and the brief does not resolve any of them. Q2 (hide-then-reveal) is privacy-neutral and fine. Every mitigation below is mandatory before Q1 or Q3 scope can be opened. "Broad framing" for Q1 is a blocker-on-sight. CSAM quarantine retention is unspecified and is probably the single most legally exposed unknown in the whole brief.

## Section 7.9 answers

**Q1 photo retention + biometric under PPL Amendment 13.** Handwriting is biometric-adjacent but is **not** "biometric data" under PPL Amendment 13 as enacted (Jan 2025), which tracks the statutory list of fingerprint/face/iris/voiceprint/palmprint/gait used for *automated identification*. Handwriting of a math solution is not being used to identify the student — it's being used to diagnose a misconception. So PPL Amendment 13's heightened biometric regime is not triggered. GDPR Art. 9(1) is the tighter question: "biometric data for the purpose of uniquely identifying a natural person" (Recital 51 clarifies — only when used for ID). Same answer: not Art. 9 special-category data as used. **However**, the photo is still personal data under PPL §7 and GDPR Art. 4(1) (identifiable-via-content: handwriting style, margin doodles, name on paper, school name on worksheet header). So default Art. 5(1)(e) storage-limitation applies. Retention must be **≤30 days, session-scoped, never on student profile** — identical to ADR-0003's misconception rule. The vision-model *output* (extracted misconception) inherits ADR-0003. The raw photo binary must be deleted at session-end, not at 30 days — there is no product reason to keep the raw image once the misconception vector is derived. Recommend **raw photo TTL = 24h or end-of-session, whichever first**; misconception extract TTL = 30d per ADR-0003.

**Q1 photo in LLM vision-model call — PRR-022 applies.** PRR-022 was written for text prompts; the brief ducks whether it covers vision inputs. It must. A photo of paper can contain: student name written on page, school crest/header, address in a header, sibling's homework visible on the back, other students' faces if the shot is wide. Every one of these is a PII leak to the vision-model vendor. Requirements: (i) pre-vision crop/mask pipeline that zeros out the top 10% header strip and any detected faces/text-outside-worksheet before the vision call; (ii) vendor contract must be DPA-covered + zero-retention + no-training — if the vendor retains for abuse-monitoring >24h, the vendor is disqualified; (iii) PRR-022 lint rule extended to a new `VisionPromptSafetyGate` that runs OCR-layer-0 PII detection (names, addresses, phone, email) and blocks the vision call if the confidence is above threshold. This is a **new task** (see PRR-251 below), not a scope-extension note.

**Q3 freeform text — trauma/family scrub before LLM.** Yes, mandatory, and the existing PRR-022 scrubber is insufficient. A 200-char ExamTarget note was bad; a 500-1000 word Literature/History essay is dramatically worse. A teen writing about "my grandfather in the Holocaust" for Tanakh analysis, or "my mother's illness" in a Civics essay on healthcare policy, is writing genuinely sensitive content that is pedagogically *correct* to write. We cannot strip it without destroying the assignment. The model answer is **two-tier storage**: (a) raw student text stored in student-scoped encrypted store, NOT sent to LLM grader verbatim; (b) LLM grader receives a *rubric-aligned abstraction* — structured features extracted locally (word-count, thesis-sentence, citation-count, argument-structure). LLM scores the abstraction, not the essay. This is consistent with PRR-022's "no PII in prompts" principle applied seriously. If product insists on whole-essay grading, the prompt must pass through a named-entity + health/family/trauma scrubber with refusal-on-high-confidence behavior. Cheap scrubber will not catch Hebrew/Arabic NER reliably — this is a multi-language problem ADR-0022 has never solved.

**RTBF cascade (PRR-223) — does it cover photo + vision output?** No, not as scoped. PRR-003a covers event store; PRR-003b covers crypto-shred; PRR-223 covers the cascade into derived stores. Raw photo binaries live in blob storage (not the event store), and vision-model responses may be cached in the LLM-cache layer (PRR-233 prompt-cache). Both are outside PRR-223's current scope. **Must add**: (i) blob-storage photo lifecycle hook that RTBF-cascades on student-erase; (ii) LLM response-cache invalidation on student-erase, keyed by studentId. This is PRR-252 below. If we ship Q1 without this, GDPR Art. 17 + PPL §13F erasure requests will leave photo binaries orphaned in S3/GCS — a data-breach-in-waiting.

**Q1 CSAM-moderation retention — the unknown that should stop the room.** Autoresearch iteration-04 mandates CSAM moderation. What happens to a *flagged* image? Three operational realities the brief dodges: (1) under Israeli law, suspected CSAM must be reported to the police cyber unit (Lahav 433) and retained per their chain-of-custody requirements — typically **years**, not 30 days; (2) under US law (if any vendor is US-resident), 18 U.S.C. §2258A requires NCMEC reporting and vendor-side retention (90 days default, extendable to 1 year); (3) the moderation vendor may retain flagged content for model-training — an independently unacceptable outcome. None of this is compatible with ADR-0003's 30-day session-scope. The correct design: flagged images exit the Cena retention regime entirely on first positive classification, enter a legal-hold quarantine that is **named, documented, DPO-owned, and excluded from the student's RTBF scope** (RTBF cannot override a legal-hold; GDPR Art. 17(3)(b) explicitly exempts legal obligation). The brief must name this quarantine, its retention horizon, its owner, and the disclosure obligation to the student/parent. This is the single biggest omission — call it Blocker-1.

## Consent surface — first-time photo-upload

Students <18: explicit first-time modal, parent-consent-verified per COPPA/EPIC-PRR-C (<13) or student-informed-consent with parent-notification (13-17 per PRR-052). Copy must name (a) photo goes to third-party vision vendor, (b) retained ≤24h raw / 30d as misconception, (c) CSAM-scanned with law-enforcement-disclosure possibility, (d) student can decline and still use text-entry path. No dark-pattern defaults — "Allow once" should be as prominent as "Allow always."

Students ≥18: single-sentence disclosure banner acceptable; first-time modal preferred for auditability. Same content points, lighter tone. The 18+ consent still must be revocable via RTBF per PRR-003a.

Do **not** ship an implicit-consent-by-action design. Action = tapping upload-button is insufficient under PPL §11 (informed consent) and GDPR Art. 7 (unambiguous, specific, informed) for special-category-adjacent data flowing to a third-party vendor.

## Section 8 positions

**Q1 framing:** **Narrow only.** Broad framing ("ask the tutor about anything") explodes the moderation + prompt-injection + PII surface and is a blocker. Photo-upload must be bound to the open-question context and auto-expired at session end.

**Q3 architecture:** Privacy-preference shared abstraction (per-subject adapter) — one scrubber/allowlist pipeline is auditable; N per-subject pipelines is N audit gaps. No position on non-privacy tradeoffs.

## Recommended new PRR tasks

1. **PRR-250 — CSAM-quarantine retention policy + legal-hold lane.** Define quarantine storage, retention horizon (18 U.S.C. §2258A + Israeli police reqs), RTBF-exemption basis, DPO-owned disclosure workflow. **Blocker for Q1.**
2. **PRR-251 — VisionPromptSafetyGate: pre-vision PII mask + face/header-strip crop.** Extends PRR-022 to vision inputs. Vendor must be zero-retention DPA-bound. **Blocker for Q1.**
3. **PRR-252 — RTBF cascade extension: blob-storage photo lifecycle + LLM-cache invalidation.** Extends PRR-223 + PRR-003b to cover vision pipeline outputs. **Blocker for Q1.**
4. **PRR-253 — Q3 two-tier essay grading: local rubric-feature extraction + scored-abstraction prompts.** Student essay text never reaches LLM verbatim. Fallback path if feasibility fails: named-entity + trauma/family/health scrubber with refusal-on-high-confidence.
5. **PRR-254 — Photo-upload first-time consent modal copy + age-banded flow.** Coordinates with EPIC-PRR-C + PRR-052. Required copy points enumerated above.
6. **PRR-255 — Raw photo TTL = end-of-session / 24h hard cap.** Extends DataRetentionPolicy for photo binaries (distinct from misconception extract). Launch blocker for Art. 5(1)(e) compliance.
7. **PRR-256 — Multi-language PII/trauma scrubber for Hebrew + Arabic NER.** Extends ADR-0022. Without this, Q3 freeform in non-English subjects is un-shippable.

## Blockers / non-negotiables

- **Blocker-1:** CSAM-quarantine retention + legal-hold design (PRR-250). Shipping Q1 without a named quarantine policy is a law-enforcement chain-of-custody disaster and a GDPR Art. 5/17 contradiction.
- **Blocker-2:** Vision-model PII scrub pipeline (PRR-251). PRR-022 as written does not cover vision inputs. No Q1 ship without this.
- **Blocker-3:** RTBF cascade extension to blob + LLM-cache (PRR-252). GDPR Art. 17 completeness.
- **Blocker-4:** Raw photo TTL ≤24h (PRR-255). Not "≤30 days."
- **Non-negotiable:** Q1 framing = narrow only. Broad framing is not a product option until the three Blockers land and a second privacy review clears it.
- **Non-negotiable:** Q3 essay text does not reach an LLM verbatim in v1. Either two-tier abstraction (PRR-253) ships, or Q3 freeform-grading slips.
- **Non-negotiable:** First-time consent modal for photo upload, all ages. No implicit-consent-by-action.

## Questions back to decision-holder

1. **Who owns the CSAM-quarantine lane operationally?** DPO + Legal + one engineer on-call is the minimum. If no named owner exists, Q1 cannot ship.
2. **Is there a vision-model vendor already selected with a zero-retention DPA?** If not, vendor selection is on the Q1 critical path.
3. **Q3 humanities Launch-scope:** does product accept MC-only humanities at Launch (degraded pedagogy, acceptable privacy) rather than freeform-with-weak-scrubbing (full pedagogy, unacceptable privacy)? My position: MC-only ships; freeform slips to v1.1 behind PRR-253 + PRR-256.
4. **Tenant override to disable photo entirely (persona-enterprise 7.5):** does this create a consent-basis shift analogous to the school-forced-plan question from multi-target findings? Likely yes — needs a paired disclosure path.
