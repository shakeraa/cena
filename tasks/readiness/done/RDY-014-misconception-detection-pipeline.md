# RDY-014: Wire Misconception Detection Pipeline

- **Priority**: High — catalog exists, remediation exists, detection is the missing link
- **Complexity**: Senior engineer — algorithm integration
- **Source**: Expert panel audit — Nadia (Pedagogy)
- **Tier**: 2
- **Effort**: 2-3 weeks (revised from 1-2 weeks — pattern matching algorithm needs research)

> **Rami's challenge**: This is a RESEARCH task disguised as ENGINEERING. The task says "extract the error pattern" but doesn't specify HOW — symbolic matching? ML classification? Regex? Someone needs to hand-craft error pattern matchers for each buggy rule or integrate symbolic math to detect patterns. Before starting, research: does ASSISTments have published pattern matchers that can be reused?

## Problem

`MisconceptionCatalog.cs` contains 15 empirically-documented buggy rules (Koedinger et al.). `RemediationTemplates.cs` has pre-authored micro-tasks for each misconception. `ErrorClassificationService.cs` tracks session-scoped tallies. But the pipeline is disconnected: wrong answers don't trigger misconception detection.

The flow should be: wrong answer → error classification → misconception catalog match → remediation task triggered. Currently: wrong answer → generic feedback.

## Scope

### 1. Answer → misconception matching

When a student submits a wrong answer:
- Extract the error pattern (e.g., distributing exponent over sum: (a+b)^2 → a^2+b^2)
- Match against `MisconceptionCatalog` buggy rules
- If match found: emit `MisconceptionDetected` event with rule ID, confidence, counter-example

### 2. Misconception → remediation trigger

When `MisconceptionDetected` fires:
- Look up `RemediationTemplates` for matching rule
- If remediation exists: queue a remediation micro-task (warmup → targeted → transfer)
- Remediation task appears as next question in session (or as a side-quest)

### 3. Session-scoped tracking (ADR-0003)

- All misconception data lives in `LearningSessionState` only
- Never persists to `StudentState` or student profile
- 30-day retention, `[ml-excluded]` tag (see RDY-006)

### 4. Misconception feedback in hint pipeline

When a misconception is detected:
- Level 2 hint should reference the specific buggy rule
- "I notice you might be thinking [misconception]. Here's why that doesn't work: [counter-example]"

## Files to Modify

- `src/actors/Cena.Actors/Services/ErrorClassificationService.cs` — add misconception matching logic
- `src/actors/Cena.Actors/Services/MisconceptionCatalog.cs` — add matching interface
- `src/actors/Cena.Actors/Services/RemediationTemplates.cs` — add task generation
- `src/actors/Cena.Actors/Services/HintGenerationService.cs` — integrate misconception-aware hints
- `src/actors/Cena.Actors/StudentActor.cs` — wire detection into attempt handling

## Acceptance Criteria

- [ ] Wrong answer matching a buggy rule emits `MisconceptionDetected` event
- [ ] Remediation micro-task queued when misconception detected
- [ ] Misconception data session-scoped only (ADR-0003 compliance)
- [ ] Level 2 hint references specific misconception when detected
- [ ] At least 5 of 15 buggy rules have working detection patterns
- [ ] E2E test: submit answer matching DIST-EXP-SUM → remediation triggered
