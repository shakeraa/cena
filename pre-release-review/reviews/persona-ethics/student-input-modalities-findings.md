---
persona: ethics
subject: STUDENT-INPUT-MODALITIES-001
date: 2026-04-21
verdict: yellow
---

## Summary

All three modalities are defensible under the Design Non-Negotiables, but only if Q1 ships narrow, Q2 ships as option B, and Q3 ships with an explicit appeals path. The autoresearch series (iterations 02-10) already did the hard ethics work for photo input — the conclusions are consistent and aggressive: photo is a legitimate access-widening feature for Arab-sector / peripheral / low-income students (iter-08), but every layer assumes ephemeral processing (iter-06), CSAM-tier moderation (iter-04), prompt-injection-as-structural (iter-02), and learning-first-not-answer-first response (iter-07). The product brief inherits those conclusions but does not yet carry them forward into the UX scope — that is the gap this review closes. Q2 option C is the one structural dark-pattern risk in the brief. Q3 is a latent bias-injustice surface that will ship yellow unless an appeals path exists at launch.

## Section 7.3 answers

**Q1 — Photo-upload consent for minors: implicit consent-by-action sufficient?**

No. Implicit-consent-by-action is defensible for sending a typed math expression to a CAS engine; it is not defensible for sending a minor's photograph to a third-party vision model (Gemini 2.5 Flash, per iter-04 §5). Amendment 13 classifies biometric-adjacent data (handwriting is not a face but is on the gradient) as ISS; COPPA 2025 explicitly names photos containing a child's image as personal information (iter-06 §1.1); GDPR-K Article 8 requires a specific lawful basis. The ethically defensible pattern is a first-time modal, one-time per account, dismissable, re-surfaceable from settings. Copy (English source, he/ar translations commissioned):

> "To diagnose where your working went wrong, we send your photo to an AI vision model. We strip EXIF data and location. We never store your photo. We never use it to train models. Your teacher can see that you uploaded a photo, but not the photo itself. You can turn this off any time in Settings. [Continue] [Not now]"

Key properties: dismissable without penalty; "Not now" falls back to typing; the consent is per-student, not per-tenant (tenant can additionally disable — persona-enterprise's 7.5 point — but cannot force-enable). For students under the jurisdictional consent age (Germany 16, Italy 14, France 13 — Cena serves 14-18 so most fall above Israel/IL's informal threshold), the modal is supplemented by the guardian-consent flow already locked for EPIC-PRR-C. Do not rely on ToS acceptance alone. One modal, shown once, in the moment of the action — this is the distinction between meaningful consent and compliance theatre.

**Q1 framing — narrow or broad?**

Narrow. Non-negotiable for v1. Three reasons:

1. Iter-02 explicitly ranks photo upload as the single highest prompt-injection-via-OCR surface in the platform. The defense-in-depth stack (six layers) is engineered around the narrow flow — "extract math from photo of work, verify via CAS." Broad framing ("ask the tutor about anything") expands the extraction space past what the CAS-gate can verify and past what the allowlist-schema can constrain (iter-02 §4.4-4.6). The CAS is the only deterministic backstop in the stack; removing it by broadening the flow collapses the security model.
2. Broad framing breaks iter-07's academic-integrity position. Cena's defense against Scenario A (live exam photography) is that every photo returns a guided session for a *variant* question, not an answer to the photographed question. Broad framing explicitly invites "solve this for me" semantics and reopens the Photomath failure mode.
3. Finops per 7.10: broad framing plus the ~$3.30/student/month cap (ADR-0050 Q5) is arithmetically incompatible. Narrow framing keeps vision calls bounded to the "I got this wrong" affordance — one call per wrong-answer event, not unbounded exploration.

Broad framing can be reconsidered post-launch with a separate ADR, separate cost envelope, and a separate consent surface. Do not roll it in.

**Q2 — Pedagogy-driven hide-then-reveal (option C): dark pattern?**

Yes, as currently framed. Option C hides MC options based on the scheduler's mastery belief about the student without telling the student, and with no off-switch. That fails two of GD-004's three pillars: (a) it is opaque — the student cannot determine why they got the hard variant today; (b) it is non-reciprocal — the student has no way to contest or override. The mechanic is pedagogically sound per persona-cogsci's generation-effect framing, but paternalism stops being benign the moment the student cannot see or change the rule. That is the ADR-0003 misconception-data pattern inverted: misconception data is session-scoped *because* the student cannot argue with an inference the system does not surface.

