# GD-007: Design spike — PhET-style student interview iteration protocol

## Goal
Document a repeatable student-interview protocol for iterating on Cena's sandbox physics mode (Point 4 of the proposal), modeled after PhET's published interview-driven iteration method.

## Source
`docs/research/cena-sexy-game-research-2026-04-11.md` — Point 4 is the best empirically validated claim in the proposal (Finkelstein 2005 PhET RCT beats real physical lab on conceptual items). Podolefsky finding: pure sandbox underperforms guided sandbox. Revision: "PhET-style manipulables inside Brilliant-style scripted beats."

## Work to do
1. Author `docs/design/phet-interview-protocol.md`:
   - Interview structure: 30–45 min / student, think-aloud, one sandbox per session
   - Moderator script (warmup, task, probe, debrief)
   - Observation rubric: what counts as a productive exploration vs. aimless wandering
   - Iteration cadence: interview → synthesize → change one variable → re-interview
   - Metrics: time-to-first-meaningful-interaction, % sessions reaching target conceptual insight, rating of frustration vs engagement
2. Recruit plan:
   - 6–8 Bagrut 4-unit or 5-unit physics students per iteration
   - EN / AR / HE split to match target demographic
   - Consent form (adult + minor — parental consent required for under-18 interviews)
3. Ethics / consent:
   - Parental consent form template for minors
   - Data handling (audio recordings): purpose-scoped, 90-day retention, no training use
   - Honorarium ~₪100 per session
4. Deliverable: a reusable protocol that any future Cena researcher can execute

## Source references
- Finkelstein et al. 2005 (PhET RCT vs real lab)
- Podolefsky et al. PhET implicit-scaffolding line
- Adams et al. "PhET interviews" methodology papers

## DoD
- Protocol doc merged
- Consent form templates included (EN/AR/HE)
- First interview round scheduled for after FIGURE-005 physics service ships

## Reporting
Complete with branch + first-round scheduling plan.
