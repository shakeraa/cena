---
persona: privacy
subject: STUDENT-INPUT-MODALITIES-002
date: 2026-04-22
verdict: red
supersedes_scope: null
extends: student-input-modalities-findings.md
---

## Summary

Verdict **red**, carried forward from 001. Q2 hide-reveal is privacy-neutral except one subtlety in §3.3 (server-side redaction projection is itself a data-minimization control — good — but introduces a new cache surface the RTBF cascade does not cover). Q3 writing-pad-plus-HWR is where the new exposure lives: **stroke dynamics are behavioural-biometric-adjacent, HWR-via-LLM-vision reuses the Q1 vision pipeline surface, and ≥one of the three HWR procurement options (§4.6) is disqualified on vendor-retention grounds before UX gets a vote.** The 001 blockers (PRR-250/251/252/255/256) remain open and now gate Q3 writing-pad as well as Q1 photo. Four new PRR tasks below.

## Section 6.9 answers

**Stroke dynamics — biometric-adjacent under PPL Amendment 13?** Handwriting kinematics (pressure, velocity, stroke-order, pen-lift timing, dwell-angle, tremor signature) are a well-established behavioural biometric in the literature — the ISO/IEC 19794-7 "signature/sign time series" standard exists for exactly this, and Israel's National Cyber Directorate writeup on PPL Amendment 13 cites "behaviour-based identification" as an in-scope modality when used to identify. The legal test is the same as Q1: **is the data being used for automated identification of a natural person?** For diagnosing a math misconception, no — same answer as Q1 handwriting-content. For "is this really the enrolled student submitting" (proctoring / exam-fidelity anti-cheat), yes, and that is a different product surface we do not run today. **Conclusion**: stroke dynamics are not Art. 9 GDPR special-category or PPL §7A biometric *as used* in Q3 writing-pad pedagogy, BUT they are demonstrably re-identifiable (Plamondon 2014, Sae-Bae 2014: a 30-second handwriting sample reaches >95% identification accuracy on a cohort of 100). Under GDPR Art. 4(1) and PPL §7, that makes them personal data with a **heightened re-identification risk** — meaning Art. 5(1)(c) data-minimization and Art. 32 appropriate-safeguards bite harder than for plain text input. Practical impact: **do not persist raw stroke time-series beyond the HWR call**. Store the HWR-emitted symbolic answer (`"x = 3"`) and an optional low-resolution rasterized thumbnail for student review; discard velocity/pressure/timing arrays. ADR-0003 session-scope inherited; same 30-day derived-output cap as misconception data. If we ever turn stroke dynamics into a proctoring signal, that is a new consent basis, new DPIA, new ADR — do not let it slip in as a telemetry add-on. Flag for a future "no proctoring-via-biometrics" ADR (see Blocker-5).