Ship-safe path: option B (per-session student toggle) is the ethically defensible default. Option A (per-question author-set) is also acceptable because the author-set flag is visible in the item and auditable. Option C is acceptable *only if* the UI always tells the student "this question hides the options by default — you can reveal them any time" with a one-tap override. That is option B wearing a C costume, which is fine. Without the reveal-always escape hatch, C is banned alongside variable-ratio rewards.

**Q3 — Rubric-graded free-form essays: inconsistency/bias surface + appeals path**

This is the single most load-bearing ethics question in the brief, and the brief underweights it. LLM rubric graders are inconsistent across runs (non-zero temperature), biased across demographics (the literature is unambiguous — Arab-sector students writing Hebrew, Hebrew-L2 students, students using atypical sentence structure all systematically score lower on LLM graders than on human graders of the same essays), and opaque. Absent mitigation, rubric grading is an injustice pipeline wearing an "AI tutor" costume.

Minimum ethics bar for launch:

1. **Rubric grading is never terminal.** The LLM grader produces a *draft* with rubric-level feedback. The student sees the draft with a banner: "This is an AI draft grade. A human teacher gives the final score." For tenants without a teacher-in-loop (self-study users), the grade is displayed as "AI estimate — not a final grade" and is not persisted as a score against the student's profile (ADR-0003 session-scope extends here).
2. **Appeals path mandatory.** One-click "Disagree with this grade" button on every rubric-graded response. The disagreement is logged, shown to the teacher if tenant-enrolled, and does not penalize the student's future grading in any way (link this to a PRR task for the override-access-control pattern used for `ExamTargetOverrideApplied` in multi-target — same access-control shape).
3. **Bias audit required pre-launch.** A held-out eval set of Bagrut Literature / History essays scored by both LLM grader and human raters, stratified by Arabic-L1/Hebrew-L1/Hebrew-L2. Publish rater-LLM agreement and delta-by-demographic. If the demographic delta exceeds ~5 percentile points, rubric grading does not ship for humanities at launch.
4. **Structured reasons only.** The LLM returns a rubric-aligned score plus a bounded list of rubric-line comments. Free-form "here's why you're wrong" prose is banned — it is where hallucinated reasoning, demographic tells, and tone-policing slip in.

**Autoresearch iterations 02-10 ethics summary**

- **iter-02 (prompt injection)**: structural, unfixable at the LLM layer. Defense is architectural (CAS backstop, dual-LLM, structured output). Conclusion: ship with all six defense layers or do not ship photo.
- **iter-03 (LaTeX sanitization)**: allowlist-for-students creates a walled garden for 5-unit advanced learners — pedagogical cost, not an ethics blocker.
- **iter-04 (moderation for minors)**: CSAM reporting is non-negotiable, PhotoDNA tier-0 required, over-filtering (Scunthorpe problem in physics/chem) is the real operational risk. Escalation path needed for moderator-uncertain cases.
- **iter-06 (privacy)**: ephemeral processing is the only defensible stance. The "we could learn from student images" objection is steel-manned and rejected — privacy-first wins on both ethics and PPL Amendment 13 grounds.
- **iter-07 (academic integrity)**: learning-first-not-answer-first is architectural, not prompt-based. The step-solver + variant-generation + engagement gates make the platform useless for cheating by construction. This is the ethics high-water mark — the rest of Cena should match it.
- **iter-08 (a11y / digital divide)**: photo input narrows the tutoring gap for Arab-sector and peripheral students *if* the text-input fallback is genuinely first-class (not a consolation prize). Banning photo would be an ethics violation against the population it serves.
- **iter-09 (graceful degradation)**: the failure distribution is anti-correlated with need — exactly the students photo is meant to help have the worst cameras and the messiest handwriting. Fallback copy must not blame the student ("try a clearer photo" is banned by error-message-blame.yml — verify).
- **iter-10 (e2e)**: 87/100 with all layers active. The residual 13 points are accepted residual risk, not dismissed.

Position: the iter-02 → iter-10 conclusions are stricter than the brief's section 3, and must carry forward into the PRR tasks verbatim. The brief says "infra exists" — it does, but the infra's ethics envelope does too, and the brief must not silently re-narrow it.

