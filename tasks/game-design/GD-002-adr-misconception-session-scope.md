# GD-002: ADR — Misconception state is session-scoped, not profile-scoped

## Goal
Produce `docs/adr/ADR-XXX-misconception-session-scope.md` binding the platform to: misconception telemetry lives inside a session aggregate and is never used to train global ML models, never persisted to a minor's long-lived profile, and never exported outside the session boundary.

## Source
`docs/research/cena-sexy-game-research-2026-04-11.md` — Track 8 findings (FTC v. Edmodo "Affected Work Product" consent decree; COPPA 2025 Final Rule; ICO v. Reddit £14.47M Feb 2026; GDPR-K Art. 8). Proposal Point 2 (misconception-as-enemy) must survive these constraints.

## Required decisions
1. Aggregate boundary: misconception tags live on `SessionState`, not `StudentState`. Define the events that carry them and the events that explicitly strip them on session end.
2. Retention: deleted with the session event stream after X days (X to be set — propose 30 for active remediation, 90 max).
3. Training data pipeline: misconception data MUST be excluded from any embedding or fine-tuning corpus used to build global models. Write the filter and put it under test.
4. Export path: admin exports of a student profile MUST NOT include historical misconception tags — only surface the currently-active-session's remediation notes.
5. Audit trail: one-line log per misconception event stating source session, topic, and retention horizon.
6. Cross-reference with existing `src/actors/Cena.Actors/Mastery/` aggregates and `EloDifficultyService` — mastery is OK to persist (aggregate learning signal), misconception profile is not.

## DoD
- ADR merged
- At least one existing code path audited for violation (flag to follow-up task)
- Session aggregate + event naming decided, schema task can start

## Reporting
Complete with branch + list of files that need later refactor.