**HWR-via-LLM-vision — same VisionPromptSafetyGate as Q1, or a separate path?** Same gate. Same code path. Call it the same name. §4.6 option 3 ("Claude Sonnet vision on an image of the stroke") uses the identical MSP pipeline built for Q1 photo-of-paper-work, and any attempt to route it around `VisionPromptSafetyGate` on the grounds that "hand-drawn content is simpler than uploaded photos" (brief §4.6) is wrong — the surface is the same: arbitrary vendor-side retention, arbitrary vendor-side model-training, arbitrary prompt-injection smuggled as written text inside the canvas, and arbitrary PII if the student writes their name on the pad (which they will, habitually — 11 years of paper-tops-have-names is muscle memory). The three gates from PRR-251 (DPA zero-retention, pre-vision PII mask, canary-token structured-schema output) apply unchanged. **What IS different**: the canvas is server-cropped to the drawing area by construction (no ambient background, no sibling's homework on the back, no worksheet header), so the face/header-strip crop component is a no-op. Keep it as a no-op defense-in-depth, not as a removed stage.

**MyScript / iink.js (§4.6 option 1) — client-side HWR.** Privacy-positive: strokes never leave the device. Data-minimization paradise. Licensing note is not a privacy concern. **This should be the preferred option on privacy grounds**, with LLM-vision as fallback only for content the client HWR fails on. Mathpix (§4.6 option 2) is **disqualified until vendor-side retention audited** — their public DPA has historically logged raw images for 30 days for abuse review, which is incompatible with ADR-0003 session-scope *and* with the Q1 photo ≤24h raw cap. Do not let UX pick Mathpix without Legal review.

**Stroke-data retention vs Q1 photo — same or different?** Same ceiling, different optimum. Q1 photo raw binary = **end-of-session or 24h**, whichever first (per PRR-255). Stroke-data raw time-series = **end-of-HWR-call**, not 24h. There is no product reason to keep stroke arrays after the HWR has emitted its symbolic answer — the symbolic answer IS the data. Keeping the raw strokes is pure liability. HWR-output TTL = 30d per ADR-0003 inheritance. Low-res rasterized thumbnail for student review = session-scoped (evict on logout) is acceptable, not required. **New task**: PRR-257 Stroke-Data Minimization Policy — raw strokes discarded post-HWR, thumbnail session-scoped, no dynamics telemetry ever persisted.

**§3.3 redacted-question projection — separate RTBF handling?** Yes. The server-side redaction mode (brief §3.3, option B or C) creates a **second projection of the question payload** — the same question exists as full-payload-with-options and as redacted-without-options, and the redacted projection is itself keyed by studentId+sessionId+questionId (because it's per-session student-toggled). This projection may be cached in the API response cache or in the client runtime state. PRR-223 RTBF cascade, as of today, iterates projections of **student-owned data**; the redacted-question projection is **derived per-student but contains content-under-author-copyright** — a hybrid. On student erasure, the session-scoped redaction-state record (which question was hidden when, revealed when) must cascade — this is session-telemetry and clearly in-scope for RTBF. The question content itself does not cascade (it belongs to the author, not the student). Action: when PRR-252 lands (RTBF cascade extension to blob + LLM-cache), extend scope to include the `redaction_state` cache keyed on sessionId — add a bullet to PRR-252 rather than spawning a new task. No separate blocker.

## Section 7 positions

- **Q2 default state** (brief 3.1): no privacy position — pedagogy call.
- **Q2 server-side enforcement** (3.3): **optional student-controlled is best-in-class privacy** (student has agency over their own session-state projection). Classroom-enforced is acceptable with a paired disclosure, paralleling the 001-findings tenant-override question.
- **Q2 commit-and-compare** (3.7): **privacy-neutral** — does not change data flows.
- **Q3 math modality** (4.1): **no position on primary**, but if writing-pad ships, client-side HWR must be the default path; LLM-vision is fallback only, gated by PRR-251.
- **Q3 chem modality** (4.3): **typed-primary is privacy-preferred**; Lewis-structure pad is LLM-vision-minority-path, same PRR-251 gate.
- **Q3 language modality** (4.4): **confirm keyboard-only**. Handwritten essay HWR would pipe a student's entire inner voice through an LLM vendor — categorical no-ship.
- **Q3 HWR procurement** (4.6): **MyScript/iink.js client-side preferred on privacy grounds**; LLM-vision as fallback; **Mathpix disqualified pending DPA audit**.

## Recommended new PRR tasks

1. **PRR-257 — Stroke-Data Minimization Policy.** Raw strokes discarded post-HWR call; HWR symbolic output inherits ADR-0003 (30d); optional thumbnail session-scoped; no dynamics telemetry persisted. Extends PRR-255 (photo TTL) to stroke data. **Launch blocker for Q3 writing-pad.**
2. **PRR-258 — Client-side HWR procurement gate + Mathpix DPA audit.** Privacy-review gate: any server-side HWR vendor (Mathpix, Google HWR, TrOCR-as-a-service) must pass zero-retention DPA review before shortlist. Locks MyScript/iink.js-client as default path absent explicit override.
3. **PRR-259 — No-proctoring-via-biometrics ADR.** Stroke dynamics, keystroke dynamics, gaze, and voice are permanently out of scope for identity-verification / anti-cheat without a new DPIA + ADR + parental consent surface. Prevents silent scope-creep into behavioural biometrics. **Forward-looking guardrail.**
4. **PRR-260 — Extend PRR-252 scope** to cover `redaction_state` session cache (§3.3). Single bullet addition, not standalone.

## Blockers / non-negotiables

- **Blocker-1..4 from 001 remain open** (PRR-250 CSAM quarantine, PRR-251 VisionPromptSafetyGate, PRR-252 RTBF cascade blob+cache, PRR-255 photo TTL ≤24h). PRR-252 scope now expands per PRR-260.
- **Blocker-5 (new):** PRR-257 Stroke-Data Minimization. Writing-pad ships with raw-stroke-array persistence = Art. 5(1)(c) violation on day one.
- **Non-negotiable:** Mathpix or any server-side HWR vendor cannot be selected without passing PRR-258 DPA audit. Default procurement = client-side HWR.
- **Non-negotiable:** Handwritten essay HWR for language subjects is categorical no-ship in v1 and v1.x. Confirms brief §4.4 recommendation.
- **Non-negotiable:** HWR-via-LLM-vision uses the same `VisionPromptSafetyGate` class, not a bypass or a "lite" path.

## Questions back to decision-holder

1. **Client-side HWR accuracy floor** — if MyScript/iink.js hits only ~85-92% on Hebrew/Arabic math, is the fallback to LLM-vision acceptable, or do we cap writing-pad to content types where client-side clears a gate? My position: cap and ship, don't over-rely on LLM-vision.
2. **Thumbnail review UX** — does product want the student to see their own inked thumbnail after submission for verification? If no, kill it; if yes, session-scoped with evict-on-logout is the privacy-preferred shape.
3. **Section 3.3 classroom-enforced redaction disclosure** — who writes the per-class disclosure copy (teacher, system, platform)? Paired question to the 001 tenant-override disclosure.
4. **PRR-259 scope confirmation** — is a preemptive "no proctoring-via-biometrics" ADR desired now, or parked until someone proposes proctoring? My position: land the ADR now; it costs nothing and it slams a door we do not want to walk through.
