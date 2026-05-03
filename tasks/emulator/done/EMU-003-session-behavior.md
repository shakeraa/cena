# EMU-003: Realistic Session Behavior — Attempts, Focus, Errors, Annotations

**Priority:** P0 — generates the actual learning events
**Blocked by:** EMU-002
**Estimated effort:** 3 days
**Contract:** `src/actors/Cena.Actors/Bus/NatsBusMessages.cs`

---

> **⛔ NO STUBS/MOCKS/FAKE CODE.**

## Context

Each active student session must generate a realistic stream of learning events. The current emulator replays pre-computed history; the new one generates events dynamically based on the student's archetype, mastery state, and focus level.

## Subtasks

### EMU-003.1: Concept Attempt Generator

**Files to create/modify:**
- `src/emulator/Behavior/ConceptAttemptGenerator.cs`

**Acceptance:**
- [ ] Each student session generates concept attempts at a realistic pace:
  - Fast students: 1 attempt every 15-30s (compressed time)
  - Normal: 1 every 30-60s
  - Slow: 1 every 60-120s
- [ ] Concept selection: weighted random from curriculum graph, biased toward:
  - Concepts near mastery frontier (BKT P(mastery) = 0.3-0.7)
  - Prerequisite-satisfied concepts only
  - Recently failed concepts get priority (spaced repetition)
- [ ] Correctness probability based on archetype + concept difficulty:
  - Genius: 85-95% on easy, 70-85% on hard
  - Struggling: 40-60% on easy, 15-30% on hard
  - Inconsistent: high variance (30-90% randomly per session)
- [ ] Response time realistic: 3-15s for easy, 15-60s for hard, with archetype modifiers
- [ ] Answer field: `"correct"` or `"incorrect"` (matches emulator protocol)

### EMU-003.2: Focus Degradation Simulation

**Files to create/modify:**
- `src/emulator/Behavior/FocusDegradationSimulator.cs`

**Acceptance:**
- [ ] Each session tracks simulated focus state: `Strong → Stable → Declining → Degrading → Critical`
- [ ] Transition rate varies by archetype:
  - Genius: degrades after 25-35 min
  - SteadyLearner: after 20-30 min
  - Struggling: after 10-15 min
  - VeryLowCognitive: after 5-10 min
- [ ] Focus degradation emits NATS events: `cena.events.focus.updated` (matching existing FocusDegradationService format)
- [ ] Mind-wandering events emitted probabilistically during `Declining`/`Degrading` states
- [ ] Microbreak suggestions emitted when focus reaches `Critical`
- [ ] After microbreak: focus resets to `Stable` (not `Strong`)

### EMU-003.3: Annotation & Confusion Events

**Files to create/modify:**
- `src/emulator/Behavior/AnnotationGenerator.cs`

**Acceptance:**
- [ ] Confusion annotations generated when:
  - Student gets 3+ consecutive wrong answers on same concept
  - Focus state is `Declining` or worse
  - Probability: 15% per wrong answer for Struggling/VeryLowCognitive, 5% for others
- [ ] Question annotations: 5-10% of attempts trigger a question annotation
- [ ] Annotation text drawn from bilingual templates (Hebrew for Hebrew students, Arabic for Arabic)
- [ ] Methodology switch events: triggered when stagnation detected (5+ attempts on same concept without improvement)

### EMU-003.4: Session End Behavior

**Files to create/modify:**
- `src/emulator/Behavior/SessionEndDecider.cs`

**Acceptance:**
- [ ] Session ends when ANY of:
  - Duration exceeds planned session time (from study habit profile)
  - Focus reaches `Critical` AND no microbreak taken
  - Student completes all available concepts at their mastery frontier
  - Random abandonment (5% chance per minute after 80% of planned duration)
- [ ] End reason mapped to `SessionEndReason`: `Completed`, `Fatigue`, `Abandoned`, `Timeout`
- [ ] End reason distribution varies by archetype:
  - Genius: 80% Completed, 10% Timeout, 10% Abandoned
  - Struggling: 30% Completed, 40% Fatigue, 20% Abandoned, 10% Timeout
  - Inconsistent: 20% Completed, 15% Fatigue, 50% Abandoned, 15% Timeout
