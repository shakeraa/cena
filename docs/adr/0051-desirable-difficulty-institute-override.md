# ADR-0051 — Desirable-difficulty target: 75% default + institute override

- **Status**: Accepted
- **Date**: 2026-04-21
- **Decision Makers**: Shaker (project owner), persona-cogsci, persona-educator, persona-ethics
- **Task**: prr-030
- **Related**: [ADR-0040](0040-accommodation-scope-and-bagrut-parity.md), [ADR-0049](0049-citation-integrity-effect-size-communication.md), [prr-041 DifficultyTarget.cs](../../src/actors/Cena.Actors/Mastery/DifficultyTarget.cs)

---

## Context

The adaptive scheduler needs a cohort *accuracy target* — the success rate at which new items are served to a student. Too high → ceiling effect, no consolidation (Wilson 2019); too low → extinction learning, quit rate spikes (Bjork 1994).

Cena shipped prr-041 with a cohort default of 0.75 + a pre-exam window of 0.85 + a +5pp anxious-profile boost. That default was correct in direction (the original 0.60 baseline demoralised IL Bagrut students per persona-educator field audit) but it was *global*. The pre-release review (2026-04-20) raised two problems with a single global value:

1. **IL Bagrut institutes have heterogeneous cohorts.** A cram school preparing 5-unit (5-יחידות) maths students and an NGO supporting weak 3-unit students need different pacing — the cram cohort tolerates a lower target (0.70) because they thrive on effortful retrieval; the NGO cohort needs a higher anti-demoralisation floor (0.80).

2. **We have no arch-test guard** against a future contributor silently raising the default to 0.90 ("let's make students feel successful!") or dropping it to 0.50 ("let's push harder!"). Both ship a research-contradicting setting.

## Decision

### Default remains 0.75, now documented research-first

The `DifficultyTarget.Default` constant stays at 0.75. The in-file comment header now cites Bjork & Bjork (2011), Wilson et al. (2019), Karpicke & Roediger (2008), and Dunlosky et al. (2013), with explicit effect-size honesty (0.85 rule is synthetic, not human-RCT; desirable difficulty meta-effect is d≈0.3–0.5, not transformative; IL-Bagrut calibration is internal observation, not peer-reviewed).

### Institutes can override, but only inside the Bjork-bounded range [0.6, 0.9]

`InstituteDocument.TargetAccuracy` (nullable `double`) is the override seam. `DifficultyTarget.TargetSuccessRate` now accepts an `instituteTargetOverride` parameter:

- `null` → use `Default` (0.75)
- in `[0.6, 0.9]` → replaces `Default` as the cohort base. Pre-exam window (0.85) and anxious-profile boost (+0.05) still compose on top.
- outside `[0.6, 0.9]` → `ArgumentOutOfRangeException` at call time. We **refuse to silently clamp** because a misconfigured institute deserves a visible startup failure, not a silent 0.6 floor.

### Arch-test lock

`AccuracyTargetInBjorkRangeTest` enforces:

- `DifficultyTarget.Default` is in `[0.6, 0.9]`.
- `DifficultyTarget.PreExam` is in `[0.6, 0.9]`.
- Any valid-range override round-trips through `InstituteConfig` cleanly.
- Any out-of-range override throws `ArgumentOutOfRangeException`.
- `null` override defers to `Default`.
- Valid override replaces `Default`, does NOT lower `PreExam`.
- Valid override composes with anxious boost (0.70 + 0.05 = 0.75).

## Rationale — cited BEFORE the code change (senior-architect protocol)

### Bjork desirable difficulty

Bjork & Bjork (2011), *Making things hard on yourself, but in a good way: Creating desirable difficulties to enhance learning* (Psychology and the Real World, pp.56–64). Learning is *optimised* when retrieval is effortful but mostly successful. Trivially easy retrieval produces no memory consolidation (ceiling effect); constantly failing retrieval produces learned helplessness. The "sweet spot" is empirically domain-specific.

### Wilson 85% rule

Wilson, Shenhav, Straccia & Cohen (2019), *The Eighty Five Percent Rule for optimal learning*, Nature Communications 10:4646, DOI 10.1038/s41467-019-12552-4. Derived 0.847 ≈ 85% as theoretical optimum for *fixed-difficulty stochastic gradient descent*. Synthetic, not RCT. Human learners with motivation budgets and metacognitive shame run BELOW this ceiling.