## Section 8 positions

1. **Q1 framing**: narrow. Broad framing requires its own ADR post-launch.
2. **Q2 implementation**: option B (per-session student toggle). Option C only if the reveal-always override is permanent and visible. Option A is fine as a v1.5 addition.
6. **LLM cost cap**: narrow Q1 + option B Q2 + rubric-grading with human-in-loop for humanities (tenant-gated) is inside the $3.30 cap. Broad Q1 or option C Q2 or rubric-graded chem at launch blows it. If the $3.30 is load-bearing (ADR-0050 Q5), that's the ethics-anchored scope.

## Recommended new PRR tasks

1. **PRR-ethics-INP1** — banned-identifier scanner additions: add `photoAskAnything`, `photoBroadMode`, `openEndedPhoto`, `hideOptions.*auto`, `hiddenByMastery`, `masteryDrivenHide` as identifier patterns in `src/` (prevents option C leaking via variable names as multi-target's `daysUntil` would have). Hebrew/Arabic equivalents for "שאל על כל דבר" / "اسأل عن أي شيء".
2. **PRR-ethics-INP2** — first-time photo-upload consent modal: en/he/ar copy, dismissable, settings-reachable, telemetry-free (consent decision stored locally + once on profile; no event stream). Blocks photo-feature GA sign-off.
3. **PRR-ethics-INP3** — Q2 reveal-always override: assert every `AttemptMode='hidden'` surface has a visible "Show options now" affordance reachable by keyboard and screen reader. Unit + e2e test. Blocks EPIC-PRR-H (or whichever epic owns Q2) sign-off.
4. **PRR-ethics-INP4** — rubric-grading appeals path: "Disagree with this grade" button, non-penalizing log, teacher-visible if enrolled. Access-controlled per ExamTargetOverride pattern. Blocks humanities launch.
5. **PRR-ethics-INP5** — rubric-grading demographic bias audit: held-out eval set, delta-by-L1, publish-before-launch. If delta > ~5pp, humanities rubric-grading does not ship at launch; MC-only is the honest fallback.
6. **PRR-ethics-INP6** — moderation-uncertain escalation: when iter-04's 4-tier moderation returns "uncertain" on a photo, route to human review queue (24h SLO), never silently show-to-student. Copy for the student during the wait: neutral, no blame, retry-typing affordance.
7. **PRR-ethics-INP7** — fallback copy audit: verify "we couldn't read your photo, please type the question" variants pass `error-message-blame.yml`, are not amber/red-styled, and offer a one-tap typing alternative without re-asking for consent.

## Blockers / non-negotiables

- **Q1 ships narrow.** Broad framing requires a separate ADR and is not in v1.
- **Q1 first-time consent modal is required.** Implicit-consent-by-action is insufficient for minor + photo + third-party vision model.
- **Q2 option C ships only with a visible, always-available reveal override.** Otherwise C is banned.
- **Rubric-graded humanities essays ship with appeals path + bias audit.** No exceptions.
- **Rubric-grading is never terminal without human-in-loop for enrolled tenants, and is surfaced as "AI estimate, not a grade" for self-study.**
- **Fallback text-input is first-class, not a consolation prize.** The student who declines photo consent or whose photo failed OCR must not experience a degraded product — this is the iter-08 digital-divide commitment.
- **Iter-02 six-layer defense stack is a ship-gate for photo.** Not "target", not "phase 2" — hard precondition.

## Questions back to decision-holder

1. For Q3 humanities, is there appetite to ship MC-only at launch (persona-educator 7.1's "can we honestly claim CAS-gated correctness" question) and defer rubric grading until the bias audit clears? This is the ethically safest path and it is on the table.
2. For the Q1 consent modal: does it re-prompt if the student switches devices / re-installs the PWA, or is the consent decision tied to the user profile server-side? Recommend server-side so one consent decision follows the student; confirm.
3. For Q2, do we want the student toggle (option B) to default on ("try first mode"), default off (today's behaviour), or remember-last-session? Ethics-neutral among the three; cogsci/educator lenses should pick.
4. For the rubric-grading appeals path, does a student disagreement route to the teacher (if enrolled), to Cena operations, or both? Recommend teacher-primary with Cena-ops on aggregate patterns only; confirm.
