# ACT-020: Fix Stagnation Signal Architecture — Per-Session Aggregation

**Priority:** P1 — stagnation detection is currently non-functional
**Blocked by:** ACT-019 (decomposition makes this cleaner)
**Estimated effort:** 1.5 days
**Source:** Actor system review — architectural mismatch (per-attempt vs per-session signals)

---

## Context
`StudentActor` was sending `UpdateSignals` after every individual attempt, but `StagnationDetectorActor` is designed to receive per-SESSION summaries (`UpdateStagnationSignals` with session accuracy, avg RT, duration, error repeat count, annotation sentiment). The per-attempt sends have been replaced with TODO comments in the C2 fix. This task implements the correct session-level aggregation.

## Subtasks

### ACT-020.1: Accumulate Session Signals in StudentActor
**Files:**
- `src/actors/Cena.Actors/Students/StudentActor.cs` — modify

**Acceptance:**
- [ ] Add session-level signal accumulator fields: `_sessionAttemptCount`, `_sessionCorrectCount`, `_sessionTotalRtMs`, `_sessionErrorCounts` (Dictionary<string, int>)
- [ ] On each `AttemptConcept`, increment session accumulators
- [ ] Reset accumulators on `StartSession`
- [ ] On `EndSession`, compute session-level aggregates and send `UpdateStagnationSignals` to detector

### ACT-020.2: Send Session Summary to StagnationDetector
**Files:**
- `src/actors/Cena.Actors/Students/StudentActor.cs` — modify (HandleEndSession)

**Acceptance:**
- [ ] After ending session, compute: sessionAccuracy, avgResponseTimeMs, sessionDurationMinutes, errorRepeatCount, annotationSentiment
- [ ] Send `new UpdateStagnationSignals(conceptCluster, sessionAccuracy, avgRt, duration, errorRepeatCount, sentiment)` to `_stagnationDetector`
- [ ] Send `new CheckStagnation(conceptCluster)` after the update
- [ ] ConceptCluster = the primary concept worked on during the session

### ACT-020.3: Integration Test
**Acceptance:**
- [ ] Test: 3 sessions with flat accuracy trigger stagnation detection
- [ ] Test: improving accuracy across sessions does NOT trigger stagnation
- [ ] Test: cooldown period after methodology switch prevents re-triggering
