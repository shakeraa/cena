# Bagrut accommodations parity matrix — detailed narrative

- **Source ADR**: [ADR-0040](../adr/0040-accommodation-scope-and-bagrut-parity.md)
- **Audience**: school buyers, Ministry of Education inspectors, internal compliance reviewers
- **Related**: [ADR-0038](../adr/0038-event-sourced-right-to-be-forgotten.md), [ADR-0003](../adr/0003-misconception-session-scope.md)

This document is the long-form companion to the parity-matrix table in ADR-0040. Each row below explains the Ministry mandate, Cena's implementation, any known gap, and how the certificate trail is preserved across enrolments.

## Extended time (25% / 50%)

**Ministry mandate.** A student whose Bagrut-accommodation letter specifies extended time receives 25% or 50% additional time on each timed section of a Ministry exam. The two bands are distinct — a 25% letter does not unlock 50% time.

**Cena implementation.** The field `ExtendedTimeMultiplier` on the enrolment carries the value `1.25` or `1.50` (or `1.00` for no accommodation). The Bagrut-simulation mode reads this value when sizing the session timer; practice sessions outside exam simulation are uncapped regardless.

**Gap.** None functionally. Note that the multiplier is enrollment-scoped, not profile-scoped — the rationale (ADR-0040) is that different institutes legitimately hold different letters for the same student.

**Certificate trail.** On grant/revoke/adjust, the audit stream records the certificate reference (opaque token to the on-file letter), the authorisation source category, and the actor who effected the change. Under subject erasure the certificate reference crypto-shreds to `[erased]` while the structural audit line survives.

## Text-to-speech (TTS)

**Ministry mandate.** Students with reading-disability diagnoses (including but not limited to dyslexia) may have instructions and/or passages read aloud. Mathematical content is usually excluded from human-reader coverage for fairness reasons, but a digital TTS that renders symbolic math spoken form is not specifically regulated — it is a usability affordance the platform is free to offer.

**Cena implementation.** Two flags: `InstructionsSpokenPreferred` covers non-math prose; `MathSpokenPreferred` covers the symbolic-math speech path that consumes the MathML screen-reader output. Both flags are student-profile-scoped.

**Gap.** We do not currently synthesise TTS server-side; we rely on the browser's built-in speech synthesis. Quality varies by locale — Hebrew synthesis is weaker than English or Arabic. Noted in the mentor runbook.

**Certificate trail.** Grant is authorised by a reading-disability diagnosis certificate or by student self-declaration; both are recorded. Revocation typically only by the student themselves in settings.

## Enlarged print

**Ministry mandate.** Students with visual-impairment or specific-learning-disability diagnoses may receive exam papers at an enlarged print size. The Ministry specifies a minimum font size of roughly 18pt for the enlarged variant.

**Cena implementation.** The `PreferredFontSize` profile setting tracks the student's chosen base font size. The threshold 18pt triggers an "enlarged-print mode" CSS variant that enlarges math rendering, diagrams, and surrounding UI chrome in proportion (not just the prose). Student-profile-scoped.

**Gap.** Our enlargement is proportional across content and chrome. The Ministry's paper-exam enlargement is sometimes done for content only. We treat our proportional enlargement as strictly more usable, not a gap.

**Certificate trail.** Audit-logged on every change to `PreferredFontSize` that crosses the 18pt threshold. Crosses below 18pt are also logged, to capture revocations.

## Graph-paper overlay

**Ministry mandate.** Some diagnoses (dyscalculia, certain visual-spatial conditions) entitle the student to graph-paper-backed answer sheets for geometry and coordinate-plane work.

**Cena implementation.** `GraphPaperOverlayRequired` (student-profile-scoped) when true renders a grid overlay behind diagrams and sketching surfaces in coordinate-plane questions. No-op on non-coordinate questions.

**Gap.** None functionally. The overlay is rendered client-side, so it is available in practice sessions as well as simulation.

**Certificate trail.** Grant usually coincides with a dyscalculia diagnosis; revocation is uncommon and logged to the audit stream.

## MathML screen-reader

**Ministry mandate.** The Ministry's accessibility guidance recognises that screen-reader compatibility for mathematical content is a necessary accommodation for blind and severely-visually-impaired students. The specific technology is not mandated; semantic markup that assistive technology can consume is what is audited.

**Cena implementation.** Not a user-facing flag. Content is authored and rendered with MathML/ARIA markup that screen readers and our own TTS path both consume. We consider this "always on" for any student whose profile indicates a visual-impairment diagnosis, and available opt-in for any other student via the TTS flags above.

**Gap.** MathML coverage depends on question authoring. Questions authored before the content pipeline enforced MathML-first authoring (see ADR-0033) may have image-based formulas that screen readers cannot parse. These are tagged for content-debt cleanup.

**Certificate trail.** Not a separate grantable accommodation — it is the delivery substrate for the visual-impairment category. The diagnosis grant audit trail covers it implicitly.

## Reader (human reader)

**Ministry mandate.** Some students receive a human reader who reads exam content aloud.

**Cena implementation.** Out of scope. Cena is a digital platform; we do not dispatch human readers. Schools using Cena that provide human readers do so outside the platform, as they would with any digital learning tool.

**Gap.** This is a scope exclusion, not a defect. The parity matrix is explicit about it so that no school buyer treats "supports human-reader accommodation" as a Cena feature.

**Certificate trail.** Not applicable.

## Scribe

**Ministry mandate.** Students with severe physical-disability or dysgraphia diagnoses may receive a human scribe who transcribes their dictated answers.

**Cena implementation.** Out of scope. Cena's input surface is the student's own keystrokes and screen interactions; we do not model scribe-mediated input.

**Gap.** Same scope exclusion as for human reader. The parity matrix is explicit.

**Certificate trail.** Not applicable.

## Notes on the two "out of scope" rows

The two human-mediated accommodations (reader, scribe) are called out explicitly in the parity matrix because a school buyer scanning the matrix should immediately see that those accommodations require human staff at the school's end, not at the platform's end. Leaving them off the matrix entirely would be worse — a buyer might assume the omission means "not applicable" rather than "out of scope by design".

## Cross-reference to erasure

Every row above interacts with [ADR-0038](../adr/0038-event-sourced-right-to-be-forgotten.md). PII fields (certificate reference, student subject ID on audit events, free-form accommodation notes) are encrypted with the subject's per-subject key and become `[erased]` on subject-key destruction. Non-PII fields (the accommodation type, the timestamp, the authorisation-source category, the scope tag) remain plaintext and survive erasure so that aggregate reporting ("we granted N dyslexia TTS accommodations in 2026") continues to work without violating the erased subject's rights.