### Dunlosky meta-analysis (effect size honesty, ADR-0049)

Dunlosky, Rawson, Marsh, Nathan & Willingham (2013), *Improving students' learning with effective learning techniques*, Psychological Science in the Public Interest 14:4–58. Desirable-difficulty umbrella (spacing, interleaving, retrieval practice) produces d≈0.3–0.5 across varied interventions. Moderate, not transformative. We do NOT claim d=0.85.

### IL-Bagrut calibration (internal, disclosed honestly)

Persona-educator field audit (Dr. Lior + three Israeli tutor reviewers, 2026-04-20) observed that Bagrut-prep cohorts quit sessions at elevated rates when success dropped below ~0.65. This is tutor-reported, NOT an RCT. We disclose this explicitly per ADR-0049 rather than dress it up as peer-reviewed evidence. The 0.75 default is a *floor chosen from quit-rate observations*, not a replicated experimental outcome.

### Karpicke retrieval practice near exam

Karpicke & Roediger (2008), *The critical importance of retrieval for learning*, Science 319:966–968, DOI 10.1126/science.1152408. Retrieval practice near a high-stakes exam wants ceiling-near success to reinforce access paths, not to push new learning. Hence the pre-exam window (30 days) flips to 0.85 regardless of institute override.

## Why [0.6, 0.9] specifically

- **0.60 floor** ≈ Bjork extinction threshold. Below this, a substantial fraction of a cohort disengages within 3–5 trials. This is the same value we use as the ship-gate "struggling topic" threshold for teacher dashboards (prr-049). Symmetry is deliberate: both surfaces share one pedagogical safety floor.
- **0.90 ceiling** ≈ Wilson consolidation cap. Above this, additional trials add no measurable posterior mastery update. A scheduler running at 0.95 is serving easy items to busy students — wasting their exam-prep window.

## Consequences

### Positive
- Institutes can tune within a research-safe range without touching code.
- Arch-test mechanically prevents a future "let's raise the default" or "let's drop the floor" drift.
- Default documented with real citations + honest effect sizes (ADR-0049 compliant).

### Negative
- One more institute-scoped knob to document for buyers. Not cost-free; admin-console copy must cite the Bjork range plainly.
- The override is NOT available to teachers — only to institute admins. Teachers who disagree with their institute's calibration cannot override it for their classroom. This is deliberate (per prr-044 / ADR-0040 enrollment-scope rules) but will likely raise a Phase-2 follow-up for classroom-scoped override.

### Neutral
- Existing behaviour unchanged for institutes that do not set an override. Zero behaviour change for 100% of current deployments.

## Verification (E2E, not mocked)

- **Unit**: `DifficultyTargetTests` (existing) + `AccuracyTargetInBjorkRangeTest` (new) cover the clamp.
- **Integration**: `DesirableDifficultyTargetingIntegrationTests` runs a 10-question simulated session with target 0.75 and asserts correct-rate sits inside 0.75 ± 0.05 (statistical tolerance across 200 simulated runs).
- **Arch test**: build fails if any seeded `InstituteDocument.TargetAccuracy` drifts outside `[0.6, 0.9]`.

## 03:00-on-Bagrut-morning runbook

Symptom: scheduler logs show `ArgumentOutOfRangeException` at institute startup.

1. Identify the institute from the exception message.
2. `SELECT id, target_accuracy FROM mt_doc_institutedocument WHERE target_accuracy NOT BETWEEN 0.6 AND 0.9;`
3. Either `UPDATE ... SET target_accuracy = NULL;` (revert to default) or set to a value in `[0.6, 0.9]`.
4. Do NOT silently clamp in code. This is a configuration defect and must be seen.

## References

- [DifficultyTarget.cs](../../src/actors/Cena.Actors/Mastery/DifficultyTarget.cs)
- [InstituteDocument.cs](../../src/shared/Cena.Infrastructure/Documents/InstituteDocument.cs)
- [AccuracyTargetInBjorkRangeTest.cs](../../src/actors/Cena.Actors.Tests/Architecture/AccuracyTargetInBjorkRangeTest.cs)
- ADR-0040 (accommodation scope & Bagrut parity) — precedent for institute-scoped pedagogy config.
- ADR-0049 (citation integrity & effect-size communication) — honesty contract.
- prr-030 task body, prr-041 predecessor task.
