# RDY-065 — F11: Anxiety-safe hint ladder

- **Wave**: A (ship first — universally endorsed by the 10-panel review)
- **Priority**: HIGH
- **Effort**: 1-2 engineer-weeks
- **Dependencies**: none
- **Source**: [cena-user-personas-and-features-2026-04-17.md](../../docs/research/cena-user-personas-and-features-2026-04-17.md) F11, [panel review](../../docs/research/cena-panel-review-user-personas-2026-04-17.md) Round 2.F11
- **Tier**: 1 (pedagogy core)

## Problem

Today's hint UX reveals answers too quickly and penalizes help-seeking. Across the 10-persona research, every student hits this pain (cluster C3 + C1). Yael (dyscalculia) abandons the app on timer pressure + red-X; Noa (5-unit ambitious) over-uses hints to compensate for time anxiety; Amir (Arabic L1) can't tell whether he understood or got lucky.

## Scope

Replace the current discrete hint levels with a Socratic-scaffolding ladder:

1. **Level 1 — Orient**: reformulate the problem in the student's own terms; ask "what are we trying to find?"
2. **Level 2 — Activate prior knowledge**: ask "what rule/theorem might apply here?"
3. **Level 3 — Analogous worked example**: show a solved problem with the same structure, student must draw the parallel
4. **Level 4 — Guided next step**: give the first move, student finishes
5. **Level 5 — Full worked solution** (last resort)

Guardrails:
- No timer visible anywhere in the hint flow
- No "hint used" penalty visible to student
- Mastery credit adjusted internally (assisted < unassisted per Dr. Nadia lens) — never exposed as "−20%"
- Topic-aware scaffolding questions authored per topic family (not generic)
- CAS-verified worked examples (F1 pipeline reuse)

## Files to Create / Modify

- `src/student/full-version/src/components/hints/HintLadder.vue` — new
- `src/student/full-version/src/stores/hintStore.ts` — new
- `src/shared/Cena.Domain/Hints/HintLevel.cs` — enum + domain events
- `src/actors/Cena.Actors/Students/StudentAggregate.cs` — record hint events
- `src/shared/Cena.Domain/Mastery/BktParameters.cs` — assisted-credit discount
- `docs/design/hint-ladder-design.md` — scaffolding template per topic family

## Acceptance Criteria

- [ ] Hint UI has zero visible timers, countdowns, or "X hints used" counters
- [ ] Topic-aware scaffolding questions authored for at least 8 topic families (calc, algebra, trig, geometry, stats, functions, sequences, vectors)
- [ ] BKT assisted credit discount applied but not displayed
- [ ] Ship-gate scanner confirms no banned dark-pattern terms in hint copy
- [ ] A/B measurement harness in place (metrics below)

## Success Metrics (Rami's demand)

- **Hint-helpfulness rate**: % of students who reach correct answer within 2 levels after requesting hint (target: >60% by Level 2)
- **Abandonment-during-hints rate**: % of sessions where student exits mid-hint (target: <15%)
- **Mastery-gain-per-hint**: ΔBKT per hint consumed (target: positive delta across cohort)
- **LD-cohort-specific abandonment**: same as above but scoped to accommodations-enabled students (target: not worse than general)

## ADR Alignment

- ADR-0002: scaffolding worked examples CAS-verified
- GD-004 (shipgate.md): no streaks, no penalty-shaming, no timers — CI scanner enforces
- ADR-0003: session-scoped hint-request patterns (not persisted to student profile)

## Out of Scope

- F1 (Socratic explain-it-back) — separate task RDY-074
- Cross-session hint-pattern analytics — forbidden by ADR-0003

## Assignee

Leave unassigned; expected: Dr. Nadia lens + coder pair.
