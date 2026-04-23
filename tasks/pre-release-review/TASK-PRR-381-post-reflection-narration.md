# TASK-PRR-381: Post-reflection narration + mastery signal

**Priority**: P0
**Effort**: M (1 week)
**Lens consensus**: persona #4 education research (productive-failure pattern)
**Source docs**: [PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md](../../docs/design/PHOTO-UPLOAD-DIAGNOSTIC-001-10-persona-review.md)
**Assignee hint**: frontend + backend (mastery signal)
**Tags**: epic=epic-prr-j, ux, mastery, priority=p0
**Status**: Ready
**Source**: 10-persona photo-diagnostic review 2026-04-22
**Tier**: launch
**Epic**: [EPIC-PRR-J](EPIC-PRR-J-photo-upload-cas-diagnostic-chain.md)

---

## Goal

After reflection gate, two paths:
- Student retries and gets it right → celebration (small, not cartoonish) + mastery signal logged.
- Student still stuck → full misconception narration from template.

## Scope

- Retry path: student submits corrected step; CAS verifies; if correct, celebrate + emit mastery event.
- Full-narration path: render misconception-template explanation + example counter-case + suggested next step.
- No dark-pattern celebration (no streak/variable-reward mechanic — persona consensus + shipgate).
- Link into [EPIC-PRR-A StudentActor](EPIC-PRR-A-studentactor-decomposition.md) mastery engine.

## Files

- `src/student/full-version/src/components/diagnostic/ReflectionRetry.vue`
- `src/student/full-version/src/components/diagnostic/MisconceptionNarration.vue`
- Mastery-signal backend wire-up.

## Definition of Done

- Retry-success path logs mastery signal.
- Narration path renders template.
- Shipgate passes.

## Non-negotiable references

- [ADR-0048](../../docs/adr/0048-exam-prep-time-framing.md).
- Memory "Ship-gate banned terms" — no streaks.

## Reporting

complete via: standard queue complete.

## Related

- [PRR-380](TASK-PRR-380-diagnostic-result-screen.md), [EPIC-PRR-A](EPIC-PRR-A-studentactor-decomposition.md)

---

## Progress — 2026-04-23 (claude-subagent-prr381)

Backend mastery-signal slice shipped on branch
`claude-subagent-prr381/prr-381-mastery-signal`.

- `src/actors/Cena.Actors/Mastery/MasterySignalEmitted_V1.cs` — event record,
  `MasterySignalTrigger` constants, `MasterySignalOptions` (default delta 0.05).
- `src/actors/Cena.Actors/Mastery/IMasterySignalEmitter.cs` — interface +
  `InMemoryMasterySignalEmitter` (ConcurrentBag, test-inspectable) +
  `MartenMasterySignalEmitter` (per-student stream `masterysignal-{studentAnonId}`,
  FetchStreamStateAsync-guarded StartStream vs. Append).
- `src/actors/Cena.Actors/Mastery/PostReflectionMasteryService.cs` — only emits
  when `CasVerifyResult.Status == Ok && Verified == true`; returns null on
  CAS outage (no fake mastery, ADR-0002).
- `src/actors/Cena.Actors/Mastery/MasterySignalServiceRegistration.cs` —
  `AddMasterySignalServices` (InMemory default) + `AddMasterySignalServicesMarten`.
- `src/actors/Cena.Actors.Tests/Mastery/PostReflectionMasteryServiceTests.cs` —
  18 passing cases covering verified success, non-verified, 4 non-Ok statuses,
  default + override delta, trigger string constant, empty-identifier theory,
  null CAS, out-of-range options, banned-term audit on payload + field names.

Build: `dotnet build src/actors/Cena.Actors.sln` → 0 errors.
Tests: 18/18 pass.

### Deferred

Vue frontend components remain open and should be picked up as a separate
frontend task:

- `src/student/full-version/src/components/diagnostic/ReflectionRetry.vue`
  — retry-success celebration surface (no streak, no cartoonish reward).
- `src/student/full-version/src/components/diagnostic/MisconceptionNarration.vue`
  — renders the misconception-template explanation + counter-case (backend
  already in `MisconceptionTaxonomy`, no new API needed).
